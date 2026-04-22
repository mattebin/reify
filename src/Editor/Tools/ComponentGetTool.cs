using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class ComponentGetTool
    {
        [ReifyTool("component-get")]
        public static Task<object> Handle(JToken args)
        {
            var path       = ComponentLookup.ReadGameObjectPathArg(args);
            var instanceId = ComponentLookup.ReadInstanceIdArg(args);
            var includeProperties = args?.Value<bool?>("include_properties") ?? false;

            if (string.IsNullOrEmpty(path) && !instanceId.HasValue)
                throw new ArgumentException(
                    "Provide either 'gameobject_path' (alias: path) or 'instance_id' " +
                    "(GameObject or Component).");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                // Two modes:
                //   path or gameobject instance_id > list components on that GameObject.
                //   component instance_id           > return that one component's properties.
                if (instanceId.HasValue)
                {
                    var obj = GameObjectResolver.ByInstanceId(instanceId.Value);
                    if (obj is Component c)
                        return new
                        {
                            component   = DescribeComponent(c, includeProperties: true),
                            read_at_utc = DateTime.UtcNow.ToString("o"),
                            frame       = (long)Time.frameCount
                        };
                    if (obj is GameObject g)
                        return BuildForGameObject(g, includeProperties);
                    throw new InvalidOperationException(
                        $"instance_id {instanceId} resolves to {(obj != null ? obj.GetType().Name : "null")}, " +
                        "expected GameObject or Component.");
                }

                var go = GameObjectResolver.ByPath(path)
                    ?? throw new InvalidOperationException($"GameObject not found: {path}");
                return BuildForGameObject(go, includeProperties);
            });
        }

        private static object BuildForGameObject(GameObject go, bool includeProperties)
        {
            var comps = go.GetComponents<Component>();
            var list  = new List<object>(comps.Length);
            foreach (var c in comps)
            {
                if (c == null) { list.Add(new { type_fqn = "<missing-script>" }); continue; }
                list.Add(DescribeComponent(c, includeProperties));
            }
            return new
            {
                gameobject = new
                {
                    instance_id = GameObjectResolver.InstanceIdOf(go),
                    name        = go.name,
                    path        = GameObjectResolver.PathOf(go),
                    qualified_path = GameObjectResolver.QualifiedPathOf(go),
                    scene_name  = go.scene.name,
                    scene_path  = go.scene.path
                },
                component_count = comps.Length,
                components      = list.ToArray(),
                read_at_utc     = DateTime.UtcNow.ToString("o"),
                frame           = (long)Time.frameCount
            };
        }

        private static object DescribeComponent(Component c, bool includeProperties)
        {
            var baseDesc = new Dictionary<string, object>
            {
                ["type_fqn"]    = c.GetType().FullName,
                ["instance_id"] = GameObjectResolver.InstanceIdOf(c),
                ["enabled"]     = c is Behaviour b ? b.enabled : (object)null
            };

            if (!includeProperties) return baseDesc;

            var props = new List<object>();
            try
            {
                using var so = new SerializedObject(c);
                var it = so.GetIterator();
                // Skip the default m_Script property from the first enter.
                if (it.NextVisible(true))
                {
                    do
                    {
                        props.Add(new
                        {
                            name     = it.name,
                            type     = it.propertyType.ToString(),
                            value    = ReadSerializedValue(it)
                        });
                    } while (it.NextVisible(false));
                }
            }
            catch (Exception ex)
            {
                baseDesc["property_read_error"] = ex.Message;
            }

            baseDesc["properties"] = props.ToArray();
            return baseDesc;
        }

        private static object ReadSerializedValue(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:    return p.intValue;
                case SerializedPropertyType.Boolean:    return p.boolValue;
                case SerializedPropertyType.Float:      return p.floatValue;
                case SerializedPropertyType.String:     return p.stringValue;
                case SerializedPropertyType.Color:      return new { r=p.colorValue.r, g=p.colorValue.g, b=p.colorValue.b, a=p.colorValue.a };
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue == null ? null : new
                    {
                        type_fqn    = p.objectReferenceValue.GetType().FullName,
                        instance_id = GameObjectResolver.InstanceIdOf(p.objectReferenceValue),
                        name        = p.objectReferenceValue.name
                    };
                case SerializedPropertyType.LayerMask:  return p.intValue;
                case SerializedPropertyType.Enum:       return p.enumValueIndex;
                case SerializedPropertyType.Vector2:    return new { x=p.vector2Value.x, y=p.vector2Value.y };
                case SerializedPropertyType.Vector3:    return new { x=p.vector3Value.x, y=p.vector3Value.y, z=p.vector3Value.z };
                case SerializedPropertyType.Vector4:    return new { x=p.vector4Value.x, y=p.vector4Value.y, z=p.vector4Value.z, w=p.vector4Value.w };
                case SerializedPropertyType.Quaternion: return new { x=p.quaternionValue.x, y=p.quaternionValue.y, z=p.quaternionValue.z, w=p.quaternionValue.w };
                case SerializedPropertyType.Bounds:
                    return new
                    {
                        center = new { x=p.boundsValue.center.x, y=p.boundsValue.center.y, z=p.boundsValue.center.z },
                        size   = new { x=p.boundsValue.size.x,   y=p.boundsValue.size.y,   z=p.boundsValue.size.z }
                    };
                default:
                    // Complex types (arrays, generic structs) — summarise.
                    return $"<{p.propertyType}>";
            }
        }
    }
}
