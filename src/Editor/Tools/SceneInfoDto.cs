using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Shared projection of a <see cref="Scene"/> into reify's structured-state
    /// JSON contract. Wraps the scene payload with the mandatory
    /// <c>read_at_utc</c> + <c>frame</c> metadata per ADR-001 §3.
    /// </summary>
    internal static class SceneInfoDto
    {
        public static object Build(Scene scene, bool includeRoots)
        {
            var active = SceneManager.GetActiveScene();
            string[] rootNames;

            if (includeRoots && scene.IsValid() && scene.isLoaded)
            {
                var roots = scene.GetRootGameObjects();
                rootNames = new string[roots.Length];
                for (var i = 0; i < roots.Length; i++) rootNames[i] = roots[i].name;
            }
            else
            {
                rootNames = Array.Empty<string>();
            }

            return new
            {
                scene = new
                {
                    name              = scene.name,
                    path              = scene.path,
                    build_index       = scene.buildIndex,
                    is_loaded         = scene.isLoaded,
                    is_dirty          = scene.isDirty,
                    is_active         = scene == active,
                    root_count        = rootNames.Length,
                    root_gameobjects  = rootNames
                },
                read_at_utc = DateTime.UtcNow.ToString("o"),
                frame       = (long)Time.frameCount
            };
        }
    }
}
