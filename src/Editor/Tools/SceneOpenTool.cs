using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    internal static class SceneOpenTool
    {
        [ReifyTool("scene-open")]
        public static Task<object> Handle(JToken args)
        {
            var path     = args?.Value<string>("path")     ?? throw new ArgumentException("path is required");
            var additive = args?.Value<bool?>("additive") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!File.Exists(path))
                    throw new InvalidOperationException($"Scene not found: {path}");

                var mode  = additive ? OpenSceneMode.Additive : OpenSceneMode.Single;
                var scene = EditorSceneManager.OpenScene(path, mode);
                return SceneInfoDto.Build(scene, includeRoots: true);
            });
        }
    }
}
