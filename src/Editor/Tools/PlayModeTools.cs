using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Play-mode control. State transitions are asynchronous in Unity — the
    /// handler returns once Unity has accepted the command, not once the
    /// reload and transition finish. Callers should poll play-mode-status if
    /// they need to observe the new state.
    ///
    /// Bridge survives the domain reload on play-mode enter/exit because
    /// ReifyBridge is [InitializeOnLoad] — the Editor re-runs its cctor after
    /// the reload and rebinds the HttpListener on the same port.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayModeTools
    {
        private static DateTime? _enteredUtc;

        static PlayModeTools()
        {
            EditorApplication.playModeStateChanged += OnStateChanged;
            if (EditorApplication.isPlaying) _enteredUtc = DateTime.UtcNow;
        }

        private static void OnStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode: _enteredUtc = DateTime.UtcNow; break;
                case PlayModeStateChange.ExitingPlayMode: _enteredUtc = null;            break;
            }
        }

        // ---------- play-mode-enter ----------
        public static Task<object> Enter(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (EditorApplication.isPlaying)
                    return Status("already_playing");
                EditorApplication.isPlaying = true;
                return Status("enter_queued");
            });
        }

        // ---------- play-mode-exit ----------
        public static Task<object> Exit(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!EditorApplication.isPlaying)
                    return Status("already_stopped");
                EditorApplication.isPlaying = false;
                return Status("exit_queued");
            });
        }

        // ---------- play-mode-pause ----------
        public static Task<object> Pause(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!EditorApplication.isPlaying)
                    throw new InvalidOperationException("Not in play mode — nothing to pause.");
                EditorApplication.isPaused = true;
                return Status("paused");
            });
        }

        // ---------- play-mode-resume ----------
        public static Task<object> Resume(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!EditorApplication.isPlaying)
                    throw new InvalidOperationException("Not in play mode — nothing to resume.");
                EditorApplication.isPaused = false;
                return Status("resumed");
            });
        }

        // ---------- play-mode-step ----------
        public static Task<object> Step(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!EditorApplication.isPlaying)
                    throw new InvalidOperationException("Not in play mode — step is only valid during play.");
                if (!EditorApplication.isPaused)
                    throw new InvalidOperationException("Not paused — pause first, then step.");
                EditorApplication.Step();
                return Status("stepped");
            });
        }

        // ---------- play-mode-status ----------
        public static Task<object> StatusTool(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() => Status("read"));
        }

        // ---------- helper ----------
        private static object Status(string action)
        {
            string state;
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
                state = "edit";
            else if (EditorApplication.isPaused && EditorApplication.isPlaying)
                state = "paused";
            else if (EditorApplication.isPlaying)
                state = "playing";
            else
                state = "transitioning";

            var secondsSinceEntered = _enteredUtc.HasValue
                ? (DateTime.UtcNow - _enteredUtc.Value).TotalSeconds
                : (double?)null;

            return new
            {
                action,
                state,
                is_playing                         = EditorApplication.isPlaying,
                is_paused                          = EditorApplication.isPaused,
                is_playing_or_will_change_playmode = EditorApplication.isPlayingOrWillChangePlaymode,
                is_compiling                       = EditorApplication.isCompiling,
                entered_at_utc                     = _enteredUtc?.ToString("o"),
                seconds_since_entered              = secondsSinceEntered,
                frame                              = (long)Time.frameCount,
                realtime_since_startup             = Time.realtimeSinceStartup,
                read_at_utc                        = DateTime.UtcNow.ToString("o")
            };
        }
    }
}
