using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// 10th Phase C philosophy tool (closes gap 10). Reports what would be
    /// lost if Unity crashed right now — dirty scenes and dirty loaded
    /// assets. any_dirty is the single-flag gate for "should I save before
    /// triggering a domain reload / restart?".
    /// </summary>
    internal static class PersistenceStatusTool
    {
        public static Task<object> Handle(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                // ---- scenes ----
                var dirtyScenes = new List<object>();
                var active = SceneManager.GetActiveScene();
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (!s.isDirty) continue;
                    dirtyScenes.Add(new
                    {
                        path       = s.path,
                        name       = s.name,
                        is_loaded  = s.isLoaded,
                        is_active  = s == active,
                        build_index = s.buildIndex
                    });
                }

                // ---- assets ----
                // Unity doesn't expose a cheap "list of dirty assets" API.
                // Best-effort: walk every loaded Unity object, filter to
                // those with an asset path and IsDirty=true. Bounded to
                // 50,000 iterations so a pathologically large project
                // can't stall us — document the cap as a known limitation.
                var dirtyAssets = new List<object>();
                var all = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
                var max = Math.Min(all.Length, 50_000);
                var scanned = 0;
                for (var i = 0; i < max; i++)
                {
                    var o = all[i];
                    if (o == null) continue;
                    scanned++;
                    // Skip scene objects (handled above) and internal types.
                    if (o is GameObject || o is Component) continue;

                    var path = AssetDatabase.GetAssetPath(o);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!EditorUtility.IsDirty(o)) continue;

                    // Deduplicate — multiple Object references can point at
                    // the same asset path (e.g. sub-assets of an FBX).
                    var dup = false;
                    foreach (dynamic d in dirtyAssets)
                        if ((string)d.path == path) { dup = true; break; }
                    if (!dup)
                        dirtyAssets.Add(new
                        {
                            path = path,
                            type = o.GetType().FullName,
                            name = o.name
                        });
                }

                // ---- warnings ----
                var w = new List<string>();
                if (dirtyScenes.Count > 0)
                    w.Add($"{dirtyScenes.Count} scene(s) have unsaved changes. Unity crash or force-quit would lose them.");
                if (dirtyAssets.Count > 0)
                    w.Add($"{dirtyAssets.Count} asset(s) have unsaved changes. AssetDatabase.SaveAssets would flush them.");
                if (active.isDirty)
                    w.Add($"Active scene '{active.name}' is dirty — save before any operation that triggers a domain reload.");
                if (all.Length >= 50_000)
                    w.Add($"Loaded-object scan was capped at 50,000 (found {all.Length}). Dirty-asset list may be incomplete.");

                return new
                {
                    any_dirty             = dirtyScenes.Count > 0 || dirtyAssets.Count > 0,
                    scenes                = new
                    {
                        dirty_count = dirtyScenes.Count,
                        dirty_list  = dirtyScenes.ToArray()
                    },
                    assets                = new
                    {
                        dirty_count   = dirtyAssets.Count,
                        dirty_list    = dirtyAssets.ToArray(),
                        scanned_count = scanned
                    },
                    warnings              = w.ToArray(),
                    note                  = "Project-settings-dirty tracking isn't exposed by Unity; use asset dirtiness on ProjectSettings.asset as a proxy.",
                    read_at_utc           = DateTime.UtcNow.ToString("o"),
                    frame                 = (long)Time.frameCount
                };
            });
        }
    }
}
