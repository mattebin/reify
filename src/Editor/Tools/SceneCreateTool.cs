using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor.SceneManagement;

namespace Reify.Editor.Tools
{
    internal static class SceneCreateTool
    {
        public static Task<object> Handle(JToken args)
        {
            var path         = args?.Value<string>("path")        ?? throw new ArgumentException("path is required");
            var setupDefault = args?.Value<bool?>("setup_default") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new ArgumentException($"Scene path must start with 'Assets/': {path}");

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var setup = setupDefault ? NewSceneSetup.DefaultGameObjects : NewSceneSetup.EmptyScene;
                var scene = EditorSceneManager.NewScene(setup, NewSceneMode.Single);

                var saved = EditorSceneManager.SaveScene(scene, path);
                if (!saved)
                    throw new InvalidOperationException(
                        $"SaveScene returned false while creating '{path}'. Check Unity Console.");

                return SceneInfoDto.Build(scene, includeRoots: true);
            });
        }
    }
}
