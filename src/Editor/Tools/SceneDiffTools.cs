using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Scene snapshot + diff. Captures every GameObject in the active (or
    /// every loaded) scene as a stable, hashable inventory, then computes
    /// a structural diff: added / removed / moved / component-changed.
    ///
    /// Nothing in CoplayDev or IvanMurzak exposes scene diff — this is a
    /// trust-layer differentiator that lets write operations be proven
    /// against a pre-op snapshot.
    /// </summary>
    internal static class SceneDiffTools
    {
        // ---------- scene-snapshot ----------
        [ReifyTool("scene-snapshot")]
        public static Task<object> Snapshot(JToken args)
        {
            var allLoaded = args?.Value<bool?>("all_loaded_scenes") ?? false;
            var includeComponents = args?.Value<bool?>("include_components") ?? true;
            var includeTransform  = args?.Value<bool?>("include_transform") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scenes = new List<Scene>();
                if (allLoaded)
                {
                    for (var i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var s = SceneManager.GetSceneAt(i);
                        if (s.IsValid() && s.isLoaded) scenes.Add(s);
                    }
                }
                else
                {
                    scenes.Add(SceneManager.GetActiveScene());
                }

                var gos = new List<object>();
                foreach (var scene in scenes)
                {
                    var roots = scene.GetRootGameObjects();
                    foreach (var root in roots)
                    {
                        var xforms = root.GetComponentsInChildren<Transform>(true);
                        foreach (var x in xforms)
                        {
                            gos.Add(SnapshotGameObject(x.gameObject, scene, includeComponents, includeTransform));
                        }
                    }
                }

                return new
                {
                    scene_names          = scenes.Select(s => s.name).ToArray(),
                    scene_paths          = scenes.Select(s => s.path).ToArray(),
                    all_loaded_scenes    = allLoaded,
                    include_components   = includeComponents,
                    include_transform    = includeTransform,
                    gameobject_count     = gos.Count,
                    gameobjects          = gos.ToArray(),
                    read_at_utc          = DateTime.UtcNow.ToString("o"),
                    frame                = (long)Time.frameCount
                };
            });
        }

        // ---------- scene-diff ----------
        [ReifyTool("scene-diff")]
        public static Task<object> Diff(JToken args)
        {
            var before = args?["before_snapshot"]
                ?? throw new ArgumentException("before_snapshot is required (pass a prior scene-snapshot result).");

            var allLoaded = args?.Value<bool?>("all_loaded_scenes") ?? false;
            var includeComponents = args?.Value<bool?>("include_components") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                // Build an index of the `before` snapshot by scene path.
                var beforeGos = before["gameobjects"] as JArray
                    ?? throw new ArgumentException("before_snapshot.gameobjects is missing or not an array.");

                var beforeByPath = new Dictionary<string, JObject>();
                foreach (JObject g in beforeGos.OfType<JObject>())
                {
                    var p = g.Value<string>("scene_path");
                    if (!string.IsNullOrEmpty(p)) beforeByPath[p] = g;
                }

                // Build the current snapshot inline.
                var currentScenes = new List<Scene>();
                if (allLoaded)
                {
                    for (var i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var s = SceneManager.GetSceneAt(i);
                        if (s.IsValid() && s.isLoaded) currentScenes.Add(s);
                    }
                }
                else
                {
                    currentScenes.Add(SceneManager.GetActiveScene());
                }

                var currentByPath = new Dictionary<string, object>();
                var currentRaw = new Dictionary<string, (GameObject go, Scene scene)>();
                foreach (var scene in currentScenes)
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var x in root.GetComponentsInChildren<Transform>(true))
                        {
                            var path = $"{scene.name}::{GameObjectResolver.PathOf(x.gameObject)}";
                            currentByPath[path] = SnapshotGameObject(x.gameObject, scene, includeComponents, includeTransform: true);
                            currentRaw[path] = (x.gameObject, scene);
                        }
                    }
                }

                // Added = in current, not in before.
                // Removed = in before, not in current.
                // Changed = in both, but component set or transform hash differs.
                var added = new List<object>();
                var removed = new List<object>();
                var changed = new List<object>();

                foreach (var kvp in currentByPath)
                {
                    if (!beforeByPath.ContainsKey(kvp.Key))
                    {
                        added.Add(kvp.Value);
                        continue;
                    }
                    var beforeGo = beforeByPath[kvp.Key];
                    var diffBlock = DiffOne(beforeGo, kvp.Value);
                    if (diffBlock != null) changed.Add(diffBlock);
                }

                foreach (var kvp in beforeByPath)
                {
                    if (!currentByPath.ContainsKey(kvp.Key))
                    {
                        removed.Add(new
                        {
                            scene_path      = kvp.Key,
                            instance_id     = kvp.Value.Value<int?>("instance_id"),
                            before_snapshot = kvp.Value
                        });
                    }
                }

                return new
                {
                    added_count    = added.Count,
                    removed_count  = removed.Count,
                    changed_count  = changed.Count,
                    added          = added.ToArray(),
                    removed        = removed.ToArray(),
                    changed        = changed.ToArray(),
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static object SnapshotGameObject(GameObject go, Scene scene,
            bool includeComponents, bool includeTransform)
        {
            var path = $"{scene.name}::{GameObjectResolver.PathOf(go)}";

            string[] componentTypes = null;
            if (includeComponents)
            {
                var comps = go.GetComponents<Component>();
                var names = new List<string>(comps.Length);
                foreach (var c in comps) names.Add(c != null ? c.GetType().FullName : "<missing>");
                componentTypes = names.ToArray();
            }

            object transformBlock = null;
            if (includeTransform)
            {
                var t = go.transform;
                var pos = t.position;
                var rot = t.rotation.eulerAngles;
                var scl = t.lossyScale;
                transformBlock = new
                {
                    position_world       = new { x = pos.x, y = pos.y, z = pos.z },
                    rotation_euler_world = new { x = rot.x, y = rot.y, z = rot.z },
                    scale_lossy          = new { x = scl.x, y = scl.y, z = scl.z }
                };
            }

            return new
            {
                instance_id      = GameObjectResolver.InstanceIdOf(go),
                scene_path       = path,
                name             = go.name,
                active_self      = go.activeSelf,
                active_in_hierarchy = go.activeInHierarchy,
                layer            = go.layer,
                tag              = go.tag,
                static_flags     = (int)GameObjectUtility.GetStaticEditorFlags(go),
                component_count  = go.GetComponents<Component>().Length,
                component_types  = componentTypes,
                transform        = transformBlock
            };
        }

        private static object DiffOne(JObject before, object currentAny)
        {
            var current = JObject.FromObject(currentAny);

            var diffs = new List<object>();

            // Shallow scalar fields.
            void CompareScalar(string field)
            {
                var b = before[field];
                var c = current[field];
                if (!JToken.DeepEquals(b, c))
                {
                    diffs.Add(new { field, before = b, after = c });
                }
            }

            CompareScalar("name");
            CompareScalar("active_self");
            CompareScalar("layer");
            CompareScalar("tag");
            CompareScalar("static_flags");
            CompareScalar("component_count");

            // Component-set diff.
            var bComps = (before["component_types"] as JArray)?.Select(x => x.ToString()).ToList() ?? new List<string>();
            var cComps = (current["component_types"] as JArray)?.Select(x => x.ToString()).ToList() ?? new List<string>();
            var compsAdded   = cComps.Except(bComps).ToArray();
            var compsRemoved = bComps.Except(cComps).ToArray();
            if (compsAdded.Length > 0 || compsRemoved.Length > 0)
            {
                diffs.Add(new
                {
                    field            = "component_types",
                    components_added = compsAdded,
                    components_removed = compsRemoved
                });
            }

            // Transform: compare by shallow JSON equality.
            if (before["transform"] != null && current["transform"] != null &&
                !JToken.DeepEquals(before["transform"], current["transform"]))
            {
                diffs.Add(new
                {
                    field  = "transform",
                    before = before["transform"],
                    after  = current["transform"]
                });
            }

            if (diffs.Count == 0) return null;
            return new
            {
                scene_path  = current.Value<string>("scene_path"),
                instance_id = current.Value<int?>("instance_id"),
                diffs       = diffs.ToArray()
            };
        }
    }
}
