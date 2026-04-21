using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    internal static class GameObjectFindTool
    {
        [ReifyTool("gameobject-find")]
        public static Task<object> Handle(JToken args)
        {
            var name       = args?.Value<string>("name");
            var tag        = args?.Value<string>("tag");
            var path       = args?.Value<string>("path");
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var hits = new List<GameObject>();

                if (instanceId.HasValue)
                {
                    if (GameObjectResolver.ByInstanceId(instanceId.Value) is GameObject g)
                        hits.Add(g);
                }
                else if (!string.IsNullOrEmpty(path))
                {
                    var g = GameObjectResolver.ByPath(path);
                    if (g != null) hits.Add(g);
                }
                else if (!string.IsNullOrEmpty(tag))
                {
                    try { hits.AddRange(GameObject.FindGameObjectsWithTag(tag)); }
                    catch (UnityException ex)
                    {
                        throw new InvalidOperationException($"Unknown tag '{tag}': {ex.Message}");
                    }
                }
                else if (!string.IsNullOrEmpty(name))
                {
                    // Walk every loaded scene manually — GameObject.Find only
                    // returns active objects, and we want inactive too.
                    for (var s = 0; s < SceneManager.sceneCount; s++)
                    {
                        var scene = SceneManager.GetSceneAt(s);
                        if (!scene.isLoaded) continue;
                        foreach (var root in scene.GetRootGameObjects())
                            WalkByName(root.transform, name, hits);
                    }
                }
                else
                {
                    throw new ArgumentException(
                        "Provide at least one of: name, tag, path, instance_id.");
                }

                var dtos = new object[hits.Count];
                for (var i = 0; i < hits.Count; i++)
                    dtos[i] = GameObjectDto.Build(hits[i], includeComponents: false);

                return new
                {
                    match_count = hits.Count,
                    matches     = dtos,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        private static void WalkByName(Transform t, string target, List<GameObject> hits)
        {
            if (t.gameObject.name == target) hits.Add(t.gameObject);
            for (var i = 0; i < t.childCount; i++)
                WalkByName(t.GetChild(i), target, hits);
        }
    }
}
