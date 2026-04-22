using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Assembly Definition tooling for no-click C# project structure work.
    /// Responses are evidence-first: parsed schema, SHA-256, GUID, warnings,
    /// and post-write readback.
    /// </summary>
    internal static class AsmdefTools
    {
        [ReifyTool("asmdef-list")]
        public static Task<object> List(JToken args)
        {
            var nameFilter      = args?.Value<string>("name_filter");
            var includePackages = args?.Value<bool?>("include_packages") ?? false;
            var limit           = Math.Max(1, args?.Value<int?>("limit") ?? 200);
            var offset          = Math.Max(0, args?.Value<int?>("offset") ?? 0);

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
                var items = new List<(string AssetPath, object Summary)>();

                foreach (var guid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        continue;
                    }

                    if (!includePackages &&
                        assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(nameFilter) &&
                        assetPath.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    items.Add((assetPath, Summarize(assetPath, includeDefinition: false, includeRawText: false)));
                }

                items.Sort((a, b) =>
                    StringComparer.OrdinalIgnoreCase.Compare(
                        a.AssetPath,
                        b.AssetPath));

                var total = items.Count;
                var page = new List<object>();
                for (var i = offset; i < items.Count && page.Count < limit; i++)
                {
                    page.Add(items[i].Summary);
                }

                return new
                {
                    total_matching = total,
                    returned       = page.Count,
                    limit,
                    offset,
                    truncated      = offset + page.Count < total,
                    include_packages = includePackages,
                    name_filter    = nameFilter,
                    asmdefs        = page.ToArray(),
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("asmdef-inspect")]
        public static Task<object> Inspect(JToken args)
        {
            var assetPath      = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");
            var includeRawText = args?.Value<bool?>("include_raw_text") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                ValidatePath(assetPath);
                return WithReadMetadata(Summarize(assetPath, includeDefinition: true, includeRawText: includeRawText));
            });
        }

        [ReifyTool("asmdef-update-or-create")]
        public static Task<object> UpdateOrCreate(JToken args)
        {
            var assetPath = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                ValidatePath(assetPath);

                var beforeExists = Exists(assetPath);
                var before = beforeExists
                    ? Summarize(assetPath, includeDefinition: true, includeRawText: false)
                    : null;

                var working = beforeExists
                    ? ReadDefinition(assetPath)
                    : CreateDefaultDefinition(Path.GetFileNameWithoutExtension(assetPath));

                var patch = ExtractPatch(args);
                MergeInto(working, patch);

                if (string.IsNullOrWhiteSpace(working.Value<string>("name")))
                    working["name"] = Path.GetFileNameWithoutExtension(assetPath);

                EnsureParentFolder(assetPath);
                WriteDefinition(assetPath, NormalizeDefinition(working));

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                var after = Summarize(assetPath, includeDefinition: true, includeRawText: false);

                return new
                {
                    created      = !beforeExists,
                    asset_path   = assetPath,
                    before,
                    after,
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("asmdef-delete")]
        public static Task<object> Delete(JToken args)
        {
            var assetPath = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");
            var useTrash = args?.Value<bool?>("use_trash") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                ValidatePath(assetPath);
                if (!Exists(assetPath))
                    throw new InvalidOperationException($"No .asmdef asset exists at path: {assetPath}");

                var before = Summarize(assetPath, includeDefinition: true, includeRawText: false);
                var ok = useTrash
                    ? AssetDatabase.MoveAssetToTrash(assetPath)
                    : AssetDatabase.DeleteAsset(assetPath);

                if (!ok)
                    throw new InvalidOperationException($"Unity refused to delete asmdef asset: {assetPath}");

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                return new
                {
                    deleted      = true,
                    use_trash    = useTrash,
                    asset_path   = assetPath,
                    before,
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        private static object WithReadMetadata(object value)
        {
            var obj = JObject.FromObject(value);
            obj["read_at_utc"] = DateTime.UtcNow.ToString("o");
            obj["frame"] = (long)Time.frameCount;
            return obj;
        }

        private static object Summarize(
            string assetPath,
            bool includeDefinition,
            bool includeRawText)
        {
            var text = File.ReadAllText(AbsolutePath(assetPath), Encoding.UTF8);
            var definition = JObject.Parse(text);
            var name = definition.Value<string>("name");
            var references = ReadStringArray(definition["references"]);
            var includePlatforms = ReadStringArray(definition["includePlatforms"]);
            var excludePlatforms = ReadStringArray(definition["excludePlatforms"]);
            var precompiledReferences = ReadStringArray(definition["precompiledReferences"]);
            var defineConstraints = ReadStringArray(definition["defineConstraints"]);
            var versionDefines = ReadVersionDefines(definition["versionDefines"]);
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var warnings = BuildWarnings(
                assetPath,
                name,
                references,
                includePlatforms,
                excludePlatforms,
                precompiledReferences,
                definition.Value<bool?>("autoReferenced"),
                definition.Value<bool?>("overrideReferences"));

            return new
            {
                asset_path             = assetPath,
                guid,
                absolute_path          = AbsolutePath(assetPath).Replace('\\', '/'),
                file_name              = Path.GetFileName(assetPath),
                assembly_name          = name,
                root_namespace         = definition.Value<string>("rootNamespace"),
                exists                 = true,
                sha256                 = Sha256(text),
                char_count             = text.Length,
                reference_count        = references.Length,
                references,
                include_platforms      = includePlatforms,
                exclude_platforms      = excludePlatforms,
                allow_unsafe_code      = definition.Value<bool?>("allowUnsafeCode") ?? false,
                override_references    = definition.Value<bool?>("overrideReferences") ?? false,
                precompiled_references = precompiledReferences,
                auto_referenced        = definition.Value<bool?>("autoReferenced") ?? true,
                define_constraints     = defineConstraints,
                version_defines        = versionDefines,
                no_engine_references   = definition.Value<bool?>("noEngineReferences") ?? false,
                definition             = includeDefinition ? definition : null,
                raw_text               = includeRawText ? text : null,
                warnings               = warnings.ToArray()
            };
        }

        private static List<string> BuildWarnings(
            string assetPath,
            string assemblyName,
            string[] references,
            string[] includePlatforms,
            string[] excludePlatforms,
            string[] precompiledReferences,
            bool? autoReferenced,
            bool? overrideReferences)
        {
            var warnings = new List<string>();
            var stem = Path.GetFileNameWithoutExtension(assetPath);

            if (string.IsNullOrWhiteSpace(assemblyName))
                warnings.Add("Assembly definition has no 'name'. Unity will refuse to compile it correctly.");
            if (!string.IsNullOrWhiteSpace(assemblyName) &&
                !string.Equals(stem, assemblyName, StringComparison.Ordinal))
                warnings.Add($"File name '{stem}' does not match assembly name '{assemblyName}'. This is legal, but often confuses project navigation.");
            if (includePlatforms.Length > 0 && excludePlatforms.Length > 0)
                warnings.Add("Both includePlatforms and excludePlatforms are populated. Unity treats these as mutually exclusive intent; double-check the targeting.");
            if ((overrideReferences ?? false) == false && precompiledReferences.Length > 0)
                warnings.Add("precompiledReferences are present while overrideReferences is false. Unity ignores precompiledReferences unless overrideReferences is true.");
            if ((autoReferenced ?? true) == false && references.Length == 0)
                warnings.Add("autoReferenced is false and the asmdef declares no references. This assembly may become effectively unreachable unless another asmdef references it.");

            return warnings;
        }

        private static void ValidatePath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("asset_path is required.");
            if (!(assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                  assetPath.StartsWith("Packages/", StringComparison.Ordinal)))
                throw new ArgumentException("asset_path must be under Assets/ or Packages/.");
            if (!assetPath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("asset_path must point to a .asmdef file.");
        }

        private static bool Exists(string assetPath) => File.Exists(AbsolutePath(assetPath));

        private static string AbsolutePath(string assetPath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath)
                ?? throw new InvalidOperationException("Could not resolve the Unity project root.");

            var relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(projectRoot, relative));
            var root = Path.GetFullPath(projectRoot + Path.DirectorySeparatorChar);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Resolved path escapes the Unity project root: {assetPath}");
            return full;
        }

        private static void EnsureParentFolder(string assetPath)
        {
            var parent = Path.GetDirectoryName(AbsolutePath(assetPath));
            if (string.IsNullOrEmpty(parent))
                throw new InvalidOperationException($"Could not resolve a parent directory for {assetPath}");
            Directory.CreateDirectory(parent);
        }

        private static JObject ReadDefinition(string assetPath)
            => JObject.Parse(File.ReadAllText(AbsolutePath(assetPath), Encoding.UTF8));

        private static JObject CreateDefaultDefinition(string defaultName)
            => new JObject
            {
                ["name"] = defaultName,
                ["references"] = new JArray(),
                ["includePlatforms"] = new JArray(),
                ["excludePlatforms"] = new JArray(),
                ["allowUnsafeCode"] = false,
                ["overrideReferences"] = false,
                ["precompiledReferences"] = new JArray(),
                ["autoReferenced"] = true,
                ["defineConstraints"] = new JArray(),
                ["versionDefines"] = new JArray(),
                ["noEngineReferences"] = false
            };

        private static JObject ExtractPatch(JToken args)
        {
            if (args?["definition"] is JObject nested)
                return (JObject)nested.DeepClone();

            var root = new JObject();
            if (args is not JObject obj)
                return root;

            foreach (var property in obj.Properties())
            {
                if (string.Equals(property.Name, "asset_path", StringComparison.OrdinalIgnoreCase))
                    continue;
                root[property.Name] = property.Value.DeepClone();
            }

            return root;
        }

        private static void MergeInto(JObject target, JObject patch)
        {
            foreach (var property in patch.Properties())
            {
                if (property.Value.Type == JTokenType.Null)
                    target.Remove(property.Name);
                else
                    target[property.Name] = property.Value.DeepClone();
            }
        }

        private static JObject NormalizeDefinition(JObject source)
        {
            var normalized = new JObject();
            CopyIfPresent(source, normalized, "name");
            CopyIfPresent(source, normalized, "rootNamespace");
            CopyIfPresent(source, normalized, "references");
            CopyIfPresent(source, normalized, "includePlatforms");
            CopyIfPresent(source, normalized, "excludePlatforms");
            CopyIfPresent(source, normalized, "allowUnsafeCode");
            CopyIfPresent(source, normalized, "overrideReferences");
            CopyIfPresent(source, normalized, "precompiledReferences");
            CopyIfPresent(source, normalized, "autoReferenced");
            CopyIfPresent(source, normalized, "defineConstraints");
            CopyIfPresent(source, normalized, "versionDefines");
            CopyIfPresent(source, normalized, "noEngineReferences");

            foreach (var property in source.Properties())
            {
                if (normalized[property.Name] == null)
                    normalized[property.Name] = property.Value.DeepClone();
            }
            return normalized;
        }

        private static void CopyIfPresent(JObject from, JObject to, string name)
        {
            if (from.TryGetValue(name, out var value))
                to[name] = value.DeepClone();
        }

        private static void WriteDefinition(string assetPath, JObject definition)
        {
            var text = definition.ToString(Formatting.Indented) + Environment.NewLine;
            File.WriteAllText(
                AbsolutePath(assetPath),
                text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static string[] ReadStringArray(JToken token)
        {
            if (token is not JArray arr)
                return Array.Empty<string>();

            var list = new List<string>(arr.Count);
            foreach (var item in arr)
            {
                if (item.Type == JTokenType.String)
                    list.Add(item.Value<string>());
            }
            return list.ToArray();
        }

        private static object[] ReadVersionDefines(JToken token)
        {
            if (token is not JArray arr)
                return Array.Empty<object>();

            var list = new List<object>(arr.Count);
            foreach (var item in arr)
            {
                if (item is not JObject obj)
                    continue;

                list.Add(new
                {
                    name       = obj.Value<string>("name"),
                    expression = obj.Value<string>("expression"),
                    define     = obj.Value<string>("define")
                });
            }
            return list.ToArray();
        }

        private static string Sha256(string text)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(hash.Length * 2);
            for (var i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
