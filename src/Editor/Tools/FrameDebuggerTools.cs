using System;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Frame Debugger surface. The public API is thin — most of what we
    /// want lives on internal <see cref="UnityEditorInternal.FrameDebuggerUtility"/>.
    /// Reached reflectively so the tools compile on Unity versions where
    /// the symbols drift.
    /// </summary>
    internal static class FrameDebuggerTools
    {
        // In Unity 6 the type moved out of the UnityEditor.dll assembly.
        // Scan every loaded assembly by full name so we don't hard-code
        // a specific assembly-qualified name.
        private static readonly Lazy<Type> Util = new(() =>
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType("UnityEditorInternal.FrameDebuggerUtility", false); } catch { }
                if (t != null) return t;
            }
            return null;
        });

        // ---------- frame-debugger-status ----------
        [ReifyTool("frame-debugger-status")]
        public static Task<object> Status(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var t = Util.Value;
                if (t == null)
                    throw new InvalidOperationException(
                        "UnityEditorInternal.FrameDebuggerUtility not found — Unity API drift.");

                var count = GetStatic<int>(t, "count");
                var limit = GetStatic<int>(t, "limit");
                var isLocal = GetStatic<bool>(t, "IsLocalEnabled");
                var isRemote = GetStatic<bool>(t, "IsRemoteEnabled");
                var receivingEvents = GetStatic<bool>(t, "receivingRemoteEvents");

                return new
                {
                    is_enabled_local      = isLocal,
                    is_enabled_remote     = isRemote,
                    receiving_remote      = receivingEvents,
                    total_event_count     = count,
                    current_event_limit   = limit,
                    note = "enable via Window > Analysis > Frame Debugger in Unity; these tools only read state.",
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
                var t = Util.Value;
                if (t == null)
                    throw new InvalidOperationException(
                        "UnityEditorInternal.FrameDebuggerUtility not found — Unity API drift.");

                var before = GetStatic<bool>(t, "IsLocalEnabled");

                var setMethod = t.GetMethod("SetEnabled",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(bool), typeof(int) }, null);
                if (setMethod != null)
                {
                    setMethod.Invoke(null, new object[] { enabled, 0 });
                }
                else
                {
                    var alt = t.GetMethod(enabled ? "EnableFrameDebugger" : "DisableFrameDebugger",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (alt == null)
                        throw new InvalidOperationException(
                            "No SetEnabled/Enable*/Disable* method found on FrameDebuggerUtility.");
                    alt.Invoke(null, null);
                }

                var after = GetStatic<bool>(t, "IsLocalEnabled");

                return new
                {
                    before_enabled = before,
                    after_enabled  = after,
                    requested      = enabled,
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        private static T GetStatic<T>(Type type, string name)
        {
            var p = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) try { return (T)Convert.ChangeType(p.GetValue(null), typeof(T)); } catch { }
            var f = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) try { return (T)Convert.ChangeType(f.GetValue(null), typeof(T)); } catch { }
            return default;
        }
    }
}
