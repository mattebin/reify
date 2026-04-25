using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// One-call combined health probe. Replaces the
    /// "ping + domain-reload-status + tests-status + console-log-read"
    /// quartet an agent does at the start of every turn to figure out
    /// "is the bridge alive AND is the editor in a state where my next
    /// call will succeed?".
    /// </summary>
    [InitializeOnLoad]
    internal static class ReifyHealthTool
    {
        private static readonly DateTime BridgeStartUtc = DateTime.UtcNow;

        // ---------- reify-health ----------
        [ReifyTool("reify-health")]
        public static Task<object> Health(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var compiling     = EditorApplication.isCompiling;
                var updating      = EditorApplication.isUpdating;
                var playing       = EditorApplication.isPlaying;
                var paused        = EditorApplication.isPaused;
                var compileFailed = EditorUtility.scriptCompilationFailed;

                string state;
                if (compileFailed)              state = "compile_failed";
                else if (compiling || updating) state = "compiling";
                else if (paused && playing)     state = "play_paused";
                else if (playing)               state = "play";
                else                            state = "edit_idle";

                bool ready = !compiling && !updating && !compileFailed;

                // Tool count (uses the same enumeration the self-check uses).
                int toolCount = 0;
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type[] types;
                        try { types = asm.GetTypes(); } catch { continue; }
                        foreach (var t in types)
                            foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                                if (m.GetCustomAttribute<ReifyToolAttribute>() != null) toolCount++;
                    }
                }
                catch { /* ignore */ }

                int errorCount = 0, warningCount = 0;
                try
                {
                    var snap = CompileErrorsTool.SnapshotCurrent();
                    errorCount   = snap.errors.Count;
                    warningCount = snap.warnings.Count;
                }
                catch { /* tolerate reflection misses */ }

                var uptime = (DateTime.UtcNow - BridgeStartUtc);

                return new
                {
                    state,
                    ready,
                    is_compiling          = compiling,
                    is_updating           = updating,
                    is_playing            = playing,
                    is_paused             = paused,
                    script_compilation_failed = compileFailed,
                    tool_count            = toolCount,
                    error_count_in_console = errorCount,
                    warning_count_in_console = warningCount,
                    bridge_uptime_seconds = (long)uptime.TotalSeconds,
                    process_id            = Process.GetCurrentProcess().Id,
                    unity_version         = Application.unityVersion,
                    project_path          = System.IO.Path.GetFullPath(System.IO.Directory.GetCurrentDirectory()),
                    has_focus             = UnityEditorInternal.InternalEditorUtility.isApplicationActive,
                    read_at_utc           = DateTime.UtcNow.ToString("o"),
                    frame                 = (long)Time.frameCount
                };
            });
        }
    }
}
