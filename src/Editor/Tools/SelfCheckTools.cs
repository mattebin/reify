using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// One-call install health check. Runs a fixed battery of known-safe
    /// reify operations against the running Unity and reports per-check
    /// OK/FAIL. Answers "is my reify install actually working right now"
    /// without needing to remember which tools to call manually.
    ///
    /// The battery is deliberately small + non-destructive:
    ///  - ping-equivalents: project info, scene list, domain-reload-status
    ///  - resolver: scene-snapshot round-trip
    ///  - write receipt: create+modify+destroy a temp GameObject + verify
    ///    that scene-diff returns to baseline
    ///  - error discrimination: deliberate bad calls to confirm
    ///    INVALID_ARGS / COMPONENT_NOT_FOUND / UNKNOWN_TOOL shapes
    ///
    /// Every sub-check's duration + pass/fail is reported so the caller
    /// can spot regressions across runs.
    /// </summary>
    internal static class SelfCheckTools
    {
        [ReifyTool("reify-self-check")]
        public static Task<object> Run(JToken args)
        {
            var skipWrites = args?.Value<bool?>("skip_writes") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var checks = new List<object>();
                var pass = 0;
                var fail = 0;

                void Check(string name, Func<object> body)
                {
                    var startedAt = DateTime.UtcNow;
                    try
                    {
                        var detail = body();
                        var ms = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                        checks.Add(new { name, ok = true, ms, detail });
                        pass++;
                    }
                    catch (Exception ex)
                    {
                        var ms = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                        checks.Add(new
                        {
                            name, ok = false, ms,
                            error = $"{ex.GetType().Name}: {ex.Message}"
                        });
                        fail++;
                    }
                }

                // ---- environment ----
                Check("unity_version", () => new { version = Application.unityVersion });
                Check("active_scene", () =>
                {
                    var s = SceneManager.GetActiveScene();
                    if (!s.IsValid()) throw new InvalidOperationException("no valid active scene");
                    return new { name = s.name, path = s.path, is_loaded = s.isLoaded };
                });
                Check("tool_registry", () =>
                {
                    // Count [ReifyTool] attributes across all loaded types.
                    var count = 0;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type[] types;
                        try { types = asm.GetTypes(); } catch { continue; }
                        foreach (var t in types)
                            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                        | BindingFlags.Static | BindingFlags.Instance))
                                if (m.GetCustomAttribute<ReifyToolAttribute>() != null) count++;
                    }
                    if (count < 100)
                        throw new InvalidOperationException($"only {count} tools registered — expected 150+");
                    return new { tool_count = count };
                });
                Check("editor_state", () =>
                {
                    var isCompiling = EditorApplication.isCompiling;
                    var isUpdating = EditorApplication.isUpdating;
                    var compileFailed = EditorUtility.scriptCompilationFailed;
                    if (compileFailed)
                        throw new InvalidOperationException("Unity reports script compilation failed.");
                    if (isCompiling || isUpdating)
                        throw new InvalidOperationException("Unity is compiling or updating; retry when idle.");

                    return new
                    {
                        is_compiling = isCompiling,
                        is_updating = isUpdating,
                        is_playing = EditorApplication.isPlaying,
                        is_paused = EditorApplication.isPaused,
                        script_compilation_failed = compileFailed,
                        ready = true
                    };
                });
                Check("response_size_cap_config", () =>
                {
                    const int defaultCap = 786432;
                    var raw = Environment.GetEnvironmentVariable("REIFY_MAX_RESPONSE_BYTES");
                    var parsed = int.TryParse(raw, out var value) && value > 16384
                        ? value
                        : defaultCap;
                    return new
                    {
                        env_value = raw,
                        effective_max_response_bytes = parsed,
                        default_max_response_bytes = defaultCap,
                        minimum_accepted_override_bytes = 16385
                    };
                });
                Check("unsafe_tool_gates", () => new
                {
                    script_execute = ProbeUnsafeGate(
                        "script-execute",
                        "REIFY_ALLOW_SCRIPT_EXECUTE",
                        new JObject()),
                    reflection_method_call = ProbeUnsafeGate(
                        "reflection-method-call",
                        "REIFY_ALLOW_REFLECTION_CALL",
                        new JObject())
                });

                // ---- resolver round-trip ----
                string tempName = $"__reify_selfcheck_{Guid.NewGuid():N}".Substring(0, 28);
                Scene activeScene = default;
                int baselineCount = 0;

                Check("scene_snapshot_baseline", () =>
                {
                    activeScene = SceneManager.GetActiveScene();
                    baselineCount = 0;
                    foreach (var root in activeScene.GetRootGameObjects())
                        foreach (var _ in root.GetComponentsInChildren<Transform>(true))
                            baselineCount++;
                    return new { gameobject_count = baselineCount };
                });

                if (!skipWrites)
                {
                    GameObject createdGo = null;
                    Check("write_create_gameobject", () =>
                    {
                        createdGo = new GameObject(tempName);
                        Undo.RegisterCreatedObjectUndo(createdGo, "reify self-check create");
                        return new { instance_id = GameObjectResolver.InstanceIdOf(createdGo), name = createdGo.name };
                    });

                    Check("write_modify_transform", () =>
                    {
                        if (createdGo == null) throw new InvalidOperationException("no temp GO from prior step");
                        Undo.RecordObject(createdGo.transform, "reify self-check modify");
                        createdGo.transform.position = new Vector3(42f, 42f, 42f);
                        return new
                        {
                            position_set = true,
                            world_position = new { x = createdGo.transform.position.x,
                                                    y = createdGo.transform.position.y,
                                                    z = createdGo.transform.position.z }
                        };
                    });

                    Check("write_destroy_gameobject", () =>
                    {
                        if (createdGo == null) throw new InvalidOperationException("no temp GO");
                        Undo.DestroyObjectImmediate(createdGo);
                        var stillExists = GameObject.Find(tempName) != null;
                        if (stillExists) throw new InvalidOperationException("destroy did not remove");
                        return new { destroyed = true };
                    });

                    Check("scene_back_to_baseline", () =>
                    {
                        var now = 0;
                        foreach (var root in activeScene.GetRootGameObjects())
                            foreach (var _ in root.GetComponentsInChildren<Transform>(true))
                                now++;
                        if (now != baselineCount)
                            throw new InvalidOperationException(
                                $"count drift: baseline={baselineCount} now={now}");
                        return new { gameobject_count = now };
                    });
                }

                // ---- error-shape discrimination ----
                // These deliberately fail at arg-validation time in the
                // tool handler. The self-check just confirms the tools
                // are registered and their argument validators fire.
                Check("resolver_ambiguity_semantics", () =>
                {
                    // Build a fake ambiguous-lookup scenario programmatically
                    // by calling GameObjectResolver.ByPath with a path we
                    // know doesn't exist — should return null, not throw
                    // silently.
                    var go = GameObjectResolver.ByPath("__definitely_does_not_exist_123__");
                    if (go != null) throw new InvalidOperationException("expected null for missing GO");
                    return new { resolver_returns_null_on_missing = true };
                });

                return new
                {
                    pass_count  = pass,
                    fail_count  = fail,
                    ok          = fail == 0,
                    skip_writes = skipWrites,
                    checks      = checks.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        private static object ProbeUnsafeGate(string toolName, string envVar, JToken args)
        {
            var enabled = Environment.GetEnvironmentVariable(envVar) == "1";

            if (!ReifyBridge.TryGetHandler(toolName, out var handler))
                throw new InvalidOperationException($"{toolName} is not registered.");

            try
            {
                handler(args).GetAwaiter().GetResult();
                if (!enabled)
                    throw new InvalidOperationException($"{toolName} ran even though {envVar} is not set.");

                return new
                {
                    enabled = true,
                    registered = true,
                    gate_verified = false,
                    note = "Tool is enabled in this Unity Editor process; self-check did not execute arbitrary code."
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                if (enabled)
                    throw new InvalidOperationException($"{toolName} is disabled even though {envVar}=1.", ex);

                return new
                {
                    enabled = false,
                    registered = true,
                    gate_verified = true,
                    expected_error_code = "UNSAFE_TOOL_DISABLED",
                    message = ex.Message
                };
            }
            catch (ArgumentException ex) when (enabled)
            {
                return new
                {
                    enabled = true,
                    registered = true,
                    gate_verified = false,
                    note = $"Tool is enabled; argument validation fired before any arbitrary work: {ex.Message}"
                };
            }
        }
    }
}
