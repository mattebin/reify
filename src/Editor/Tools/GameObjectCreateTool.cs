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

                // When a primitive was created, surface the mesh's intrinsic
                // dimensions. Otherwise the caller has to Google "what are
                // the default dimensions of Unity's Sphere primitive" — the
                // answer is on reify's disk.
                var meshBounds = ReadMeshBounds(go);
                var primitiveDims = PrimitiveDefaults.For(primitive);

                var dto = GameObjectDto.Build(go, includeComponents: true);
                return new
                {
                    gameobject          = dto,
                    primitive           = primitive,
                    primitive_defaults  = primitiveDims,   // intrinsic, unscaled
                    mesh_bounds         = meshBounds,       // from Renderer, in local space
                    world_height        = meshBounds != null ? meshBounds.size_world_y : (float?)null,
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        private sealed class MeshBoundsDto
        {
            public object local_center;
            public object local_size;
            public object local_extents;
            public object world_center;
            public object world_size;
            public float  size_world_y;
        }

        private static MeshBoundsDto ReadMeshBounds(GameObject go)
        {
            if (!go.TryGetComponent<Renderer>(out var r)) return null;
            var lb = r.localBounds;      // mesh-local bounds, before world transform
            var wb = r.bounds;           // world-space AABB after scale/rotation
            return new MeshBoundsDto
            {
                local_center  = new { x = lb.center.x,  y = lb.center.y,  z = lb.center.z  },
                local_size    = new { x = lb.size.x,    y = lb.size.y,    z = lb.size.z    },
                local_extents = new { x = lb.extents.x, y = lb.extents.y, z = lb.extents.z },
                world_center  = new { x = wb.center.x,  y = wb.center.y,  z = wb.center.z  },
                world_size    = new { x = wb.size.x,    y = wb.size.y,    z = wb.size.z    },
                size_world_y  = wb.size.y
            };
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
