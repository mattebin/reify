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
        /// Resolve a GameObject by scene path. Accepts "/Root/Child",
        /// "Root/Child", or a bare name. Walks across every loaded scene.
        /// </summary>
        public static GameObject ByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var direct = GameObject.Find(path);
            if (direct != null) return direct;

            var slashed  = path.TrimStart('/');
            var segments = slashed.Split('/');

            for (var s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != segments[0]) continue;
                    var current = root.transform;
                    var ok = true;
                    for (var i = 1; i < segments.Length && ok; i++)
                    {
                        var child = current.Find(segments[i]);
                        if (child == null) { ok = false; break; }
                        current = child;
                    }
                    if (ok) return current.gameObject;
                }
            }
            return null;
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
    }
}
