using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Applies a JSON value to a SerializedProperty. Centralises the
    /// type-coercion switch so every tool that writes component state
    /// (component-modify, component-set-property, future asset setters)
    /// speaks the same input shapes.
    /// </summary>
    internal static class SerializedPropertyWriter
    {
        public static void Apply(SerializedProperty p, JToken value)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (value == null || value.Type == JTokenType.Null)
            {
                if (p.propertyType == SerializedPropertyType.ObjectReference)
                    p.objectReferenceValue = null;
                return;
            }

            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:   p.intValue    = value.Value<int>();    break;
                case SerializedPropertyType.Boolean:   p.boolValue   = value.Value<bool>();   break;
                case SerializedPropertyType.Float:     p.floatValue  = value.Value<float>();  break;
                case SerializedPropertyType.String:    p.stringValue = value.Value<string>(); break;
                case SerializedPropertyType.LayerMask: p.intValue    = value.Value<int>();    break;
                case SerializedPropertyType.Enum:      p.enumValueIndex = value.Value<int>(); break;

                case SerializedPropertyType.Color:
                    p.colorValue = new Color(
                        value.Value<float?>("r") ?? 0,
                        value.Value<float?>("g") ?? 0,
                        value.Value<float?>("b") ?? 0,
                        value.Value<float?>("a") ?? 1);
                    break;

                case SerializedPropertyType.Vector2:
                    p.vector2Value = new Vector2(
                        value.Value<float?>("x") ?? 0,
                        value.Value<float?>("y") ?? 0);
                    break;

                case SerializedPropertyType.Vector3:
                    p.vector3Value = new Vector3(
                        value.Value<float?>("x") ?? 0,
                        value.Value<float?>("y") ?? 0,
                        value.Value<float?>("z") ?? 0);
                    break;

                case SerializedPropertyType.Vector4:
                    p.vector4Value = new Vector4(
                        value.Value<float?>("x") ?? 0,
                        value.Value<float?>("y") ?? 0,
                        value.Value<float?>("z") ?? 0,
                        value.Value<float?>("w") ?? 0);
                    break;

                case SerializedPropertyType.Quaternion:
                    p.quaternionValue = new Quaternion(
                        value.Value<float?>("x") ?? 0,
                        value.Value<float?>("y") ?? 0,
                        value.Value<float?>("z") ?? 0,
                        value.Value<float?>("w") ?? 1);
                    break;

                case SerializedPropertyType.ObjectReference:
                {
                    // Accept { "instance_id": N } or { "asset_path": "..." }.
                    var iid = value["instance_id"]?.Value<int?>();
                    if (iid.HasValue)
                    {
                        var resolved = GameObjectResolver.ByInstanceId(iid.Value);
                        if (resolved == null)
                            throw new InvalidOperationException(
                                $"No Unity object with instance_id {iid.Value} for ObjectReference property '{p.propertyPath}'.");
                        p.objectReferenceValue = resolved;
                        break;
                    }
                    var path = value["asset_path"]?.Value<string>();
                    if (!string.IsNullOrEmpty(path))
                    {
                        var resolved = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                        if (resolved == null)
                            throw new InvalidOperationException(
                                $"No asset found at path '{path}' for ObjectReference property '{p.propertyPath}'.");
                        p.objectReferenceValue = resolved;
                        break;
                    }
                    throw new ArgumentException(
                        $"ObjectReference property '{p.propertyPath}' needs either " +
                        "{instance_id:N} or {asset_path:\"...\"}.");
                }

                default:
                    throw new NotSupportedException(
                        $"Cannot write property '{p.propertyPath}' of type {p.propertyType} " +
                        "— not yet implemented in SerializedPropertyWriter.");
            }
        }
    }
}
