using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// C# script asset tools. Kept deliberately evidence-heavy: every read
    /// and write returns structured code facts (hash, declarations, namespace,
    /// warnings) so an agent can reason about scripts from code evidence.
    /// </summary>
    internal static class ScriptTools
    {
        [ReifyTool("script-read")]
        public static Task<object> Read(JToken args)
        {
            var assetPath      = args?.Value<string>("asset_path") ?? throw new ArgumentException("asset_path is required.");
            var includeContent = args?.Value<bool?>("include_content") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var abs = ScriptEvidence.AbsolutePath(assetPath);
                if (!File.Exists(abs))
                    throw new InvalidOperationException($"Script file not found: {assetPath}");

                var content = File.ReadAllText(abs);
                return new
                {
                    script          = ScriptEvidence.Summarize(assetPath, content),
                    content         = includeContent ? content : null,
                    content_included = includeContent,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("script-update-or-create")]
        public static Task<object> UpdateOrCreate(JToken args)
        {
            var assetPath = args?.Value<string>("asset_path") ?? throw new ArgumentException("asset_path is required.");
            var content   = args?.Value<string>("content") ?? throw new ArgumentException("content is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var abs = ScriptEvidence.AbsolutePath(assetPath);
                var existed = File.Exists(abs);
                var beforeText = existed ? File.ReadAllText(abs) : null;
                var before = existed ? ScriptEvidence.Summarize(assetPath, beforeText) : null;

                ScriptEvidence.EnsureParentFolder(assetPath);
                File.WriteAllText(abs, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                var afterText = File.ReadAllText(abs);
                var after = ScriptEvidence.Summarize(assetPath, afterText);

                return new
                {
                    created             = !existed,
                    updated             = existed,
                    before,
                    after,
                    compile_may_trigger = true,
                    note                = "Writing a .cs asset usually triggers async reimport/compilation. Poll domain-reload-status if you need a readiness gate.",
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("script-inspect")]
        public static Task<object> Inspect(JToken args)
        {
            var assetPath = args?.Value<string>("asset_path") ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() => ScriptRoslyn.Inspect(assetPath));
        }

        [ReifyTool("script-delete")]
        public static Task<object> Delete(JToken args)
        {
            var assetPath = args?.Value<string>("asset_path") ?? throw new ArgumentException("asset_path is required.");
            var useTrash  = args?.Value<bool?>("use_trash") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var abs = ScriptEvidence.AbsolutePath(assetPath);
                if (!File.Exists(abs))
                    throw new InvalidOperationException($"Script file not found: {assetPath}");

                var beforeText = File.ReadAllText(abs);
                var before = ScriptEvidence.Summarize(assetPath, beforeText);

                var ok = useTrash
                    ? AssetDatabase.MoveAssetToTrash(assetPath)
                    : AssetDatabase.DeleteAsset(assetPath);
                if (!ok)
                    throw new InvalidOperationException(
                        $"{(useTrash ? "MoveAssetToTrash" : "DeleteAsset")} refused: {assetPath}. Check Unity Console for details.");

                AssetDatabase.SaveAssets();

                return new
                {
                    deleted = new
                    {
                        asset_path          = assetPath,
                        use_trash           = useTrash,
                        compile_may_trigger = true
                    },
                    before,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }
    }
}
