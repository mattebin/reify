using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Reads Unity's on-disk Editor.log directly. The point isn't to
    /// duplicate console-log-read — it's to provide an evidence channel
    /// during the dead zone when the bridge can't reach Unity (initial
    /// load, compile failure that prevents reify assembly loading, mid
    /// domain-reload). When this tool succeeds and console-log-read
    /// doesn't, the agent knows the bridge is the failure point and can
    /// keep diagnosing.
    ///
    /// Path resolution per-OS matches Unity docs:
    ///   Win:   %LOCALAPPDATA%\Unity\Editor\Editor.log
    ///   macOS: ~/Library/Logs/Unity/Editor.log
    ///   Linux: ~/.config/unity3d/Editor.log
    /// </summary>
    internal static class EditorLogTailTool
    {
        // ---------- editor-log-tail ----------
        [ReifyTool("editor-log-tail")]
        public static Task<object> Tail(JToken args)
        {
            var maxLines = Math.Clamp(args?.Value<int?>("max_lines") ?? 200, 1, 5000);
            var maxBytes = Math.Clamp(args?.Value<int?>("max_bytes") ?? 256 * 1024, 1024, 4 * 1024 * 1024);
            var contains = args?.Value<string>("contains_substring");
            var explicitPath = args?.Value<string>("path");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var path = explicitPath;
                if (string.IsNullOrEmpty(path)) path = ResolveDefaultPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return new
                    {
                        path,
                        exists = false,
                        note = "Editor.log not found at the resolved path. Try passing path= explicitly.",
                        candidates = CandidatePaths()
                    };

                long fileSize;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    fileSize = fs.Length;

                long offset = Math.Max(0, fileSize - maxBytes);
                string text;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    text = sr.ReadToEnd();
                }

                var lines = text.Split('\n');
                var filtered = new List<string>(lines.Length);
                foreach (var raw in lines)
                {
                    var line = raw.TrimEnd('\r');
                    if (string.IsNullOrEmpty(line)) continue;
                    if (!string.IsNullOrEmpty(contains)
                        && line.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    filtered.Add(line);
                }
                int start = Math.Max(0, filtered.Count - maxLines);
                var slice = filtered.GetRange(start, filtered.Count - start);

                return new
                {
                    path,
                    exists                = true,
                    file_size_bytes       = fileSize,
                    bytes_read            = fileSize - offset,
                    truncated_to_max_bytes = offset > 0,
                    lines_after_filter    = filtered.Count,
                    returned_lines        = slice.Count,
                    contains_substring    = contains,
                    lines                 = slice.ToArray(),
                    read_at_utc           = DateTime.UtcNow.ToString("o")
                };
            });
        }

        private static string ResolveDefaultPath()
        {
#if UNITY_EDITOR_WIN
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Unity", "Editor", "Editor.log");
#elif UNITY_EDITOR_OSX
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Logs", "Unity", "Editor.log");
#else
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "unity3d", "Editor.log");
#endif
        }

        private static string[] CandidatePaths()
        {
            var list = new List<string>();
            try { list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log")); } catch { }
            try { list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "Unity", "Editor.log")); } catch { }
            try { list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "unity3d", "Editor.log")); } catch { }
            return list.ToArray();
        }
    }
}
