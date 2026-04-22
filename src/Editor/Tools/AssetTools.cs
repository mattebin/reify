using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Asset-* tool handlers. One file for the six-tool asset domain because
    /// each handler is small and they share helpers.
    /// </summary>
    internal static class AssetTools
    {
        // ---------- asset-find ----------
        [ReifyTool("asset-find")]
        public static Task<object> Find(JToken args)
        {
            var name = args?.Value<string>("name");
            var type = args?.Value<string>("type");      // e.g. "Material", "Texture", "t:Prefab"
            var guid = args?.Value<string>("guid");
            var path = args?.Value<string>("path");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var hits = new List<string>();

                if (!string.IsNullOrEmpty(path))
                {
                    if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                        hits.Add(path);
                }
                else if (!string.IsNullOrEmpty(guid))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(p)) hits.Add(p);
                }
                else
                {
                    // AssetDatabase.FindAssets query syntax: "name t:Type"
                    var q = "";
                    if (!string.IsNullOrEmpty(name)) q += name;
                    if (!string.IsNullOrEmpty(type))
                        q += (q.Length > 0 ? " " : "") + (type.StartsWith("t:") ? type : "t:" + type);
                    if (q.Length == 0)
                        throw new ArgumentException("Provide at least one of: name, type, guid, path.");
                    foreach (var g in AssetDatabase.FindAssets(q))
                        hits.Add(AssetDatabase.GUIDToAssetPath(g));
                }

                var results = new List<object>(hits.Count);
                foreach (var p in hits) results.Add(AssetSummary(p));

                return new
                {
                    match_count = results.Count,
                    matches     = results,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- asset-create ----------
        [ReifyTool("asset-create")]
        public static Task<object> Create(JToken args)
        {
            var kind      = args?.Value<string>("kind")      ?? throw new ArgumentException("kind is required ('folder', 'material', 'scriptable_object').");
            var path      = args?.Value<string>("path")      ?? throw new ArgumentException("path is required.");
            var typeName  = args?.Value<string>("type_name");  // SO only
            var shaderName = args?.Value<string>("shader")   ?? "Universal Render Pipeline/Lit";

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new ArgumentException($"path must start with 'Assets/': {path}");

                switch (kind.ToLowerInvariant())
                {
                    case "folder":
                    {
                        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
                        var leaf   = Path.GetFileName(path);
                        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                            throw new ArgumentException($"Invalid folder path: {path}");
                        var guid = AssetDatabase.CreateFolder(parent, leaf);
                        if (string.IsNullOrEmpty(guid))
                            throw new InvalidOperationException($"CreateFolder refused: {path}");
                        AssetDatabase.SaveAssets();
                        return AssetSummaryEnvelope(AssetDatabase.GUIDToAssetPath(guid));
                    }
                    case "material":
                    {
                        var shader = Shader.Find(shaderName)
                            ?? throw new InvalidOperationException($"Shader not found: {shaderName}");
                        var mat = new Material(shader);
                        EnsureParentFolder(path);
                        AssetDatabase.CreateAsset(mat, path);
                        AssetDatabase.SaveAssets();
                        return AssetSummaryEnvelope(path);
                    }
                    case "scriptable_object":
                    {
                        if (string.IsNullOrEmpty(typeName))
                            throw new ArgumentException("scriptable_object requires type_name.");
                        var t = ResolveType(typeName)
                            ?? throw new InvalidOperationException($"Type not found: {typeName}");
                        if (!typeof(ScriptableObject).IsAssignableFrom(t))
                            throw new InvalidOperationException($"{t.FullName} is not a ScriptableObject.");
                        var so = ScriptableObject.CreateInstance(t);
                        EnsureParentFolder(path);
                        AssetDatabase.CreateAsset(so, path);
                        AssetDatabase.SaveAssets();
                        return AssetSummaryEnvelope(path);
                    }
                    default:
                        throw new ArgumentException(
                            $"Unknown kind '{kind}'. Supported: folder, material, scriptable_object.");
                }
            });
        }

        // ---------- asset-delete ----------
        [ReifyTool("asset-delete")]
        public static Task<object> Delete(JToken args)
        {
            var path        = args?.Value<string>("path") ?? throw new ArgumentException("path is required.");
            var useTrash    = args?.Value<bool?>("use_trash") ?? true; // MoveAssetToTrash = reversible

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid))
                    throw new InvalidOperationException($"Asset not found: {path}");

                // Forward dependencies — what this asset depends on. A proper
                // reverse-dependency scan (who depends on this?) requires a
                // full AssetDatabase scan which we defer.
                var dependencies = AssetDatabase.GetDependencies(path, recursive: false);

                var warnings = new List<string>();
                if (dependencies.Length > 1)
                    warnings.Add(
                        $"Asset references {dependencies.Length - 1} other asset(s). " +
                        "Reverse-dependency scan (who references THIS) not performed — " +
                        "deleting may orphan references in scenes/prefabs.");

                // Capture pre-delete provenance so the receipt includes the
                // full {path, guid, type_fqn, instance_id} we just removed.
                var deletedSummary = AssetProvenance.Summarize(path);

                var ok = useTrash
                    ? AssetDatabase.MoveAssetToTrash(path)
                    : AssetDatabase.DeleteAsset(path);
                if (!ok)
                    throw new InvalidOperationException(
                        $"{(useTrash ? "MoveAssetToTrash" : "DeleteAsset")} refused: {path}. " +
                        "Check Unity Console for details.");

                AssetDatabase.SaveAssets();

                return new
                {
                    deleted = new { path, guid, use_trash = useTrash },
                    deleted_provenance = deletedSummary,
                    guids_touched = new[] { deletedSummary },
                    dependencies = dependencies,
                    warnings,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- asset-get ----------
        [ReifyTool("asset-get")]
        public static Task<object> Get(JToken args)
        {
            var path              = args?.Value<string>("path") ?? throw new ArgumentException("path is required.");
            var includeProperties = args?.Value<bool?>("include_properties") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)
                    ?? throw new InvalidOperationException($"Asset not found: {path}");

                var importer = AssetImporter.GetAtPath(path);
                var guid     = AssetDatabase.AssetPathToGUID(path);
                var deps     = AssetDatabase.GetDependencies(path, recursive: false);
                var labels   = AssetDatabase.GetLabels(obj);

                object properties = null;
                if (includeProperties)
                {
                    var props = new List<object>();
                    try
                    {
                        using var so = new SerializedObject(obj);
                        var it = so.GetIterator();
                        if (it.NextVisible(true))
                        {
                            do
                            {
                                props.Add(new
                                {
                                    name  = it.name,
                                    type  = it.propertyType.ToString(),
                                    value = ReadValue(it)
                                });
                            } while (it.NextVisible(false));
                        }
                        properties = props.ToArray();
                    }
                    catch (Exception ex)
                    {
                        properties = new { read_error = ex.Message };
                    }
                }

                return new
                {
                    path,
                    guid,
                    type_fqn       = obj.GetType().FullName,
                    name           = obj.name,
                    asset_bundle   = importer != null ? importer.assetBundleName : "",
                    importer_type  = importer != null ? importer.GetType().FullName : null,
                    labels,
                    dependencies   = deps,
                    instance_id    = GameObjectResolver.InstanceIdOf(obj),
                    properties,
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- asset-rename ----------
        [ReifyTool("asset-rename")]
        public static Task<object> Rename(JToken args)
        {
            var path    = args?.Value<string>("path")     ?? throw new ArgumentException("path is required.");
            var newName = args?.Value<string>("new_name") ?? throw new ArgumentException("new_name is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var err = AssetDatabase.RenameAsset(path, newName);
                if (!string.IsNullOrEmpty(err))
                    throw new InvalidOperationException($"RenameAsset failed: {err}");
                AssetDatabase.SaveAssets();

                var newPath = Path.GetDirectoryName(path)?.Replace('\\', '/') + "/" + newName +
                              (Path.HasExtension(path) ? Path.GetExtension(path) : "");
                return new
                {
                    renamed     = new { from = path, to = newPath },
                    asset       = AssetSummary(newPath),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- asset-move ----------
        [ReifyTool("asset-move")]
        public static Task<object> Move(JToken args)
        {
            var from = args?.Value<string>("from") ?? throw new ArgumentException("from is required.");
            var to   = args?.Value<string>("to")   ?? throw new ArgumentException("to is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                EnsureParentFolder(to);
                var err = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(err))
                    throw new InvalidOperationException($"MoveAsset failed: {err}");
                AssetDatabase.SaveAssets();
                return new
                {
                    moved       = new { from, to },
                    asset       = AssetSummary(to),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- asset-copy ----------
        [ReifyTool("asset-copy")]
        public static Task<object> Copy(JToken args)
        {
            var from = args?.Value<string>("from") ?? throw new ArgumentException("from is required.");
            var to   = args?.Value<string>("to")   ?? throw new ArgumentException("to is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(from)))
                    throw new InvalidOperationException($"Asset not found: {from}");

                EnsureParentFolder(to);
                var ok = AssetDatabase.CopyAsset(from, to);
                if (!ok)
                    throw new InvalidOperationException($"CopyAsset refused: {from} -> {to}");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var sourceProv = AssetProvenance.Summarize(from);
                var destProv   = AssetProvenance.Summarize(to);
                return new
                {
                    copied      = new { from, to },
                    asset       = AssetSummary(to),
                    source_provenance = sourceProv,
                    destination_provenance = destProv,
                    guids_touched = new[] { sourceProv, destProv },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- asset-refresh ----------
        [ReifyTool("asset-refresh")]
        public static Task<object> Refresh(JToken args)
        {
            var path        = args?.Value<string>("path");
            var forceUpdate = args?.Value<bool?>("force_update") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var options = forceUpdate ? ImportAssetOptions.ForceUpdate : ImportAssetOptions.Default;

                if (!string.IsNullOrEmpty(path))
                {
                    if (!path.StartsWith("Assets/", StringComparison.Ordinal) &&
                        !path.StartsWith("Packages/", StringComparison.Ordinal))
                        throw new ArgumentException($"path must start with 'Assets/' or 'Packages/': {path}");

                    AssetDatabase.ImportAsset(path, options);
                    AssetDatabase.Refresh(options);

                    var guid = AssetDatabase.AssetPathToGUID(path);
                    var exists = !string.IsNullOrEmpty(guid);
                    return new
                    {
                        refreshed = new
                        {
                            scope        = "asset",
                            path,
                            force_update = forceUpdate,
                            exists
                        },
                        asset       = exists ? AssetSummary(path) : null,
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame       = (long)Time.frameCount
                    };
                }

                AssetDatabase.Refresh(options);
                return new
                {
                    refreshed = new
                    {
                        scope        = "project",
                        force_update = forceUpdate
                    },
                    asset_count  = AssetDatabase.GetAllAssetPaths().Length,
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- asset-dependencies ----------
        [ReifyTool("asset-dependencies")]
        public static Task<object> Dependencies(JToken args)
        {
            var assetPath = args?.Value<string>("asset_path") ?? throw new ArgumentException("asset_path is required.");
            var recursive = args?.Value<bool?>("recursive") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
                    throw new InvalidOperationException($"Asset not found: {assetPath}");

                var deps = AssetDatabase.GetDependencies(assetPath, recursive);
                var list = new List<object>(deps.Length);
                foreach (var dep in deps)
                {
                    if (string.Equals(dep, assetPath, StringComparison.Ordinal))
                        continue;
                    list.Add(AssetSummary(dep));
                }

                return new
                {
                    asset = AssetSummary(assetPath),
                    recursive,
                    dependency_count = list.Count,
                    dependencies     = list.ToArray(),
                    read_at_utc      = DateTime.UtcNow.ToString("o"),
                    frame            = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------

        private static object AssetSummary(string path)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            return new
            {
                path,
                guid     = AssetDatabase.AssetPathToGUID(path),
                type_fqn = obj != null ? obj.GetType().FullName : null,
                name     = obj != null ? obj.name : null,
                instance_id = obj != null ? GameObjectResolver.InstanceIdOf(obj) : 0
            };
        }

        private static object AssetSummaryEnvelope(string path) => new
        {
            asset       = AssetSummary(path),
            read_at_utc = DateTime.UtcNow.ToString("o"),
            frame       = (long)Time.frameCount
        };

        private static void EnsureParentFolder(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir) || AssetDatabase.IsValidFolder(dir)) return;
            var parts = dir.Split('/');
            var accum = parts[0]; // "Assets"
            for (var i = 1; i < parts.Length; i++)
            {
                var next = accum + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(accum, parts[i]);
                accum = next;
            }
        }

        private static Type ResolveType(string typeName)
        {
            var t = Type.GetType(typeName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(typeName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { /* ignore */ }
            }
            return null;
        }

        private static object ReadValue(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:    return p.intValue;
                case SerializedPropertyType.Boolean:    return p.boolValue;
                case SerializedPropertyType.Float:      return p.floatValue;
                case SerializedPropertyType.String:     return p.stringValue;
                case SerializedPropertyType.Color:      return new { r=p.colorValue.r, g=p.colorValue.g, b=p.colorValue.b, a=p.colorValue.a };
                case SerializedPropertyType.Vector2:    return new { x=p.vector2Value.x, y=p.vector2Value.y };
                case SerializedPropertyType.Vector3:    return new { x=p.vector3Value.x, y=p.vector3Value.y, z=p.vector3Value.z };
                case SerializedPropertyType.Vector4:    return new { x=p.vector4Value.x, y=p.vector4Value.y, z=p.vector4Value.z, w=p.vector4Value.w };
                case SerializedPropertyType.Quaternion: return new { x=p.quaternionValue.x, y=p.quaternionValue.y, z=p.quaternionValue.z, w=p.quaternionValue.w };
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue == null ? null : new
                    {
                        type_fqn    = p.objectReferenceValue.GetType().FullName,
                        instance_id = GameObjectResolver.InstanceIdOf(p.objectReferenceValue),
                        name        = p.objectReferenceValue.name
                    };
                case SerializedPropertyType.LayerMask:  return p.intValue;
                case SerializedPropertyType.Enum:       return p.enumValueIndex;
                default:                                return $"<{p.propertyType}>";
            }
        }
    }
}
