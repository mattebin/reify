using System;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Frame Debugger surface. Unity moved / renamed the editor API
    /// across versions:
    ///  - 2019..5: `UnityEditorInternal.FrameDebuggerUtility`
    ///  - Unity 6: public `UnityEditor.FrameDebugger` with `enabled` +
    ///    `EnableFrameDebugger` / `DisableFrameDebugger`
    /// We try each candidate name across every loaded assembly and fall
    /// back to property/method probes so the tools work regardless of
    /// which generation of Unity is running.
    /// </summary>
    internal static class FrameDebuggerTools
    {
        private static readonly string[] CandidateTypeNames =
        {
            "UnityEditorInternal.FrameDebuggerUtility",
            "UnityEditor.FrameDebuggerUtility",
            "UnityEditor.Rendering.FrameDebuggerUtility",
            "UnityEditor.FrameDebugger",
            "UnityEngine.Rendering.FrameDebugger"
        };

        private static readonly Lazy<Type> Util = new(() =>
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var name in CandidateTypeNames)
                {
                    Type t = null;
                    try { t = asm.GetType(name, false); } catch { }
                    if (t != null) return t;
                }
            }
            return null;
        });

        // ---------- frame-debugger-status ----------
        [ReifyTool("frame-debugger-status")]
        public static Task<object> Status(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var t = Util.Value
                    ?? throw new InvalidOperationException(BuildUnsupportedMessage());

                // Field/property names drift too — probe for any of:
                //   IsLocalEnabled (pre-6) / enabled (Unity 6)
                var isEnabled =
                    TryReadStaticBool(t, "enabled")
                    ?? TryReadStaticBool(t, "IsLocalEnabled")
                    ?? false;
                var isRemote = TryReadStaticBool(t, "IsRemoteEnabled") ?? false;
                var receiving = TryReadStaticBool(t, "receivingRemoteEvents") ?? false;
                var count = TryReadStaticInt(t, "count") ?? 0;
                var limit = TryReadStaticInt(t, "limit") ?? 0;

                return new
                {
                    is_enabled            = isEnabled,
                    is_enabled_remote     = isRemote,
                    receiving_remote      = receiving,
                    total_event_count     = count,
                    current_event_limit   = limit,
                    resolved_type         = t.FullName,
                    resolved_assembly     = t.Assembly.GetName().Name,
                    note = "enable via Window > Analysis > Frame Debugger in Unity.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- frame-debugger-set-enabled ----------
        [ReifyTool("frame-debugger-set-enabled")]
        public static Task<object> SetEnabled(JToken args)
        {
            var enabled = args?.Value<bool?>("enabled")
                ?? throw new ArgumentException("enabled (bool) is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var t = Util.Value
                    ?? throw new InvalidOperationException(BuildUnsupportedMessage());

                var before = TryReadStaticBool(t, "enabled")
                    ?? TryReadStaticBool(t, "IsLocalEnabled") ?? false;

                // Try every known method shape; first one that accepts the
                // call wins.
                var invoked = TryInvokeSetEnabled(t, enabled);
                if (!invoked)
                    throw new InvalidOperationException(
                        $"No compatible SetEnabled / EnableFrameDebugger / DisableFrameDebugger " +
                        $"method on {t.FullName}. Unity API has drifted; frame-debugger-* " +
                        $"needs a port for this build.");

                var after = TryReadStaticBool(t, "enabled")
                    ?? TryReadStaticBool(t, "IsLocalEnabled") ?? false;

                return new
                {
                    before_enabled = before,
                    after_enabled  = after,
                    requested      = enabled,
                    resolved_type  = t.FullName,
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static bool TryInvokeSetEnabled(Type t, bool enabled)
        {
            // SetEnabled(bool, int)
            var m1 = t.GetMethod("SetEnabled",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(bool), typeof(int) }, null);
            if (m1 != null) { m1.Invoke(null, new object[] { enabled, 0 }); return true; }

            // SetEnabled(bool)
            var m2 = t.GetMethod("SetEnabled",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(bool) }, null);
            if (m2 != null) { m2.Invoke(null, new object[] { enabled }); return true; }

            // EnableFrameDebugger() / DisableFrameDebugger()
            var name = enabled ? "EnableFrameDebugger" : "DisableFrameDebugger";
            var m3 = t.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m3 != null) { m3.Invoke(null, null); return true; }

            // enabled { set; }
            var p = t.GetProperty("enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite) { p.SetValue(null, enabled); return true; }

            return false;
        }

        private static bool? TryReadStaticBool(Type t, string name)
        {
            try
            {
                var p = t.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(null);
                var f = t.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(null);
            }
            catch { }
            return null;
        }

        private static int? TryReadStaticInt(Type t, string name)
        {
            try
            {
                var p = t.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(int)) return (int)p.GetValue(null);
                var f = t.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(int)) return (int)f.GetValue(null);
            }
            catch { }
            return null;
        }

        private static string BuildUnsupportedMessage()
            => "Frame Debugger API not found under any of: "
               + string.Join(", ", CandidateTypeNames)
               + ". This Unity build exposes the Frame Debugger under a different name; "
               + "the tool needs an update. Use Window > Analysis > Frame Debugger manually.";
    }
}
