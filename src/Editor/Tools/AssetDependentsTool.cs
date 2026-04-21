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
    /// 7th Phase C philosophy tool. Reverse-dependency lookup — "what breaks
    /// if I delete this?" Scans the AssetDatabase for every asset (including
    /// .unity scenes) whose forward-dependency set contains the target.
    /// First call is O(total-assets); cached for subsequent calls until
    /// AssetDatabase mutates (cache invalidated on every AssetDatabase
    /// change via AssetPostprocessor callback).
    /// </summary>
    internal static class AssetDependentsTool
    {
        // Cache: target_path → direct_dependents[]. Rebuilt on AssetDatabase
        // changes via AssetMutationListener below. Memory cost: ~100KB per
        // 10k-asset project.
        private static Dictionary<string, List<string>> _reverseIndex;
        private static readonly object _cacheLock = new object();

        [ReifyTool("asset-dependents")]
        public static Task<object> Handle(JToken args)
        {
            var assetPath        = args?.Value<string>("asset_path") ?? throw new ArgumentException("asset_path is required.");
            var includeScenes    = args?.Value<bool?>("include_scene_references") ?? true;
            var maxDepth         = Math.Clamp(args?.Value<int?>("max_depth") ?? 1, 1, 4);

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
                    throw new InvalidOperationException($"Asset not found: {assetPath}");

                var idx = GetIndex();

                // Direct dependents.
                idx.TryGetValue(assetPath, out var direct);
                direct ??= new List<string>();

                // Partition into scenes vs non-scenes.
                var sceneRefs     = new List<object>();
                var nonSceneDeps  = new List<object>();
                foreach (var p in direct)
                {
                    if (p.EndsWith(".unity", StringComparison.Ordinal))
                    {
                        if (includeScenes) sceneRefs.Add(new
                        {
                            scene_path = p,
                            guid       = AssetDatabase.AssetPathToGUID(p)
                        });
                    }
                    else
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
                        nonSceneDeps.Add(new
                        {
                            path  = p,
                            guid  = AssetDatabase.AssetPathToGUID(p),
                            type  = obj != null ? obj.GetType().FullName : null,
                            name  = obj != null ? obj.name : Path.GetFileNameWithoutExtension(p)
                        });
                    }
                }

                // Transitive (depth 2+).
                var transitive = new List<object>();
                if (maxDepth > 1)
                {
                    var seen = new HashSet<string> { assetPath };
                    foreach (var p in direct) seen.Add(p);
                    var frontier = new List<string>(direct);
                    for (var d = 2; d <= maxDepth && frontier.Count > 0; d++)
                    {
                        var next = new List<string>();
                        foreach (var f in frontier)
                        {
                            if (!idx.TryGetValue(f, out var deps)) continue;
                            foreach (var dep in deps)
                            {
                                if (!seen.Add(dep)) continue;
                                next.Add(dep);
                                transitive.Add(new
                                {
                                    path        = dep,
                                    depth       = d,
                                    via         = f,
                                    guid        = AssetDatabase.AssetPathToGUID(dep),
                                    is_scene    = dep.EndsWith(".unity", StringComparison.Ordinal)
                                });
                            }
                        }
                        frontier = next;
                    }
                }

                var warnings = new List<string>();
                if (sceneRefs.Count > 0)
                    warnings.Add($"Asset is referenced in {sceneRefs.Count} scene(s). Deleting will produce missing-reference warnings on scene load.");
                var prefabCount = 0;
                foreach (var d in nonSceneDeps)
                {
                    dynamic dd = d;
                    var p = (string)dd.path;
                    if (p.EndsWith(".prefab", StringComparison.Ordinal)) prefabCount++;
                }
                if (prefabCount > 0)
                    warnings.Add($"{prefabCount} prefab(s) reference this asset. Deleting breaks them in every scene instance.");
                if (direct.Count == 0)
                    warnings.Add("No dependents found — safe to delete, subject to runtime Resources.Load-style references which aren't tracked by AssetDatabase.");

                var obj0 = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                return new
                {
                    asset = new
                    {
                        path = assetPath,
                        guid = AssetDatabase.AssetPathToGUID(assetPath),
                        type = obj0 != null ? obj0.GetType().FullName : null,
                        name = obj0 != null ? obj0.name : Path.GetFileNameWithoutExtension(assetPath)
                    },
                    direct_dependent_count = direct.Count,
                    dependents             = nonSceneDeps.ToArray(),
                    scene_references       = sceneRefs.ToArray(),
                    transitive_dependents  = transitive.ToArray(),
                    max_depth              = maxDepth,
                    include_scene_references = includeScenes,
                    warnings               = warnings.ToArray(),
                    cache_size             = idx.Count,
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        internal static void InvalidateCache()
        {
            lock (_cacheLock) { _reverseIndex = null; }
        }

        private static Dictionary<string, List<string>> GetIndex()
        {
            lock (_cacheLock)
            {
                if (_reverseIndex != null) return _reverseIndex;

                var all = AssetDatabase.GetAllAssetPaths();
                var idx = new Dictionary<string, List<string>>(all.Length);

                foreach (var path in all)
                {
                    if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                    // recursive=false keeps this O(total asset dependencies).
                    // We walk transitive depth ourselves in Handle.
                    var deps = AssetDatabase.GetDependencies(path, recursive: false);
                    foreach (var d in deps)
                    {
                        if (d == path) continue;  // self
                        if (!idx.TryGetValue(d, out var list))
                        {
                            list = new List<string>();
                            idx[d] = list;
                        }
                        list.Add(path);
                    }
                }
                _reverseIndex = idx;
                return idx;
            }
        }

        /// <summary>
        /// Invalidates the reverse-index cache whenever the AssetDatabase
        /// changes. Registered once via InitializeOnLoad below.
        /// </summary>
        internal sealed class AssetMutationListener : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                AssetDependentsTool.InvalidateCache();
            }
        }
    }
}
