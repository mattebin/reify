using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Creates an axis-aligned line primitive (Capsule or Cylinder or Cube)
    /// stretched and rotated to span a world-space segment from A to B
    /// with a given thickness. Eliminates the rotation-math trap that
    /// every hand-built "capsule along a diagonal" hits: the caller
    /// specifies endpoints, reify handles the quaternion.
    ///
    /// Receipt includes ADR-002 applied_fields, the computed midpoint /
    /// length / rotation, and post-write endpoint proofs so the caller
    /// can verify the placement matches intent without a second tool call.
    /// </summary>
    internal static class GeometryLinePrimitiveTool
    {
        [ReifyTool("geometry-line-primitive")]
        public static Task<object> Create(JToken args)
        {
            var name = args?.Value<string>("name") ?? "LineSegment";
            var from = ReadVec3(args?["from"], "from");
            var to   = ReadVec3(args?["to"],   "to");
            var thickness = args?.Value<float?>("thickness") ?? 0.05f;
            if (thickness <= 0f)
                throw new ArgumentException("thickness must be > 0.");
            var primitiveName = args?.Value<string>("primitive") ?? "Capsule";
            if (!Enum.TryParse<PrimitiveType>(primitiveName, ignoreCase: true, out var primitive))
                throw new ArgumentException(
                    $"primitive '{primitiveName}' is not valid. Use Capsule, Cylinder, or Cube.");
            if (primitive != PrimitiveType.Capsule &&
                primitive != PrimitiveType.Cylinder &&
                primitive != PrimitiveType.Cube)
                throw new ArgumentException(
                    "geometry-line-primitive only supports Capsule, Cylinder, or Cube " +
                    "— other primitives don't have a sensible 'length axis'.");

            var parentPath = args?.Value<string>("parent_path");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var delta = to - from;
                var length = delta.magnitude;
                if (length < 1e-5f)
                    throw new ArgumentException(
                        $"from and to are too close (|to-from|={length:E3}m). " +
                        "Cannot orient a line primitive along a zero vector.");

                var midpoint = (from + to) * 0.5f;
                var direction = delta / length;

                // Capsule and Cylinder have their length axis on local +Y with intrinsic
                // height 2m. Cube uses local +Y with intrinsic height 1m.
                var rotation = Quaternion.FromToRotation(Vector3.up, direction);

                // Scale the length axis so world length matches |to-from|.
                // Capsule/Cylinder: intrinsic 2m on Y, so scale.y = length / 2.
                // Cube: intrinsic 1m on Y, so scale.y = length.
                float yScale;
                if (primitive == PrimitiveType.Cube) yScale = length;
                else                                 yScale = length * 0.5f;

                var go = GameObject.CreatePrimitive(primitive);
                go.name = name;
                Undo.RegisterCreatedObjectUndo(go, $"Reify: create line primitive '{name}'");

                go.transform.position   = midpoint;
                go.transform.rotation   = rotation;
                go.transform.localScale = new Vector3(thickness, yScale, thickness);

                if (!string.IsNullOrEmpty(parentPath))
                {
                    var parent = GameObjectResolver.ByPath(parentPath)
                        ?? throw new InvalidOperationException($"parent_path not found: {parentPath}");
                    // Preserve world transform across reparent.
                    Undo.SetTransformParent(go.transform, parent.transform, "Reify: reparent line primitive");
                }

                EditorUtility.SetDirty(go);

                // Read-back proof: the capsule's local +Y tip (top) and -Y tip (bottom)
                // should land exactly on `to` and `from` respectively. We compute them
                // from the actual transform instead of trusting our own math.
                var measuredTop    = go.transform.TransformPoint(new Vector3(0,  primitive == PrimitiveType.Cube ? 0.5f : 1f, 0));
                var measuredBottom = go.transform.TransformPoint(new Vector3(0, -(primitive == PrimitiveType.Cube ? 0.5f : 1f), 0));
                var gapToTop    = (measuredTop - to).magnitude;
                var gapToBottom = (measuredBottom - from).magnitude;

                return new
                {
                    gameobject = new
                    {
                        instance_id    = GameObjectResolver.InstanceIdOf(go),
                        name           = go.name,
                        path           = GameObjectResolver.PathOf(go),
                        qualified_path = GameObjectResolver.QualifiedPathOf(go)
                    },
                    primitive      = primitive.ToString(),
                    thickness      = thickness,
                    computed = new
                    {
                        length_meters  = length,
                        midpoint       = V(midpoint),
                        rotation_euler = V(rotation.eulerAngles),
                        direction      = V(direction)
                    },
                    // ADR-002 receipt
                    applied_fields = new object[]
                    {
                        new { field = "primitive_created", before = (object)null,
                              after = new { primitive = primitive.ToString(), name = go.name,
                                            instance_id = GameObjectResolver.InstanceIdOf(go) } },
                        new { field = "line_length_meters", before = 0f, after = length }
                    },
                    applied_count = 2,
                    // Endpoint proofs — caller can skip a follow-up anchor-distance call.
                    endpoint_proofs = new
                    {
                        measured_top_world    = V(measuredTop),
                        expected_top_world    = V(to),
                        gap_top_meters        = gapToTop,
                        measured_bottom_world = V(measuredBottom),
                        expected_bottom_world = V(from),
                        gap_bottom_meters     = gapToBottom,
                        both_within_1mm       = gapToTop <= 0.001f && gapToBottom <= 0.001f
                    },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        private static Vector3 ReadVec3(JToken token, string name)
        {
            if (token == null || token.Type == JTokenType.Null)
                throw new ArgumentException($"{name} is required ({{x,y,z}}).");
            return new Vector3(
                token.Value<float?>("x") ?? 0f,
                token.Value<float?>("y") ?? 0f,
                token.Value<float?>("z") ?? 0f);
        }

        private static object V(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    }
}
