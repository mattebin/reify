using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class PingTool
    {
        public static Task<object> Handle(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var projectPath = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
                var projectName = new DirectoryInfo(projectPath).Name;

                return new
                {
                    status        = "ok",
                    unity_version = Application.unityVersion,
                    project_name  = projectName,
                    project_path  = projectPath,
                    platform      = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    is_play_mode  = EditorApplication.isPlayingOrWillChangePlaymode,
                    is_compiling  = EditorApplication.isCompiling,
                    frame         = (long)Time.frameCount,
                    read_at_utc   = DateTime.UtcNow.ToString("o")
                };
            });
        }
    }
}
