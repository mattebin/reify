using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// 9th Phase C philosophy tool (closes gap 11). Reports the "is Unity
    /// ready for another tool call right now?" state. Tracks compile and
    /// domain-reload timestamps across reloads via SessionState so a recent
    /// reload is still visible post-reboot.
    /// </summary>
    [InitializeOnLoad]
    internal static class DomainReloadStatusTool
    {
        private const string KeyLastCompile      = "Reify.LastCompileFinishedUtc";
        private const string KeyLastReload       = "Reify.LastDomainReloadUtc";
        private const string KeyLastCompileStart = "Reify.LastCompileStartedUtc";

        static DomainReloadStatusTool()
        {
            SessionState.SetString(KeyLastReload, DateTime.UtcNow.ToString("o"));
            CompilationPipeline.compilationStarted  += OnCompileStarted;
            CompilationPipeline.compilationFinished += OnCompileFinished;
        }

        private static void OnCompileStarted(object _)
            => SessionState.SetString(KeyLastCompileStart, DateTime.UtcNow.ToString("o"));
        private static void OnCompileFinished(object _)
            => SessionState.SetString(KeyLastCompile, DateTime.UtcNow.ToString("o"));

        public static Task<object> Handle(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var compiling     = EditorApplication.isCompiling;
                var updating      = EditorApplication.isUpdating;
                var playing       = EditorApplication.isPlaying;
                var paused        = EditorApplication.isPaused;
                var transitioning = EditorApplication.isPlayingOrWillChangePlaymode && !playing;
                var busy          = compiling || updating || transitioning;

                string appState;
                if (transitioning) appState = playing ? "exit_play" : "enter_play";
                else if (paused && playing) appState = "pause";
                else if (playing)           appState = "play";
                else                        appState = "edit";

                var lastCompile      = SessionState.GetString(KeyLastCompile, null);
                var lastCompileStart = SessionState.GetString(KeyLastCompileStart, null);
                var lastReload       = SessionState.GetString(KeyLastReload, null);

                var warnings = new System.Collections.Generic.List<string>();
                if (compiling)
                    warnings.Add("Editor is compiling — tool calls may fail or return stale state until compile completes.");
                if (updating)
                    warnings.Add("Editor is updating (asset import / database refresh) — prefer waiting.");
                if (transitioning)
                    warnings.Add("Editor is transitioning play mode — wait for the transition to settle.");
                if (!string.IsNullOrEmpty(lastReload)
                    && DateTime.TryParse(lastReload, out var reloadAt)
                    && (DateTime.UtcNow - reloadAt).TotalSeconds < 2)
                    warnings.Add("Recent domain reload (< 2s ago) — cached instance_ids from before the reload are invalidated.");

                return new
                {
                    is_compiling                = compiling,
                    is_updating                 = updating,
                    is_playing                  = playing,
                    is_paused                   = paused,
                    is_transitioning_play_mode  = transitioning,
                    application_state           = appState,
                    has_focus                   = UnityEditorInternal.InternalEditorUtility.isApplicationActive,
                    is_busy                     = busy,
                    last_compile_started_utc    = lastCompileStart,
                    last_compile_finished_utc   = lastCompile,
                    last_domain_reload_utc      = lastReload,
                    warnings                    = warnings.ToArray(),
                    read_at_utc                 = DateTime.UtcNow.ToString("o"),
                    frame                       = (long)Time.frameCount
                };
            });
        }
    }
}
