using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Multiplayer Play Mode (MPPM) / Virtual Players surface.
    /// Package-gated on `com.unity.multiplayer.playmode`.
    ///
    /// MPPM clones the project into sibling folders named 'VP_<n>' under
    /// `<project>/Library/VP/` (or similar) and runs them as separate
    /// Unity Editor instances on the same machine. The public API lives
    /// in `Unity.Multiplayer.PlayMode.Editor.*`. We inspect state
    /// reflectively so this compiles without the package installed.
    /// </summary>
    internal static class MppmTools
    {
        private static Type FindType(params string[] candidates)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var name in candidates)
                {
                    Type t = null;
                    try { t = asm.GetType(name, false); } catch { }
                    if (t != null) return t;
                }
            return null;
        }

        // ---------- mppm-status ----------
        [ReifyTool("mppm-status")]
        public static Task<object> Status(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var apiType = FindType(
                    "Unity.Multiplayer.PlayMode.VirtualProjects.Editor.VirtualProjectsEditor",
                    "Unity.Multiplayer.PlayMode.Editor.VirtualProjectsEditor",
                    "Unity.Multiplayer.Playmode.VirtualPlayer.Editor.VirtualPlayerEditor");

                var installed = apiType != null;
                var note = installed
                    ? $"MPPM detected via {apiType.FullName}."
                    : "MPPM not installed. Add `com.unity.multiplayer.playmode` to see clones.";

                // Filesystem fallback — check for VP_ folders under Library.
                var clones = new List<object>();
                var libDir = Path.GetFullPath("Library");
                if (Directory.Exists(libDir))
                {
                    foreach (var sub in Directory.GetDirectories(libDir))
                    {
                        var name = Path.GetFileName(sub);
                        if (name.StartsWith("VP_", StringComparison.Ordinal))
                        {
                            clones.Add(new
                            {
                                tag             = name,
                                path            = sub,
                                last_write_utc  = new DirectoryInfo(sub).LastWriteTimeUtc.ToString("o")
                            });
                        }
                    }
                }

                return new
                {
                    package_installed = installed,
                    api_type_fqn      = apiType?.FullName,
                    note              = note,
                    clone_directory_count = clones.Count,
                    clone_directories     = clones.ToArray(),
                    read_at_utc       = DateTime.UtcNow.ToString("o"),
                    frame             = (long)Time.frameCount
                };
            });
        }

        // ---------- mppm-clone-list ----------
        [ReifyTool("mppm-clone-list")]
        public static Task<object> CloneList(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var apiType = FindType(
                    "Unity.Multiplayer.PlayMode.VirtualProjects.Editor.VirtualProjectsEditor",
                    "Unity.Multiplayer.PlayMode.Editor.VirtualProjectsEditor");
                if (apiType == null)
                    throw new InvalidOperationException(
                        "MPPM API not found — install `com.unity.multiplayer.playmode` to list clones.");

                // Try common static members that return IEnumerable<virtual player>.
                IEnumerable list = null;
                foreach (var name in new[] { "GetClones", "Clones", "GetPlayers", "Players" })
                {
                    var p = apiType.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                    if (p != null) { list = p.GetValue(null) as IEnumerable; if (list != null) break; }
                    var m = apiType.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (m != null) { list = m.Invoke(null, null) as IEnumerable; if (list != null) break; }
                }

                var arr = new List<object>();
                if (list != null)
                {
                    foreach (var c in list)
                    {
                        if (c == null) continue;
                        var ct = c.GetType();
                        arr.Add(new
                        {
                            type_fqn  = ct.FullName,
                            name      = ct.GetProperty("Name")?.GetValue(c) as string,
                            tag       = ct.GetProperty("Tag")?.GetValue(c) as string
                                      ?? ct.GetProperty("Identifier")?.GetValue(c) as string,
                            is_running = (bool?)(ct.GetProperty("IsRunning")?.GetValue(c)
                                               ?? ct.GetProperty("IsActive")?.GetValue(c)),
                            project_path = ct.GetProperty("ProjectPath")?.GetValue(c) as string
                        });
                    }
                }

                return new
                {
                    clone_count = arr.Count,
                    clones      = arr.ToArray(),
                    api_type_fqn = apiType.FullName,
                    note = "MPPM's reflection surface varies across package versions. If `clones` " +
                           "is empty but Unity shows virtual players, the detected API name is " +
                           "listed in api_type_fqn — file an issue with the Unity version + " +
                           "package version so we can add the probe.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }
    }
}
