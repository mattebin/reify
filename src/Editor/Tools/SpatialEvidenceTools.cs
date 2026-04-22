using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Geometry helpers that make "prove the spatial claim" easier than
    /// guessing. These are intentionally small and composable so agents can
    /// combine them with primitive-defaults, gameobject-find, and batch-execute.
    /// </summary>
    internal static class SpatialEvidenceTools
    {
        [ReifyTool("spatial-primitive-evidence")]
        public static Task<object> SpatialPrimitiveEvidence(JToken args)
        {
            var gameObjectPath = args?.Value<string>("gameobject_path")
                                 ?? throw new ArgumentException("gameobject_path is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var go = GameObjectResolver.ByPath(gameObjectPath)
                    ?? throw new InvalidOperationException($"GameObject not found: {gameObjectPath}");

                var evidence = BuildPrimitiveEvidence(go);
                return new
                {
                    primitive = evidence,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("spatial-anchor-distance")]
        public static Task<object> SpatialAnchorDistance(JToken args)
        {
            var aPath = args?.Value<string>("a_path")
                        ?? throw new ArgumentException("a_path is required.");
            var bPath = args?.Value<string>("b_path")
                        ?? throw new ArgumentException("b_path is required.");
            var aAnchor = args?.Value<string>("a_anchor") ?? "center";
            var bAnchor = args?.Value<string>("b_anchor") ?? "center";
            var tolerance = args?.Value<float?>("tolerance") ?? 0.025f;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var aGo = GameObjectResolver.ByPath(aPath)
                    ?? throw new InvalidOperationException($"GameObject not found: {aPath}");
                var bGo = GameObjectResolver.ByPath(bPath)
                    ?? throw new InvalidOperationException($"GameObject not found: {bPath}");

                var aEvidence = BuildPrimitiveEvidence(aGo);
                var bEvidence = BuildPrimitiveEvidence(bGo);
                var aPoint = ResolveAnchorPoint(aGo, aAnchor);
                var bPoint = ResolveAnchorPoint(bGo, bAnchor);

                var delta = bPoint - aPoint;
                var distance = delta.magnitude;

                return new
                {
                    a = new
                    {
                        instance_id = GameObjectResolver.InstanceIdOf(aGo),
                        path = GameObjectResolver.PathOf(aGo),
                        qualified_path = GameObjectResolver.QualifiedPathOf(aGo),
                        anchor = aAnchor.ToLowerInvariant(),
                        point = ToVec3(aPoint)
                    },
                    b = new
                    {
                        instance_id = GameObjectResolver.InstanceIdOf(bGo),
                        path = GameObjectResolver.PathOf(bGo),
                        qualified_path = GameObjectResolver.QualifiedPathOf(bGo),
                        anchor = bAnchor.ToLowerInvariant(),
                        point = ToVec3(bPoint)
                    },
                    delta = ToVec3(delta),
                    axis_gap_meters = new
                    {
                        x = Mathf.Abs(delta.x),
                        y = Mathf.Abs(delta.y),
                        z = Mathf.Abs(delta.z)
                    },
                    distance_meters = distance,
                    tolerance_meters = tolerance,
                    within_tolerance = distance <= tolerance,
                    a_primitive = new
                    {
                        primitive_kind = aEvidence.primitive_kind,
                        world_size = aEvidence.world_size
                    },
                    b_primitive = new
                    {
                        primitive_kind = bEvidence.primitive_kind,
                        world_size = bEvidence.world_size
                    },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)Time.frameCount
                };
            });
        }

        private static PrimitiveEvidence BuildPrimitiveEvidence(GameObject go)
        {
            var renderer = go.GetComponent<Renderer>();
            var meshFilter = go.GetComponent<MeshFilter>();
            var skinned = go.GetComponent<SkinnedMeshRenderer>();

            Bounds localBounds;
            string meshName = null;
            string primitiveKind = null;

            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                localBounds = meshFilter.sharedMesh.bounds;
                meshName = meshFilter.sharedMesh.name;
                primitiveKind = GuessPrimitiveKind(meshFilter.sharedMesh.name);
            }
            else if (skinned != null && skinned.sharedMesh != null)
            {
                localBounds = skinned.sharedMesh.bounds;
                meshName = skinned.sharedMesh.name;
                primitiveKind = GuessPrimitiveKind(skinned.sharedMesh.name);
            }
            else if (renderer != null)
            {
                localBounds = renderer.localBounds;
                primitiveKind = "renderer_only";
            }
            else
            {
                throw new InvalidOperationException(
                    $"GameObject '{GameObjectResolver.QualifiedPathOf(go)}' has no Renderer or Mesh to inspect spatially.");
            }

            var t = go.transform;
            var worldBounds = renderer != null ? renderer.bounds : TransformBounds(localBounds, t);

            return new PrimitiveEvidence
            {
                primitive_kind = primitiveKind ?? "mesh",
                mesh_name = meshName,
                instance_id = GameObjectResolver.InstanceIdOf(go),
                path = GameObjectResolver.PathOf(go),
                qualified_path = GameObjectResolver.QualifiedPathOf(go),
                local_bounds = ToBounds(localBounds),
                world_bounds = ToBounds(worldBounds),
                world_size = ToVec3(worldBounds.size),
                width_world = worldBounds.size.x,
                height_world = worldBounds.size.y,
                depth_world = worldBounds.size.z,
                transform = new
                {
                    world_position = ToVec3(t.position),
                    lossy_scale = ToVec3(t.lossyScale),
                    right = ToVec3(t.right),
                    up = ToVec3(t.up),
                    forward = ToVec3(t.forward)
                },
                anchors_world = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["center"] = ToVec3(ResolveAnchorPoint(go, "center")),
                    ["top"] = ToVec3(ResolveAnchorPoint(go, "top")),
                    ["bottom"] = ToVec3(ResolveAnchorPoint(go, "bottom")),
                    ["left"] = ToVec3(ResolveAnchorPoint(go, "left")),
                    ["right"] = ToVec3(ResolveAnchorPoint(go, "right")),
                    ["front"] = ToVec3(ResolveAnchorPoint(go, "front")),
                    ["back"] = ToVec3(ResolveAnchorPoint(go, "back")),
                    ["top_left"] = ToVec3(ResolveAnchorPoint(go, "top_left")),
                    ["top_right"] = ToVec3(ResolveAnchorPoint(go, "top_right")),
                    ["bottom_left"] = ToVec3(ResolveAnchorPoint(go, "bottom_left")),
                    ["bottom_right"] = ToVec3(ResolveAnchorPoint(go, "bottom_right"))
                }
            };
        }

        private static Vector3 ResolveAnchorPoint(GameObject go, string anchorName)
        {
            var localBounds = ReadLocalBounds(go);
            var anchor = (anchorName ?? "center")
                .Trim()
                .ToLowerInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");
            var tokens = anchor.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);

            var local = localBounds.center;

            var xSet = false;
            var ySet = false;
            var zSet = false;

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case "center":
                    case "mid":
                        break;
                    case "left":
                        local.x = localBounds.min.x;
                        xSet = true;
                        break;
                    case "right":
                        local.x = localBounds.max.x;
                        xSet = true;
                        break;
                    case "top":
                    case "up":
                        local.y = localBounds.max.y;
                        ySet = true;
                        break;
                    case "bottom":
                    case "down":
                        local.y = localBounds.min.y;
                        ySet = true;
                        break;
                    case "front":
                        local.z = localBounds.max.z;
                        zSet = true;
                        break;
                    case "back":
                        local.z = localBounds.min.z;
                        zSet = true;
                        break;
                    default:
                        throw new ArgumentException(
                            $"Unknown anchor '{anchorName}'. Valid tokens include center, top, bottom, left, right, front, back, and combinations like top_left.");
                }
            }

            if (!xSet) local.x = localBounds.center.x;
            if (!ySet) local.y = localBounds.center.y;
            if (!zSet) local.z = localBounds.center.z;

            return go.transform.TransformPoint(local);
        }

        private static Bounds ReadLocalBounds(GameObject go)
        {
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
                return meshFilter.sharedMesh.bounds;

            var skinned = go.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null && skinned.sharedMesh != null)
                return skinned.sharedMesh.bounds;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                return renderer.localBounds;

            throw new InvalidOperationException(
                $"GameObject '{GameObjectResolver.QualifiedPathOf(go)}' has no local bounds source.");
        }

        private static Bounds TransformBounds(Bounds localBounds, Transform t)
        {
            var center = t.TransformPoint(localBounds.center);
            var extents = localBounds.extents;
            var worldExtents = new Vector3(
                Mathf.Abs(t.TransformVector(new Vector3(extents.x, 0f, 0f)).x) +
                Mathf.Abs(t.TransformVector(new Vector3(0f, extents.y, 0f)).x) +
                Mathf.Abs(t.TransformVector(new Vector3(0f, 0f, extents.z)).x),
                Mathf.Abs(t.TransformVector(new Vector3(extents.x, 0f, 0f)).y) +
                Mathf.Abs(t.TransformVector(new Vector3(0f, extents.y, 0f)).y) +
                Mathf.Abs(t.TransformVector(new Vector3(0f, 0f, extents.z)).y),
                Mathf.Abs(t.TransformVector(new Vector3(extents.x, 0f, 0f)).z) +
                Mathf.Abs(t.TransformVector(new Vector3(0f, extents.y, 0f)).z) +
                Mathf.Abs(t.TransformVector(new Vector3(0f, 0f, extents.z)).z));
            return new Bounds(center, worldExtents * 2f);
        }

        private static string GuessPrimitiveKind(string meshName)
        {
            if (string.IsNullOrWhiteSpace(meshName)) return null;
            var n = meshName.Trim();
            if (n.Equals("Cube", StringComparison.OrdinalIgnoreCase)) return "Cube";
            if (n.Equals("Sphere", StringComparison.OrdinalIgnoreCase)) return "Sphere";
            if (n.Equals("Capsule", StringComparison.OrdinalIgnoreCase)) return "Capsule";
            if (n.Equals("Cylinder", StringComparison.OrdinalIgnoreCase)) return "Cylinder";
            if (n.Equals("Plane", StringComparison.OrdinalIgnoreCase)) return "Plane";
            if (n.Equals("Quad", StringComparison.OrdinalIgnoreCase)) return "Quad";
            return "mesh";
        }

        private static object ToBounds(Bounds b) => new
        {
            center = ToVec3(b.center),
            size = ToVec3(b.size),
            extents = ToVec3(b.extents),
            min = ToVec3(b.min),
            max = ToVec3(b.max)
        };

        private static object ToVec3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };

        private sealed class PrimitiveEvidence
        {
            public string primitive_kind;
            public string mesh_name;
            public int instance_id;
            public string path;
            public string qualified_path;
            public object local_bounds;
            public object world_bounds;
            public object world_size;
            public float width_world;
            public float height_world;
            public float depth_world;
            public object transform;
            public Dictionary<string, object> anchors_world;
        }
    }
}
