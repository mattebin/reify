using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
                var beforePath  = scene.path;
                var beforeDirty = scene.isDirty;
                var targetPath  = string.IsNullOrEmpty(path) ? beforePath : path;

                long beforeSizeBytes = 0;
                string beforeWriteUtc = null;
                if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                {
                    var fi = new FileInfo(targetPath);
                    beforeSizeBytes = fi.Length;
                    beforeWriteUtc  = fi.LastWriteTimeUtc.ToString("o");
                }

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
                var dto = SceneInfoDto.Build(reread, includeRoots: true);

                long afterSizeBytes = 0;
                string afterWriteUtc = null;
                if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                {
                    var fi = new FileInfo(targetPath);
                    afterSizeBytes = fi.Length;
                    afterWriteUtc  = fi.LastWriteTimeUtc.ToString("o");
                }

                var guid = AssetDatabase.AssetPathToGUID(targetPath);

                return new
                {
                    scene           = dto,
                    applied_fields  = new object[]
                    {
                        new { field = "scene_is_dirty", before = beforeDirty, after = reread.isDirty },
                        new { field = "disk_size_bytes", path = targetPath,
                              before = beforeSizeBytes, after = afterSizeBytes },
                        new { field = "disk_last_write_utc", path = targetPath,
                              before = beforeWriteUtc, after = afterWriteUtc }
                    },
                    applied_count   = 3,
                    save_as_copy    = saveAsCopy,
                    guids_touched   = string.IsNullOrEmpty(guid) ? new string[0] : new[] { guid },
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }
    }
}
