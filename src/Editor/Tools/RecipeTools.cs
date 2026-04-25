using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Named composite recipes — turn the multi-call dances agents do
    /// every iteration into single tool invocations. batch-execute is
    /// the generic; these are the sharp specializations for the
    /// most-frequent flows.
    /// </summary>
    internal static class RecipeTools
    {
        // ---------- recipe-compile-and-report ----------
        // Two-call recipe collapsed to one: request the compile, then wait.
        // The two-step structure is required because await CANNOT survive
        // the domain reload that a successful compile triggers — the reload
        // unloads the AppDomain holding the awaiting Task. So we do this in
        // a specific order:
        //   1. Issue the compile request (returns immediately, fire-and-forget)
        //   2. Pause briefly to let Unity flip isCompiling=true
        //   3. Begin awaiting. The await observes the running compile, then
        //      a NEW invocation of this recipe (or the lower-level await tool)
        //      after reload picks up the structured errors.
        // The first call returns either "compile_in_progress" (if the wait
        // was killed by the reload mid-flight) or full success.
        [ReifyTool("recipe-compile-and-report")]
        public static async Task<object> CompileAndReport(JToken args)
        {
            var timeoutSeconds = args?.Value<double?>("timeout_seconds") ?? 30.0;

            // Step 1: request via existing tool (fire-and-return, no AppDomain risk).
            object requestResult = null;
            if (Bridge.ReifyBridge.TryGetHandler("editor-request-script-compilation", out var req))
                requestResult = await req(new JObject());

            // Step 2: brief settle so isCompiling can flip.
            await Task.Delay(200);

            // Step 3: wait. This may resolve as "no_op_compile" (nothing changed)
            // or "compile_finished_no_reload" (compile done, no reload triggered)
            // or, if a reload IS triggered mid-await, the underlying Task is
            // killed by AppDomain unload and the bridge sees a connection drop.
            // Caller can then re-issue this recipe (or call editor-await-compile
            // alone) after the reload settles.
            var awaitArgs = new JObject
            {
                ["timeout_seconds"] = timeoutSeconds,
                ["request_first"]   = false
            };
            object awaitResult;
            try { awaitResult = await WaitForCompileTool.AwaitCompile(awaitArgs); }
            catch (Exception ex)
            {
                awaitResult = new
                {
                    succeeded = false,
                    note = "await_compile threw — likely because a domain reload " +
                           "fired mid-flight. Re-issue recipe-compile-and-report " +
                           "or call editor-await-compile after the reload completes.",
                    exception = ex.Message
                };
            }

            return new
            {
                recipe = "compile-and-report",
                steps  = new[] { "request_compilation", "settle_200ms", "await_compile" },
                request_result       = requestResult,
                await_compile_result = awaitResult,
                read_at_utc          = DateTime.UtcNow.ToString("o")
            };
        }

        // ---------- recipe-enter-play-and-snapshot ----------
        // Enter play mode, wait N frames for the scene to settle, then
        // snapshot a list of GameObjects matching name_contains. Useful
        // for "press play and tell me what spawned" without writing a
        // 4-call orchestration.
        [ReifyTool("recipe-enter-play-and-snapshot")]
        public static async Task<object> EnterPlayAndSnapshot(JToken args)
        {
            var nameContains = args?.Value<string>("name_contains");
            var settleFrames = Math.Clamp(args?.Value<int?>("settle_frames") ?? 60, 1, 600);
            var snapshotLimit = Math.Clamp(args?.Value<int?>("snapshot_limit") ?? 50, 1, 500);

            // Step 1: enter play mode (uses existing handler).
            object enterResult = null;
            if (Bridge.ReifyBridge.TryGetHandler("play-mode-enter", out var enter))
                enterResult = await enter(new JObject());

            // Step 2: wait for play-mode-changed event up to ~5s.
            object playEvent = null;
            if (Bridge.ReifyBridge.TryGetHandler("editor-await-event", out var awaitEvt))
                playEvent = await awaitEvt(new JObject {
                    ["events"] = new JArray("play_mode_changed"),
                    ["timeout_seconds"] = 5.0
                });

            // Step 3: tick frames (Time.frameCount via repeated update calls).
            await WaitFrames(settleFrames);

            // Step 4: snapshot using scene-query.
            object queryResult = null;
            if (Bridge.ReifyBridge.TryGetHandler("scene-query", out var query))
            {
                var qArgs = new JObject { ["limit"] = snapshotLimit };
                if (!string.IsNullOrEmpty(nameContains))
                    qArgs["name_contains"] = nameContains;
                queryResult = await query(qArgs);
            }

            return new
            {
                recipe = "enter-play-and-snapshot",
                steps  = new[] { "play_mode_enter", "await_play_mode_changed", $"settle_{settleFrames}_frames", "scene_query" },
                enter_result    = enterResult,
                play_mode_event = playEvent,
                snapshot        = queryResult,
                read_at_utc     = DateTime.UtcNow.ToString("o")
            };
        }

        // ---------- recipe-compile-then-test ----------
        // Compile, await, only run tests if the compile succeeded.
        // Returns the test job id so the caller can poll tests-status,
        // OR the compile errors so the caller knows why testing was
        // skipped. Single round-trip for "did my change break anything?".
        [ReifyTool("recipe-compile-then-test")]
        public static async Task<object> CompileThenTest(JToken args)
        {
            var timeoutSeconds = args?.Value<double?>("compile_timeout_seconds") ?? 30.0;
            var assemblyName   = args?.Value<string>("assembly_name");
            var mode           = args?.Value<string>("mode") ?? "EditMode";

            // Same two-step pattern as recipe-compile-and-report: request via the
            // existing fire-and-return tool, then await separately. Wrapped in
            // try/catch because the await may be killed by the post-compile
            // domain reload.
            object requestResult = null;
            if (Bridge.ReifyBridge.TryGetHandler("editor-request-script-compilation", out var req))
                requestResult = await req(new JObject());
            await Task.Delay(200);

            var compileArgs = new JObject
            {
                ["timeout_seconds"] = timeoutSeconds,
                ["request_first"]   = false
            };
            object compileResult;
            try { compileResult = await WaitForCompileTool.AwaitCompile(compileArgs); }
            catch (Exception ex)
            {
                compileResult = new { succeeded = false, exception = ex.Message };
            }
            var compileJObj = JObject.FromObject(compileResult);
            var compileOk = compileJObj.Value<bool?>("succeeded") ?? false;

            object testRunResult = null;
            if (compileOk && Bridge.ReifyBridge.TryGetHandler("tests-run", out var run))
            {
                var runArgs = new JObject { ["mode"] = mode };
                if (!string.IsNullOrEmpty(assemblyName)) runArgs["assembly_name"] = assemblyName;
                try { testRunResult = await run(runArgs); }
                catch (Exception ex) { testRunResult = new { error = ex.Message }; }
            }

            return new
            {
                recipe          = "compile-then-test",
                steps           = compileOk
                    ? new[] { "request_compilation", "await_compile", "tests_run" }
                    : new[] { "request_compilation", "await_compile", "skipped_tests_due_to_compile_errors" },
                compile         = compileResult,
                tests_run       = testRunResult,
                tests_started   = compileOk && testRunResult != null,
                read_at_utc     = DateTime.UtcNow.ToString("o")
            };
        }

        private static Task WaitFrames(int frames)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int remaining = frames;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (--remaining <= 0)
                {
                    EditorApplication.update -= tick;
                    tcs.TrySetResult(true);
                }
            };
            EditorApplication.update += tick;
            return tcs.Task;
        }
    }
}
