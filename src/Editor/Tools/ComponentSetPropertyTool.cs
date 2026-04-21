using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Single-property setter. Unlike component-modify (batch), this tool
    /// targets one SerializedProperty path and is intended for writes that
    /// need a per-property return shape — e.g. verifying the post-write
    /// value via read-back per ADR-001 §5.
    ///
    /// Supports nested paths Unity's SerializedObject already understands:
    /// "m_LocalPosition.x", "m_Materials.Array.size", etc.
    /// </summary>
    internal static class ComponentSetPropertyTool
    {
        public static Task<object> Handle(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath       = args?.Value<string>("gameobject_path");
            var compType     = args?.Value<string>("component_type");
            var propertyPath = args?.Value<string>("property_path")
                ?? throw new ArgumentException("property_path is required.");
            var value        = args?["value"]
                ?? throw new ArgumentException("value is required (may be null for ObjectReference).");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var component = ComponentLookup.Resolve(instanceId, goPath, compType);
                Undo.RecordObject(component, $"Reify: set {propertyPath} on {component.GetType().Name}");

                using var so = new SerializedObject(component);
                var p = so.FindProperty(propertyPath)
                    ?? throw new InvalidOperationException(
                        $"Property '{propertyPath}' not found on {component.GetType().FullName}.");

                var before = ReadValue(p);
                SerializedPropertyWriter.Apply(p, value);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(component);

                // Re-read through a fresh SerializedObject to verify.
                using var soAfter = new SerializedObject(component);
                var pAfter = soAfter.FindProperty(propertyPath);
                var after = pAfter != null ? ReadValue(pAfter) : null;

                return new
                {
                    component = new
                    {
                        type_fqn        = component.GetType().FullName,
                        instance_id     = GameObjectResolver.InstanceIdOf(component),
                        gameobject_path = GameObjectResolver.PathOf(component.gameObject)
                    },
                    property_path = propertyPath,
                    type          = p.propertyType.ToString(),
                    before,
                    after,
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        private static object ReadValue(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:    return p.intValue;
                case SerializedPropertyType.Boolean:    return p.boolValue;
                case SerializedPropertyType.Float:      return p.floatValue;
                case SerializedPropertyType.String:     return p.stringValue;
                case SerializedPropertyType.LayerMask:  return p.intValue;
                case SerializedPropertyType.Enum:       return p.enumValueIndex;
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
                default: return $"<{p.propertyType}>";
            }
        }
    }
}
