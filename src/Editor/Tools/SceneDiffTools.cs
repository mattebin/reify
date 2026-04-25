using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Scene snapshot + diff. Captures every GameObject in the active (or
    /// every loaded) scene as a stable inventory, then computes structural
    /// added / removed / changed evidence.
    /// </summary>
    internal static class SceneDiffTools
    {
        private const int AutoComponentIdThreshold = 250;

        [ReifyTool("scene-snapshot")]
        public static Task<object> Snapshot(JToken args)
        {
            var allLoaded = args?.Value<bool?>("all_loaded_scenes") ?? false;
            var includeComponents = args?.Value<bool?>("include_components") ?? true;
            var includeTransform = args?.Value<bool?>("include_transform") ?? true;
            var requestedEncoding = (args?.Value<string>("component_encoding") ?? "auto").ToLowerInvariant();

            if (requestedEncoding != "auto" && requestedEncoding != "names" && requestedEncoding != "ids")
                throw new ArgumentException("component_encoding must be one of: auto, names, ids.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scenes = ResolveScenes(allLoaded);
                var targets = CollectTargets(scenes);
                var useIds = includeComponents &&
                    (requestedEncoding == "ids" ||
                     (requestedEncoding == "auto" && targets.Count >= AutoComponentIdThreshold));
                var componentTable = useIds ? new ComponentTypeTable() : null;

                var gos = new List<object>(targets.Count);
                foreach (var target in targets)
                {
                    gos.Add(SnapshotGameObject(
                        target.go,
                        target.scene,
                        includeComponents,
                        includeTransform,
                        componentTable));
                }

                return new
                {
                    scene_names = scenes.Select(s => s.name).ToArray(),
                    scene_paths = scenes.Select(s => s.path).ToArray(),
                    all_loaded_scenes = allLoaded,
                    include_components = includeComponents,
                    include_transform = includeTransform,
                    component_encoding = includeComponents ? (useIds ? "ids" : "names") : "none",
                    component_type_table = useIds ? componentTable.Names : null,
                    component_type_table_count = useIds ? componentTable.Count : 0,
                    gameobject_count = gos.Count,
                    gameobjects = gos.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("scene-diff")]
        public static Task<object> Diff(JToken args)
        {
            var before = args?["before_snapshot"]
                ?? throw new ArgumentException("before_snapshot is required (pass a prior scene-snapshot result).");

            var allLoaded = args?.Value<bool?>("all_loaded_scenes") ?? false;
            var includeComponents = args?.Value<bool?>("include_components") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var beforeGos = before["gameobjects"] as JArray
                    ?? throw new ArgumentException("before_snapshot.gameobjects is missing or not an array.");
                var beforeComponentTable = before["component_type_table"] as JArray;

                var beforeByPath = new Dictionary<string, JObject>();
                foreach (var g in beforeGos.OfType<JObject>())
                {
                    var p = g.Value<string>("scene_path");
                    if (!string.IsNullOrEmpty(p)) beforeByPath[p] = g;
                }

                var currentScenes = ResolveScenes(allLoaded);
                var currentByPath = new Dictionary<string, object>();
                foreach (var target in CollectTargets(currentScenes))
                {
                    var path = $"{target.scene.name}::{GameObjectResolver.PathOf(target.go)}";
                    currentByPath[path] = SnapshotGameObject(
                        target.go,
                        target.scene,
                        includeComponents,
                        includeTransform: true,
                        componentTable: null);
                }

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

                    var diffBlock = DiffOne(beforeByPath[kvp.Key], kvp.Value, beforeComponentTable);
                    if (diffBlock != null) changed.Add(diffBlock);
                }

                foreach (var kvp in beforeByPath)
                {
                    if (!currentByPath.ContainsKey(kvp.Key))
                    {
                        removed.Add(new
                        {
                            scene_path = kvp.Key,
                            instance_id = kvp.Value.Value<int?>("instance_id"),
                            before_snapshot = kvp.Value
                        });
                    }
                }

                return new
                {
                    added_count = added.Count,
                    removed_count = removed.Count,
                    changed_count = changed.Count,
                    added = added.ToArray(),
                    removed = removed.ToArray(),
                    changed = changed.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)Time.frameCount
                };
            });
        }

        private static List<Scene> ResolveScenes(bool allLoaded)
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
                var active = SceneManager.GetActiveScene();
                if (active.IsValid() && active.isLoaded) scenes.Add(active);
            }

            return scenes;
        }

        private static List<(GameObject go, Scene scene)> CollectTargets(IReadOnlyList<Scene> scenes)
        {
            var targets = new List<(GameObject go, Scene scene)>();
            foreach (var scene in scenes)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var x in root.GetComponentsInChildren<Transform>(true))
                        targets.Add((x.gameObject, scene));
                }
            }

            return targets;
        }

        private static object SnapshotGameObject(
            GameObject go,
            Scene scene,
            bool includeComponents,
            bool includeTransform,
            ComponentTypeTable componentTable)
        {
            var path = $"{scene.name}::{GameObjectResolver.PathOf(go)}";

            string[] componentTypes = null;
            int[] componentTypeIds = null;
            var components = go.GetComponents<Component>();
            if (includeComponents)
            {
                if (componentTable != null)
                {
                    componentTypeIds = new int[components.Length];
                    for (var i = 0; i < components.Length; i++)
                    {
                        var name = components[i] != null ? components[i].GetType().FullName : "<missing>";
                        componentTypeIds[i] = componentTable.IdFor(name);
                    }
                }
                else
                {
                    componentTypes = new string[components.Length];
                    for (var i = 0; i < components.Length; i++)
                        componentTypes[i] = components[i] != null ? components[i].GetType().FullName : "<missing>";
                }
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
                    position_world = new { x = pos.x, y = pos.y, z = pos.z },
                    rotation_euler_world = new { x = rot.x, y = rot.y, z = rot.z },
                    scale_lossy = new { x = scl.x, y = scl.y, z = scl.z }
                };
            }

            return new
            {
                instance_id = GameObjectResolver.InstanceIdOf(go),
                scene_path = path,
                name = go.name,
                active_self = go.activeSelf,
                active_in_hierarchy = go.activeInHierarchy,
                layer = go.layer,
                tag = go.tag,
                static_flags = (int)GameObjectUtility.GetStaticEditorFlags(go),
                component_count = components.Length,
                component_types = componentTypes,
                component_type_ids = componentTypeIds,
                transform = transformBlock
            };
        }

        private static object DiffOne(JObject before, object currentAny, JArray beforeComponentTable)
        {
            var current = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(currentAny));
            var diffs = new List<object>();

            void CompareScalar(string field)
            {
                var b = before[field];
                var c = current[field];
                if (!JToken.DeepEquals(b, c))
                    diffs.Add(new { field, before = b, after = c });
            }

            CompareScalar("name");
            CompareScalar("active_self");
            CompareScalar("layer");
            CompareScalar("tag");
            CompareScalar("static_flags");
            CompareScalar("component_count");

            var bComps = ComponentNamesFrom(before, beforeComponentTable);
            var cComps = ComponentNamesFrom(current, null);
            var compsAdded = cComps.Except(bComps).ToArray();
            var compsRemoved = bComps.Except(cComps).ToArray();
            if (compsAdded.Length > 0 || compsRemoved.Length > 0)
            {
                diffs.Add(new
                {
                    field = "component_types",
                    components_added = compsAdded,
                    components_removed = compsRemoved
                });
            }

            if (HasNonNullValue(before["transform"]) && HasNonNullValue(current["transform"]) &&
                !JToken.DeepEquals(before["transform"], current["transform"]))
            {
                diffs.Add(new
                {
                    field = "transform",
                    before = before["transform"],
                    after = current["transform"]
                });
            }

            if (diffs.Count == 0) return null;
            return new
            {
                scene_path = current.Value<string>("scene_path"),
                instance_id = current.Value<int?>("instance_id"),
                diffs = diffs.ToArray()
            };
        }

        private static bool HasNonNullValue(JToken token)
        {
            return token != null && token.Type != JTokenType.Null;
        }

        private static List<string> ComponentNamesFrom(JObject item, JArray table)
        {
            var names = item["component_types"] as JArray;
            if (names != null)
                return names.Select(x => x.ToString()).ToList();

            var ids = item["component_type_ids"] as JArray;
            if (ids == null || table == null)
                return new List<string>();

            var result = new List<string>(ids.Count);
            foreach (var idToken in ids)
            {
                var id = idToken.Value<int>();
                result.Add(id >= 0 && id < table.Count ? table[id].ToString() : $"<unknown:{id}>");
            }

            return result;
        }

        private sealed class ComponentTypeTable
        {
            private readonly Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<string> names = new List<string>();

            public int Count => names.Count;
            public string[] Names => names.ToArray();

            public int IdFor(string name)
            {
                if (ids.TryGetValue(name, out var id)) return id;
                id = names.Count;
                ids[name] = id;
                names.Add(name);
                return id;
            }
        }
    }
}
