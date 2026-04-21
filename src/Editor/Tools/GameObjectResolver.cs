using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Centralised resolution of scene GameObjects by path or instance id.
    /// Every gameobject-* and component-* tool uses the same rules so error
    /// messages and edge cases stay consistent.
    /// </summary>
    internal static class GameObjectResolver
    {
        /// <summary>
        /// Resolve a GameObject by scene path. Accepts:
        /// - "/Root/Child"
        /// - "Root/Child"
        /// - "BareName"
        /// - "SceneName::Root/Child"
        /// - "Assets/Scenes/MyScene.unity::Root/Child"
        ///
        /// Ambiguous matches throw instead of silently returning the first hit.
        /// </summary>
        public static GameObject ByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var matches = FindByPath(path);
            if (matches.Count == 0) return null;
            if (matches.Count > 1)
                throw new InvalidOperationException(BuildAmbiguousPathMessage(path, matches));

            return matches[0];
        }

        /// <summary>
        /// Resolve any Unity object by instance id. Throws if missing.
        /// </summary>
        public static Object ByInstanceId(int instanceId)
        {
            #pragma warning disable CS0618
            return EditorUtility.InstanceIDToObject(instanceId);
            #pragma warning restore CS0618
        }

        /// <summary>
        /// Returns a Unity Object's instance id, wrapping the Unity-6
        /// CS0618 on GetInstanceID() that we can't migrate off of while the
        /// package supports 2021.3 (GetEntityId doesn't exist there).
        /// </summary>
        public static int InstanceIdOf(Object o)
        {
            #pragma warning disable CS0618
            return o.GetInstanceID();
            #pragma warning restore CS0618
        }

        /// <summary>
        /// Compute the full scene path of a GameObject, e.g. "Parent/Child/Leaf".
        /// </summary>
        public static string PathOf(GameObject go)
        {
            if (go == null) return null;
            var parts = new List<string> { go.name };
            var t = go.transform.parent;
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        /// <summary>
        /// Compute a scene-qualified path, preferring scene.path and falling
        /// back to scene.name for unsaved scenes.
        /// </summary>
        public static string QualifiedPathOf(GameObject go)
        {
            if (go == null) return null;
            var sceneRef = SceneReferenceOf(go.scene);
            var path = PathOf(go);
            return string.IsNullOrEmpty(sceneRef) ? path : sceneRef + "::" + path;
        }

        public static string SceneReferenceOf(Scene scene)
        {
            if (!scene.IsValid()) return null;
            return !string.IsNullOrEmpty(scene.path) ? scene.path : scene.name;
        }

        private static List<GameObject> FindByPath(string path)
        {
            ParseLookup(path, out var sceneSelector, out var hierarchyPath, out var bareNameLookup);
            if (string.IsNullOrEmpty(hierarchyPath))
                return new List<GameObject>();

            var matches = new List<GameObject>();
            if (bareNameLookup)
            {
                for (var s = 0; s < SceneManager.sceneCount; s++)
                {
                    var scene = SceneManager.GetSceneAt(s);
                    if (!SceneMatches(scene, sceneSelector)) continue;
                    foreach (var root in scene.GetRootGameObjects())
                        WalkByName(root.transform, hierarchyPath, matches);
                }
                return matches;
            }

            var segments = hierarchyPath.Split('/');
            for (var s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!SceneMatches(scene, sceneSelector)) continue;
                CollectHierarchyMatches(scene, segments, matches);
            }
            return matches;
        }

        private static void ParseLookup(string path, out string sceneSelector, out string hierarchyPath, out bool bareNameLookup)
        {
            var trimmed = path.Trim();
            var split = trimmed.IndexOf("::", StringComparison.Ordinal);
            if (split >= 0)
            {
                sceneSelector = trimmed.Substring(0, split).Trim();
                trimmed = trimmed.Substring(split + 2).Trim();
            }
            else
            {
                sceneSelector = null;
            }

            hierarchyPath = trimmed.TrimStart('/');
            bareNameLookup = hierarchyPath.IndexOf('/') < 0;
        }

        private static bool SceneMatches(Scene scene, string sceneSelector)
        {
            if (!scene.isLoaded) return false;
            if (string.IsNullOrEmpty(sceneSelector)) return true;

            return string.Equals(scene.path, sceneSelector, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(scene.name, sceneSelector, StringComparison.OrdinalIgnoreCase);
        }

        private static void CollectHierarchyMatches(Scene scene, string[] segments, List<GameObject> matches)
        {
            var frontier = new List<Transform>();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == segments[0])
                    frontier.Add(root.transform);

            for (var i = 1; i < segments.Length && frontier.Count > 0; i++)
            {
                var next = new List<Transform>();
                foreach (var current in frontier)
                {
                    for (var c = 0; c < current.childCount; c++)
                    {
                        var child = current.GetChild(c);
                        if (child.name == segments[i])
                            next.Add(child);
                    }
                }
                frontier = next;
            }

            foreach (var hit in frontier)
                matches.Add(hit.gameObject);
        }

        private static void WalkByName(Transform t, string target, List<GameObject> hits)
        {
            if (t.gameObject.name == target) hits.Add(t.gameObject);
            for (var i = 0; i < t.childCount; i++)
                WalkByName(t.GetChild(i), target, hits);
        }

        private static string BuildAmbiguousPathMessage(string path, List<GameObject> matches)
        {
            const int maxCandidates = 8;
            var shown = Math.Min(matches.Count, maxCandidates);
            var candidates = new string[shown];
            for (var i = 0; i < shown; i++)
            {
                var go = matches[i];
                candidates[i] = $"{QualifiedPathOf(go)} (instance_id {InstanceIdOf(go)})";
            }

            var more = matches.Count > shown ? $" and {matches.Count - shown} more" : "";
            return
                $"GameObject path '{path}' is ambiguous. Matches: {string.Join("; ", candidates)}{more}. " +
                "Use a scene-qualified path '<scene>::<path>' or an instance_id instead.";
        }
    }
}
