using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    internal static class SceneListTool
    {
        public static Task<object> Handle(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var active = SceneManager.GetActiveScene();
                var scenes = new List<object>(SceneManager.sceneCount);

                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    var roots = s.IsValid() && s.isLoaded ? s.GetRootGameObjects() : Array.Empty<GameObject>();
                    var rootNames = new string[roots.Length];
                    for (var j = 0; j < roots.Length; j++) rootNames[j] = roots[j].name;

                    scenes.Add(new
                    {
                        name              = s.name,
                        path              = s.path,
                        build_index       = s.buildIndex,
                        is_loaded         = s.isLoaded,
                        is_dirty          = s.isDirty,
                        is_active         = s == active,
                        root_count        = rootNames.Length,
                        root_gameobjects  = rootNames
                    });
                }

                return new
                {
                    open_scene_count = SceneManager.sceneCount,
                    scenes           = scenes,
                    read_at_utc      = DateTime.UtcNow.ToString("o"),
                    frame            = (long)Time.frameCount
                };
            });
        }
    }
}
