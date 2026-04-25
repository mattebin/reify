using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class CommandCenterTool
    {
        [ReifyTool("reify-command-center-open")]
        public static Task<object> Open(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                ReifyCommandCenterWindow.Open();
                return new
                {
                    opened = true,
                    menu_paths = new[] { "Window/Reify/Command Center", "Tools/Reify/Command Center" },
                    note = "Opened the Reify Command Center EditorWindow in Unity.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)Time.frameCount
                };
            });
        }
    }
}
