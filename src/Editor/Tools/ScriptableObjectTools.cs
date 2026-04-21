using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// ScriptableObject discovery + dedicated inspect. Creation is already
    /// covered by asset-create with kind='scriptable_object'; the useful
    /// additions here are type enumeration (so the caller knows what's
    /// creatable) and a focused inspector that emits the SO's field schema.
    /// </summary>
    internal static class ScriptableObjectTools
    {
        // ---------- scriptable-object-list-types ----------
        [ReifyTool("scriptable-object-list-types")]
        public static Task<object> ListTypes(JToken args)
        {
            var filter    = args?.Value<string>("name_filter");  // case-insensitive substring
            var userOnly  = args?.Value<bool?>("user_only") ?? true;
            var limit     = args?.Value<int?>("limit") ?? 500;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var soBase = typeof(ScriptableObject);
                var results = new List<object>();
                var truncated = false;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    // user_only filters out Unity-core assemblies so the list
                    // focuses on project-defined SOs.
                    if (userOnly)
                    {
                        var asmName = asm.GetName().Name ?? "";
                        if (asmName.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)) continue;
                        if (asmName.StartsWith("System",  StringComparison.OrdinalIgnoreCase)) continue;
                        if (asmName.StartsWith("mscorlib",StringComparison.OrdinalIgnoreCase)) continue;
                        if (asmName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)) continue;
                    }

                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types; }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null || t.IsAbstract) continue;
                        if (!soBase.IsAssignableFrom(t)) continue;
                        if (!string.IsNullOrEmpty(filter) &&
                            t.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        if (results.Count >= limit) { truncated = true; break; }

                        // CreateAssetMenu attribute surfaces any author-intended menu path.
                        var attr = Attribute.GetCustomAttribute(t, typeof(CreateAssetMenuAttribute)) as CreateAssetMenuAttribute;

                        results.Add(new
                        {
                            type_fqn   = t.FullName,
                            short_name = t.Name,
                            assembly   = asm.GetName().Name,
                            is_sealed  = t.IsSealed,
                            create_asset_menu = attr != null ? new {
                                menu_name      = attr.menuName,
                                file_name      = attr.fileName,
                                order          = attr.order
                            } : null
                        });
                    }
                    if (truncated) break;
                }

                results.Sort((a, b) =>
                    StringComparer.OrdinalIgnoreCase.Compare(
                        ((dynamic)a).type_fqn, ((dynamic)b).type_fqn));

                return new
                {
                    returned   = results.Count,
                    truncated,
                    limit,
                    user_only  = userOnly,
                    name_filter = filter,
                    types      = results.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- scriptable-object-inspect ----------
        [ReifyTool("scriptable-object-inspect")]
        public static Task<object> Inspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path)
                    ?? throw new InvalidOperationException($"No ScriptableObject asset at path: {path}");

                var fields = new List<object>();
                try
                {
                    using var serialized = new SerializedObject(so);
                    var it = serialized.GetIterator();
                    if (it.NextVisible(true))
                    {
                        do
                        {
                            fields.Add(new
                            {
                                name  = it.name,
                                type  = it.propertyType.ToString(),
                                value = ReadValue(it)
                            });
                        } while (it.NextVisible(false));
                    }
                }
                catch (Exception ex)
                {
                    fields.Add(new { name = "<error>", reason = ex.Message });
                }

                return new
                {
                    asset_path    = path,
                    type_fqn      = so.GetType().FullName,
                    instance_id   = GameObjectResolver.InstanceIdOf(so),
                    name          = so.name,
                    hide_flags    = so.hideFlags.ToString(),
                    field_count   = fields.Count,
                    fields        = fields.ToArray(),
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        private static object ReadValue(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:   return p.intValue;
                case SerializedPropertyType.Boolean:   return p.boolValue;
                case SerializedPropertyType.Float:     return p.floatValue;
                case SerializedPropertyType.String:    return p.stringValue;
                case SerializedPropertyType.Color:     return new { r=p.colorValue.r, g=p.colorValue.g, b=p.colorValue.b, a=p.colorValue.a };
                case SerializedPropertyType.Vector2:   return new { x=p.vector2Value.x, y=p.vector2Value.y };
                case SerializedPropertyType.Vector3:   return new { x=p.vector3Value.x, y=p.vector3Value.y, z=p.vector3Value.z };
                case SerializedPropertyType.Enum:      return p.enumValueIndex;
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue == null ? null : new {
                        type_fqn    = p.objectReferenceValue.GetType().FullName,
                        name        = p.objectReferenceValue.name,
                        instance_id = GameObjectResolver.InstanceIdOf(p.objectReferenceValue)
                    };
                default: return $"<{p.propertyType}>";
            }
        }
    }
}
