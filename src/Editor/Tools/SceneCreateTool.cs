using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class SceneCreateTool
    {
        [ReifyTool("scene-create")]
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

                var existedBefore = File.Exists(path);
                var setup = setupDefault ? NewSceneSetup.DefaultGameObjects : NewSceneSetup.EmptyScene;
                var scene = EditorSceneManager.NewScene(setup, NewSceneMode.Single);

                var saved = EditorSceneManager.SaveScene(scene, path);
                if (!saved)
                    throw new InvalidOperationException(
                        $"SaveScene returned false while creating '{path}'. Check Unity Console.");

                AssetDatabase.Refresh();
                var guid = AssetDatabase.AssetPathToGUID(path);
                var rootCount = scene.GetRootGameObjects().Length;

                var dto = SceneInfoDto.Build(scene, includeRoots: true);
                return new
                {
                    scene               = dto,
                    applied_fields      = new object[]
                    {
                        new { field = "scene_exists_at_path",
                              path   = path,
                              before = existedBefore, after = true },
                        new { field = "root_gameobject_count",
                              before = 0, after = rootCount }
                    },
                    applied_count       = 2,
                    created_provenance  = AssetProvenance.Summarize(path),
                    guids_touched       = new[] { guid },
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }
    }
}
