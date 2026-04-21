using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    internal static class SceneSaveTool
    {
        [ReifyTool("scene-save")]
        public static Task<object> Handle(JToken args)
        {
            var path       = args?.Value<string>("path");
            var saveAsCopy = args?.Value<bool?>("save_as_copy") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scene = SceneManager.GetActiveScene();
                bool saved;

                if (string.IsNullOrEmpty(path))
                    saved = EditorSceneManager.SaveScene(scene);
                else
                    saved = EditorSceneManager.SaveScene(scene, path, saveAsCopy);

                if (!saved)
                    throw new InvalidOperationException(
                        $"SaveScene returned false for path '{path ?? scene.path}'. " +
                        "Check Unity Console for details.");

                // Read back the post-save scene state so the response reflects disk truth.
                var reread = string.IsNullOrEmpty(path) ? scene : SceneManager.GetSceneByPath(path);
                return SceneInfoDto.Build(reread, includeRoots: true);
            });
        }
    }
}
