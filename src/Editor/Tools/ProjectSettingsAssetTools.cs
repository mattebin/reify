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
    /// Generic read/write for fields inside ProjectSettings/*.asset.
    /// Specific tools (project-tag-add, project-layer-set, etc.) cover the
    /// common cases; this is the fallback for everything else, including
    /// the activeInputHandler bug-of-the-day that previously had no
    /// reify-side fix and forced an editor restart.
    ///
    /// Read uses the same SerializedObject path as the Inspector. Write
    /// uses SerializedProperty.* with type-aware setters and a final
    /// AssetDatabase.SaveAssets so values persist immediately.
    /// </summary>
    internal static class ProjectSettingsAssetTools
    {
        // ---------- project-settings-asset-read ----------
        [ReifyTool("project-settings-asset-read")]
        public static Task<object> Read(JToken args)
        {
            var asset = args?.Value<string>("asset")
                ?? throw new ArgumentException("asset is required (e.g. 'ProjectSettings.asset', 'GraphicsSettings.asset').");
            var propertyPath = args?.Value<string>("property_path"); // null → return all top-level field names

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var fullPath = ResolveAssetPath(asset);
                var loaded = AssetDatabase.LoadAllAssetsAtPath(fullPath);
                if (loaded == null || loaded.Length == 0)
                    throw new InvalidOperationException($"Could not load any asset from '{fullPath}'.");

                // ProjectSettings/*.asset are typically a single root asset.
                var settings = loaded[0];
                var so = new SerializedObject(settings);

                if (string.IsNullOrEmpty(propertyPath))
                {
                    // Walk the SerializedObject and return the field roots.
                    var names = new List<object>();
                    var iter = so.GetIterator();
                    if (iter.NextVisible(true))
                    {
                        do
                        {
                            names.Add(new
                            {
                                name        = iter.name,
                                display     = iter.displayName,
                                type        = iter.propertyType.ToString(),
                                is_array    = iter.isArray,
                                array_size  = iter.isArray ? iter.arraySize : 0,
                                serialized_path = iter.propertyPath
                            });
                        } while (iter.NextVisible(false));
                    }
                    return new
                    {
                        asset = fullPath,
                        object_type = settings.GetType().FullName,
                        field_count = names.Count,
                        fields      = names.ToArray(),
                        note        = "Pass property_path to read a specific field's value.",
                        read_at_utc = DateTime.UtcNow.ToString("o")
                    };
                }

                var prop = so.FindProperty(propertyPath);
                if (prop == null)
                    throw new InvalidOperationException(
                        $"Property '{propertyPath}' not found on '{settings.GetType().FullName}'. " +
                        "Call this tool without property_path to list available fields.");

                return new
                {
                    asset          = fullPath,
                    object_type    = settings.GetType().FullName,
                    property_path  = propertyPath,
                    type           = prop.propertyType.ToString(),
                    value          = ReadValue(prop),
                    is_array       = prop.isArray,
                    array_size     = prop.isArray ? prop.arraySize : 0,
                    read_at_utc    = DateTime.UtcNow.ToString("o")
                };
            });
        }

        // ---------- project-settings-asset-write ----------
        [ReifyTool("project-settings-asset-write")]
        public static Task<object> Write(JToken args)
        {
            var asset        = args?.Value<string>("asset")
                ?? throw new ArgumentException("asset is required.");
            var propertyPath = args?.Value<string>("property_path")
                ?? throw new ArgumentException("property_path is required.");
            var newValue     = args?["value"]
                ?? throw new ArgumentException("value is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var fullPath = ResolveAssetPath(asset);
                var loaded = AssetDatabase.LoadAllAssetsAtPath(fullPath);
                if (loaded == null || loaded.Length == 0)
                    throw new InvalidOperationException($"Could not load asset '{fullPath}'.");

                var settings = loaded[0];
                var so = new SerializedObject(settings);
                var prop = so.FindProperty(propertyPath);
                if (prop == null)
                    throw new InvalidOperationException($"Property '{propertyPath}' not found.");

                var before = ReadValue(prop);
                WriteValue(prop, newValue);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                return new
                {
                    asset         = fullPath,
                    property_path = propertyPath,
                    type          = prop.propertyType.ToString(),
                    before_value  = before,
                    after_value   = ReadValue(prop),
                    note          = "Some ProjectSettings changes (e.g. activeInputHandler) only take effect on editor restart.",
                    read_at_utc   = DateTime.UtcNow.ToString("o")
                };
            });
        }

        private static string ResolveAssetPath(string asset)
        {
            // Allow short names ("ProjectSettings.asset") or full paths.
            if (asset.Contains("/")) return asset;
            return "ProjectSettings/" + asset;
        }

        private static object ReadValue(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:    return p.intValue;
                case SerializedPropertyType.Boolean:    return p.boolValue;
                case SerializedPropertyType.Float:      return p.floatValue;
                case SerializedPropertyType.String:     return p.stringValue;
                case SerializedPropertyType.Color:      var c = p.colorValue; return new { r = c.r, g = c.g, b = c.b, a = c.a };
                case SerializedPropertyType.Vector2:    var v2 = p.vector2Value; return new { x = v2.x, y = v2.y };
                case SerializedPropertyType.Vector3:    var v3 = p.vector3Value; return new { x = v3.x, y = v3.y, z = v3.z };
                case SerializedPropertyType.Vector4:    var v4 = p.vector4Value; return new { x = v4.x, y = v4.y, z = v4.z, w = v4.w };
                case SerializedPropertyType.Enum:       return new { index = p.enumValueIndex, name = p.enumDisplayNames[Math.Clamp(p.enumValueIndex, 0, p.enumDisplayNames.Length - 1)] };
                case SerializedPropertyType.ObjectReference:
                    var o = p.objectReferenceValue;
                    return o == null ? null : new { name = o.name, type = o.GetType().FullName, instance_id = o.GetInstanceID() };
                default: return $"<unsupported:{p.propertyType}>";
            }
        }

        private static void WriteValue(SerializedProperty p, JToken val)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:  p.intValue    = val.Value<int>(); break;
                case SerializedPropertyType.Boolean:  p.boolValue   = val.Value<bool>(); break;
                case SerializedPropertyType.Float:    p.floatValue  = val.Value<float>(); break;
                case SerializedPropertyType.String:   p.stringValue = val.Value<string>() ?? ""; break;
                case SerializedPropertyType.Enum:     p.enumValueIndex = val.Value<int>(); break;
                case SerializedPropertyType.Vector2:
                    p.vector2Value = new Vector2(val.Value<float>("x"), val.Value<float>("y")); break;
                case SerializedPropertyType.Vector3:
                    p.vector3Value = new Vector3(val.Value<float>("x"), val.Value<float>("y"), val.Value<float>("z")); break;
                default:
                    throw new InvalidOperationException(
                        $"Writing {p.propertyType} is not supported by project-settings-asset-write yet.");
            }
        }
    }
}
