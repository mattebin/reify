using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Scene-level structured reads. scene-hierarchy paginates a
    /// depth-first flattened tree, scene-query filters it, scene-stats
    /// aggregates it with philosophy warnings.
    /// </summary>
    internal static class SceneHierarchyTools
    {
        private const int DefaultPageSize = 500;
        private const int MaxPageSize     = 5000;

        // ---------- scene-hierarchy ----------
        [ReifyTool("scene-hierarchy")]
        public static Task<object> Hierarchy(JToken args)
        {
            var scenePath = args?.Value<string>("scene_path");
            var cursor    = args?.Value<int?>("cursor")    ?? 0;
            var pageSize  = Math.Min(args?.Value<int?>("page_size") ?? DefaultPageSize, MaxPageSize);
            var includeComponents = args?.Value<bool?>("include_components") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scenes = ResolveScenes(scenePath);
                var all = new List<GameObject>();
                foreach (var s in scenes)
                    foreach (var root in s.GetRootGameObjects())
                        WalkDepthFirst(root.transform, all);

                var slice = new List<object>();
                var end   = Math.Min(cursor + pageSize, all.Count);
                for (var i = cursor; i < end; i++)
                    slice.Add(NodeDto(all[i], includeComponents));

                return new
                {
                    scene_paths = PathsOf(scenes),
                    total_nodes = all.Count,
                    cursor,
                    page_size   = pageSize,
                    returned    = slice.Count,
                    next_cursor = end < all.Count ? (int?)end : null,
                    nodes       = slice,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- scene-query ----------
        [ReifyTool("scene-query")]
        public static Task<object> Query(JToken args)
        {
            var scenePath       = args?.Value<string>("scene_path");
            var componentType   = args?.Value<string>("component_type");
            var namePattern     = args?.Value<string>("name_pattern");
            var tag             = args?.Value<string>("tag");
            var layer           = args?["layer"]?.Type == JTokenType.Integer
                ? args.Value<int?>("layer") : null;
            var active          = args?["active"]?.Type == JTokenType.Boolean
                ? args.Value<bool?>("active") : null;
            var limit           = Math.Min(args?.Value<int?>("limit") ?? 500, MaxPageSize);

            Regex rx = null;
            if (!string.IsNullOrEmpty(namePattern))
            {
                try { rx = new Regex(namePattern); }
                catch (Exception ex)
                {
                    throw new ArgumentException($"name_pattern is not a valid regex: {ex.Message}");
                }
            }

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scenes = ResolveScenes(scenePath);
                var matches = new List<object>();
                var scanned = 0;
                var truncated = false;

                foreach (var s in scenes)
                {
                    foreach (var root in s.GetRootGameObjects())
                    {
                        Scan(root.transform, go =>
                        {
                            scanned++;
                            if (rx != null && !rx.IsMatch(go.name)) return;
                            if (!string.IsNullOrEmpty(tag) && go.tag != tag) return;
                            if (layer.HasValue && go.layer != layer.Value) return;
                            if (active.HasValue && go.activeInHierarchy != active.Value) return;
                            if (!string.IsNullOrEmpty(componentType) && !HasComponent(go, componentType)) return;

                            if (matches.Count >= limit) { truncated = true; return; }
                            matches.Add(NodeDto(go, includeComponents: true));
                        });
                    }
                }

                return new
                {
                    scene_paths = PathsOf(scenes),
                    query = new { component_type = componentType, name_pattern = namePattern, tag, layer, active, limit },
                    scanned,
                    match_count = matches.Count,
                    truncated,
                    matches,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- scene-stats ----------
        [ReifyTool("scene-stats")]
        public static Task<object> Stats(JToken args)
        {
            var scenePath = args?.Value<string>("scene_path");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scenes = ResolveScenes(scenePath);
                var totalGO       = 0;
                var totalActive   = 0;
                var totalInactive = 0;
                var rootCount     = 0;
                var componentByType = new Dictionary<string, int>();
                var tagByName       = new Dictionary<string, int>();
                var mainCameras     = 0;
                var cameraCount     = 0;
                var lightCount      = 0;
                var directionalCount = 0;
                var rendererCount   = 0;

                foreach (var s in scenes)
                {
                    foreach (var root in s.GetRootGameObjects())
                    {
                        rootCount++;
                        Scan(root.transform, go =>
                        {
                            totalGO++;
                            if (go.activeInHierarchy) totalActive++;
                            else totalInactive++;

                            if (!tagByName.ContainsKey(go.tag)) tagByName[go.tag] = 0;
                            tagByName[go.tag]++;

                            if (go.CompareTag("MainCamera")) mainCameras++;

                            foreach (var c in go.GetComponents<Component>())
                            {
                                if (c == null) continue;
                                var fqn = c.GetType().FullName;
                                if (!componentByType.ContainsKey(fqn)) componentByType[fqn] = 0;
                                componentByType[fqn]++;

                                if (c is Camera) cameraCount++;
                                if (c is Light l) { lightCount++; if (l.type == LightType.Directional) directionalCount++; }
                                if (c is Renderer) rendererCount++;
                            }
                        });
                    }
                }

                var warnings = new List<string>();
                if (mainCameras > 1)
                    warnings.Add($"{mainCameras} GameObjects carry the 'MainCamera' tag — Unity uses the first one found; others are effectively dead.");
                if (mainCameras == 0 && cameraCount > 0)
                    warnings.Add("No GameObject tagged 'MainCamera' — Camera.main returns null; UI raycasts and default camera lookups will break.");
                if (directionalCount == 0 && lightCount == 0)
                    warnings.Add("No Light in scene — skybox-only lighting unless URP ambient is configured.");
                if (directionalCount > 1)
                    warnings.Add($"{directionalCount} Directional Lights — usually one is intended; shadow and ambient contribution stacks.");
                if (totalInactive > totalActive)
                    warnings.Add($"More inactive objects ({totalInactive}) than active ({totalActive}) — scene may be over-provisioned or mid-refactor.");

                return new
                {
                    scene_paths       = PathsOf(scenes),
                    gameobject_count  = totalGO,
                    active_count      = totalActive,
                    inactive_count    = totalInactive,
                    root_count        = rootCount,
                    camera_count      = cameraCount,
                    light_count       = lightCount,
                    directional_count = directionalCount,
                    renderer_count    = rendererCount,
                    main_camera_tag_count = mainCameras,
                    component_by_type = componentByType,
                    tag_by_name       = tagByName,
                    warnings          = warnings.ToArray(),
                    read_at_utc       = DateTime.UtcNow.ToString("o"),
                    frame             = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static IEnumerable<Scene> ResolveScenes(string scenePath)
        {
            if (!string.IsNullOrEmpty(scenePath))
            {
                var s = SceneManager.GetSceneByPath(scenePath);
                if (!s.IsValid() || !s.isLoaded)
                    throw new InvalidOperationException($"Scene not loaded: {scenePath}");
                yield return s;
                yield break;
            }
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded) yield return s;
            }
        }

        private static string[] PathsOf(IEnumerable<Scene> scenes)
        {
            var list = new List<string>();
            foreach (var s in scenes) list.Add(s.path);
            return list.ToArray();
        }

        private static void WalkDepthFirst(Transform t, List<GameObject> accum)
        {
            accum.Add(t.gameObject);
            for (var i = 0; i < t.childCount; i++)
                WalkDepthFirst(t.GetChild(i), accum);
        }

        private static void Scan(Transform t, Action<GameObject> visit)
        {
            visit(t.gameObject);
            for (var i = 0; i < t.childCount; i++)
                Scan(t.GetChild(i), visit);
        }

        private static bool HasComponent(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var t = c.GetType();
                if (t.FullName == typeName || t.Name == typeName) return true;
            }
            return false;
        }

        private static object NodeDto(GameObject go, bool includeComponents)
        {
            object components = null;
            if (includeComponents)
            {
                var comps = go.GetComponents<Component>();
                var names = new string[comps.Length];
                for (var i = 0; i < comps.Length; i++)
                    names[i] = comps[i] != null ? comps[i].GetType().FullName : "<missing-script>";
                components = names;
            }

            return new
            {
                instance_id = GameObjectResolver.InstanceIdOf(go),
                name        = go.name,
                path        = GameObjectResolver.PathOf(go),
                active_self = go.activeSelf,
                active_in_hierarchy = go.activeInHierarchy,
                layer       = go.layer,
                layer_name  = LayerMask.LayerToName(go.layer),
                tag         = go.tag,
                child_count = go.transform.childCount,
                component_types = components
            };
        }
    }
}
