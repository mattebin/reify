using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    internal static class GameObjectCreateTool
    {
        [ReifyTool("gameobject-create")]
        public static Task<object> Handle(JToken args)
        {
            var name        = args?.Value<string>("name")            ?? "GameObject";
            var primitive   = args?.Value<string>("primitive");          // "Cube"|"Sphere"|...|null=empty
            var parentPath  = args?.Value<string>("parent_path");
            var position    = ReadVec3(args?["position"])            ?? Vector3.zero;
            var rotation    = ReadVec3(args?["rotation_euler"])      ?? Vector3.zero;
            var scale       = ReadVec3(args?["scale"])               ?? Vector3.one;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                GameObject go;
                if (string.IsNullOrEmpty(primitive))
                {
                    go = new GameObject(name);
                }
                else
                {
                    if (!Enum.TryParse<PrimitiveType>(primitive, ignoreCase: true, out var pt))
                        throw new ArgumentException(
                            $"Unknown primitive '{primitive}'. Valid: " +
                            "Cube, Sphere, Capsule, Cylinder, Plane, Quad.");
                    go = GameObject.CreatePrimitive(pt);
                    go.name = name;
                }

                Undo.RegisterCreatedObjectUndo(go, $"Reify: create GameObject '{name}'");

                if (!string.IsNullOrEmpty(parentPath))
                {
                    var parent = GameObjectResolver.ByPath(parentPath)
                        ?? throw new InvalidOperationException($"Parent not found: {parentPath}");
                    Undo.SetTransformParent(go.transform, parent.transform, "Reify: reparent new GameObject");
                }

                go.transform.localPosition   = position;
                go.transform.localEulerAngles = rotation;
                go.transform.localScale      = scale;

                return GameObjectDto.Wrap(GameObjectDto.Build(go, includeComponents: true));
            });
        }

        private static Vector3? ReadVec3(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            return new Vector3(
                token.Value<float?>("x") ?? 0f,
                token.Value<float?>("y") ?? 0f,
                token.Value<float?>("z") ?? 0f);
        }
    }
}
