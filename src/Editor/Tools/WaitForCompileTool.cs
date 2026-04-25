using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Blocking compile-and-event waits. Replaces the
    /// "request-compile + poll-status + read-console" three-call dance
    /// agents do every iteration. Both tools are bounded by an explicit
    /// timeout (max 60s) so they can't hang the bridge indefinitely.
    /// </summary>
    [InitializeOnLoad]
    internal static class WaitForCompileTool
    {
        private const string KeyLastCompileFinished = "Reify.LastCompileFinishedUtc";
        private const string KeyLastReload          = "Reify.LastDomainReloadUtc";
        private const string KeyLastPlayModeChange  = "Reify.LastPlayModeChangeUtc";

        static WaitForCompileTool()
        {
            // Piggyback on existing SessionState keys written by
            // DomainReloadStatusTool; add our own play-mode marker so
            // editor-await-event can detect a play-mode transition.
            EditorApplication.playModeStateChanged += _ =>
                SessionState.SetString(KeyLastPlayModeChange, DateTime.UtcNow.ToString("o"));
        }

        // ---------- editor-await-compile ----------
        // Blocks until the editor finishes compiling, OR until timeout.
        // Returns the structured error/warning list immediately on completion
        // — no separate console-log-read call needed.
        //
        // IMPORTANT: this tool ONLY waits. It does not request the compile.
        // Reason: a successful compile triggers a domain reload, the reload
        // unloads the AppDomain that owns this awaiting Task, and the
        // continuation either never resumes or resumes in a torched context
        // (NullReferenceException). To start a compile, call the existing
        // editor-request-script-compilation FIRST, then call this one to wait.
        // The deprecated request_first arg is now no-op; its prior behavior
        // forced an NRE roughly 100% of the time on a real compile.
        [ReifyTool("editor-await-compile")]
        public static async Task<object> AwaitCompile(JToken args)
        {
            var timeoutSeconds = Math.Clamp(args?.Value<double?>("timeout_seconds") ?? 30.0, 0.5, 60.0);
            var requestFirst   = args?.Value<bool?>("request_first") ?? false;  // legacy arg, ignored
            var pollMs         = Math.Clamp(args?.Value<int?>("poll_ms") ?? 200, 50, 2000);

            // Snapshot the "before" reload timestamp; we'll consider compile done
            // when either isCompiling goes false AND a reload has fired since,
            // OR no reload was needed and isCompiling is false at start.
            // SessionState.GetString and CompilationPipeline.RequestScriptCompilation
            // BOTH require the main thread — the handler runs on threadpool when
            // the bridge dispatches, so wrap every Unity API touch.
            var startedUtc = DateTime.UtcNow;
            var beforeReload = await MainThreadDispatcher.RunAsync<string>(
                () => SessionState.GetString(KeyLastReload, ""));

            // request_first is intentionally ignored — see header comment.
            // Caller starts the compile via editor-request-script-compilation,
            // then calls this tool to wait.
            _ = requestFirst;

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            bool everSawCompiling = false;
            string lastReloadSeen = beforeReload;
            string completionReason = "timeout";

            // No-op compile detection: when request_first=true but the request
            // turns out to be a no-op (no changed files → no compile → no
            // reload), nothing ever flips isCompiling and no reload fires. The
            // old logic only completed on (reload_fired || (!requestFirst &&
            // !everSawCompiling)), so this case timed out at the full deadline.
            // Treat "we requested, but nothing happened in this settle window"
            // as a successful no-op so callers don't pay 30s for a noop compile.
            var noOpSettleAt = DateTime.UtcNow.AddMilliseconds(2000);

            while (DateTime.UtcNow < deadline)
            {
                var (compiling, lastReload) = await MainThreadDispatcher.RunAsync<(bool, string)>(() =>
                    (EditorApplication.isCompiling, SessionState.GetString(KeyLastReload, "")));

                if (compiling) everSawCompiling = true;
                lastReloadSeen = lastReload;

                bool reloadFired = !string.IsNullOrEmpty(lastReload) && lastReload != beforeReload;

                if (!compiling)
                {
                    if (reloadFired)
                        { completionReason = "reload_fired";  break; }
                    if (everSawCompiling)
                        { completionReason = "compile_finished_no_reload"; break; }
                    if (!requestFirst)
                        { completionReason = "already_idle"; break; }
                    if (DateTime.UtcNow >= noOpSettleAt)
                        { completionReason = "no_op_compile";  break; }
                }
                await Task.Delay(pollMs);
            }

            var totalMs = (DateTime.UtcNow - startedUtc).TotalMilliseconds;
            var timedOut = DateTime.UtcNow >= deadline;

            // Pull current compile errors + final SessionState read on the
            // main thread — these all touch Unity APIs.
            var (errors, failed, lastCompileFinished) = await MainThreadDispatcher.RunAsync<(CompileErrorsTool.CompileErrorSnapshot, bool, string)>(() =>
                (CompileErrorsTool.SnapshotCurrent(),
                 EditorUtility.scriptCompilationFailed,
                 SessionState.GetString(KeyLastCompileFinished, null)));

            return new
            {
                succeeded                  = !timedOut && !failed,
                timed_out                  = timedOut,
                completion_reason          = completionReason,
                ever_saw_compiling         = everSawCompiling,
                reload_fired               = lastReloadSeen != beforeReload,
                script_compilation_failed  = failed,
                duration_ms                = (long)totalMs,
                error_count                = errors.errors.Count,
                warning_count              = errors.warnings.Count,
                errors                     = errors.errors,
                warnings                   = errors.warnings,
                last_compile_finished_utc  = lastCompileFinished,
                last_domain_reload_utc     = lastReloadSeen,
                read_at_utc                = DateTime.UtcNow.ToString("o")
            };
        }

        // ---------- editor-await-event ----------
        // Long-poll for one of: compile_done, reload_done, play_mode_changed.
        // Returns immediately when the targeted event fires after the call
        // started, or the timeout elapses.
        [ReifyTool("editor-await-event")]
        public static async Task<object> AwaitEvent(JToken args)
        {
            var eventsArg = args?["events"] as JArray;
            var events = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (eventsArg != null)
                foreach (var e in eventsArg) events.Add(e.Value<string>());
            if (events.Count == 0)
            {
                events.Add("compile_done");
                events.Add("reload_done");
                events.Add("play_mode_changed");
            }

            var timeoutSeconds = Math.Clamp(args?.Value<double?>("timeout_seconds") ?? 15.0, 0.5, 60.0);
            var pollMs         = Math.Clamp(args?.Value<int?>("poll_ms") ?? 200, 50, 2000);

            // SessionState.GetString must run on the main thread.
            var (baseCompile, baseReload, basePlayMode) =
                await MainThreadDispatcher.RunAsync<(string, string, string)>(() => (
                    SessionState.GetString(KeyLastCompileFinished, ""),
                    SessionState.GetString(KeyLastReload, ""),
                    SessionState.GetString(KeyLastPlayModeChange, "")));

            var startedUtc = DateTime.UtcNow;
            var deadline   = startedUtc.AddSeconds(timeoutSeconds);

            while (DateTime.UtcNow < deadline)
            {
                var (curCompile, curReload, curPlay) = await MainThreadDispatcher.RunAsync<(string, string, string)>(() => (
                    SessionState.GetString(KeyLastCompileFinished, ""),
                    SessionState.GetString(KeyLastReload, ""),
                    SessionState.GetString(KeyLastPlayModeChange, "")));

                string fired = null;
                if (events.Contains("compile_done")     && curCompile  != baseCompile)  fired = "compile_done";
                else if (events.Contains("reload_done") && curReload   != baseReload)   fired = "reload_done";
                else if (events.Contains("play_mode_changed") && curPlay != basePlayMode) fired = "play_mode_changed";

                if (fired != null)
                {
                    return new
                    {
                        event_fired   = fired,
                        timed_out     = false,
                        wait_ms       = (long)(DateTime.UtcNow - startedUtc).TotalMilliseconds,
                        last_compile_finished_utc = curCompile,
                        last_domain_reload_utc    = curReload,
                        last_play_mode_change_utc = curPlay,
                        read_at_utc   = DateTime.UtcNow.ToString("o")
                    };
                }
                await Task.Delay(pollMs);
            }

            return new
            {
                event_fired = (string)null,
                timed_out   = true,
                wait_ms     = (long)(DateTime.UtcNow - startedUtc).TotalMilliseconds,
                read_at_utc = DateTime.UtcNow.ToString("o")
            };
        }
    }
}
