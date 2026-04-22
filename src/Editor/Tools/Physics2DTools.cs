using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// 2D physics query domain. Mirrors the 3D <see cref="PhysicsTools"/>
    /// surface against <see cref="Physics2D"/>. Read-only queries plus a
    /// settings snapshot. Writes deferred — mutations through
    /// <c>component-set-property</c> already cover Rigidbody2D / Collider2D
    /// tuning.
    /// </summary>
    internal static class Physics2DTools
    {
        // ---------- physics2d-raycast ----------
        [ReifyTool("physics2d-raycast")]
        public static Task<object> Raycast(JToken args)
        {
            var origin    = ReadVec2Required(args?["origin"], "origin");
            var direction = ReadVec2Required(args?["direction"], "direction");
            if (direction.sqrMagnitude < 0.0001f)
                throw new ArgumentException("direction magnitude is ~0.");
            var max       = args?.Value<float?>("max_distance") ?? Mathf.Infinity;
            var maskTok   = args?["layer_mask"];
            var minDepth  = args?.Value<float?>("min_depth") ?? -Mathf.Infinity;
            var maxDepth  = args?.Value<float?>("max_depth") ?? Mathf.Infinity;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mask = ReadLayerMask(maskTok);
                var hit = Physics2D.Raycast(origin, direction, max, mask, minDepth, maxDepth);
                return new
                {
                    hit         = hit.collider != null,
                    hit_info    = hit.collider != null ? Hit2DDto(hit) : null,
                    query       = new {
                        origin     = V2(origin),
                        direction  = V2(direction.normalized),
                        max_distance = max,
                        layer_mask = mask,
                        min_depth  = minDepth,
                        max_depth  = maxDepth
                    },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- physics2d-raycast-all ----------
        [ReifyTool("physics2d-raycast-all")]
        public static Task<object> RaycastAll(JToken args)
        {
            var origin    = ReadVec2Required(args?["origin"], "origin");
            var direction = ReadVec2Required(args?["direction"], "direction");
            if (direction.sqrMagnitude < 0.0001f)
                throw new ArgumentException("direction magnitude is ~0.");
            var max       = args?.Value<float?>("max_distance") ?? Mathf.Infinity;
            var maskTok   = args?["layer_mask"];

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mask = ReadLayerMask(maskTok);
                var hits = Physics2D.RaycastAll(origin, direction, max, mask);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                var dtos = new object[hits.Length];
                for (var i = 0; i < hits.Length; i++) dtos[i] = Hit2DDto(hits[i]);
                return new
                {
                    hit_count   = hits.Length,
                    hits        = dtos,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- physics2d-overlap-circle ----------
        [ReifyTool("physics2d-overlap-circle")]
        public static Task<object> OverlapCircle(JToken args)
        {
            var pos     = ReadVec2Required(args?["position"], "position");
            var radius  = args?.Value<float?>("radius") ?? throw new ArgumentException("radius is required.");
            var maskTok = args?["layer_mask"];

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mask = ReadLayerMask(maskTok);
                var cols = Physics2D.OverlapCircleAll(pos, radius, mask);
                var dtos = new object[cols.Length];
                for (var i = 0; i < cols.Length; i++) dtos[i] = Collider2DDto(cols[i]);
                return new
                {
                    count       = cols.Length,
                    colliders   = dtos,
                    query       = new { position = V2(pos), radius, layer_mask = mask },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- physics2d-overlap-box ----------
        [ReifyTool("physics2d-overlap-box")]
        public static Task<object> OverlapBox(JToken args)
        {
            var center = ReadVec2Required(args?["center"], "center");
            var size   = ReadVec2Required(args?["size"],   "size");
            var angle  = args?.Value<float?>("angle") ?? 0f;
            var maskTok = args?["layer_mask"];

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mask = ReadLayerMask(maskTok);
                var cols = Physics2D.OverlapBoxAll(center, size, angle, mask);
                var dtos = new object[cols.Length];
                for (var i = 0; i < cols.Length; i++) dtos[i] = Collider2DDto(cols[i]);
                return new
                {
                    count       = cols.Length,
                    colliders   = dtos,
                    query       = new { center = V2(center), size = V2(size), angle, layer_mask = mask },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- physics2d-settings ----------
        [ReifyTool("physics2d-settings")]
        public static Task<object> Settings(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var warnings = new List<string>();
                var g = Physics2D.gravity;
                if (Mathf.Abs(g.y - (-9.81f)) > 0.01f && Mathf.Abs(g.magnitude) < 0.01f)
                    warnings.Add($"gravity is {V2(g)} — check intent (default is (0, -9.81)).");

                var ignoredPairs = new List<object>();
                for (var a = 0; a < 32; a++)
                {
                    var nameA = LayerMask.LayerToName(a);
                    if (string.IsNullOrEmpty(nameA)) continue;
                    for (var b = a; b < 32; b++)
                    {
                        var nameB = LayerMask.LayerToName(b);
                        if (string.IsNullOrEmpty(nameB)) continue;
                        if (Physics2D.GetIgnoreLayerCollision(a, b))
                            ignoredPairs.Add(new { a, a_name = nameA, b, b_name = nameB });
                    }
                }

                return new
                {
                    gravity                       = V2(g),
                    default_contact_offset        = Physics2D.defaultContactOffset,
                    velocity_iterations           = Physics2D.velocityIterations,
                    position_iterations           = Physics2D.positionIterations,
                    queries_start_in_colliders    = Physics2D.queriesStartInColliders,
                    queries_hit_triggers          = Physics2D.queriesHitTriggers,
                    callbacks_on_disable          = Physics2D.callbacksOnDisable,
                    // Physics2D.autoSyncTransforms is obsolete in newer Unity; omitted.
                    simulation_mode               = Physics2D.simulationMode.ToString(),
                    job_options = new
                    {
                        use_multithreading   = Physics2D.jobOptions.useMultithreading,
                        use_consistency_sort = Physics2D.jobOptions.useConsistencySorting
                    },
                    ignored_layer_pairs      = ignoredPairs.ToArray(),
                    ignored_layer_pair_count = ignoredPairs.Count,
                    warnings    = warnings.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static Vector2 ReadVec2Required(JToken t, string field)
        {
            if (t == null || t.Type == JTokenType.Null)
                throw new ArgumentException($"{field} is required ({{x, y}}).");
            return new Vector2(
                t.Value<float?>("x") ?? 0,
                t.Value<float?>("y") ?? 0);
        }

        private static int ReadLayerMask(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return Physics2D.DefaultRaycastLayers;
            if (t.Type == JTokenType.Integer) return t.Value<int>();
            if (t is JArray arr)
            {
                var mask = 0;
                foreach (var name in arr)
                {
                    var idx = LayerMask.NameToLayer(name.Value<string>());
                    if (idx < 0) throw new ArgumentException($"Unknown layer name: '{name}'.");
                    mask |= (1 << idx);
                }
                return mask;
            }
            throw new ArgumentException("layer_mask must be an int bitmask or a string[] of layer names.");
        }

        private static object Hit2DDto(RaycastHit2D h) => new
        {
            collider_instance_id   = h.collider != null ? GameObjectResolver.InstanceIdOf(h.collider) : 0,
            collider_type          = h.collider != null ? h.collider.GetType().FullName : null,
            gameobject_instance_id = h.collider != null ? GameObjectResolver.InstanceIdOf(h.collider.gameObject) : 0,
            gameobject_path        = h.collider != null ? GameObjectResolver.PathOf(h.collider.gameObject) : null,
            point       = V2(h.point),
            normal      = V2(h.normal),
            distance    = h.distance,
            fraction    = h.fraction,
            is_trigger  = h.collider != null && h.collider.isTrigger
        };

        private static object Collider2DDto(Collider2D c) => new
        {
            instance_id            = GameObjectResolver.InstanceIdOf(c),
            collider_type          = c.GetType().FullName,
            gameobject_instance_id = GameObjectResolver.InstanceIdOf(c.gameObject),
            gameobject_path        = GameObjectResolver.PathOf(c.gameObject),
            bounds_center          = V2(c.bounds.center),
            bounds_size            = V2(c.bounds.size),
            is_trigger             = c.isTrigger,
            enabled                = c.enabled
        };

        private static object V2(Vector2 v) => new { x = v.x, y = v.y };
        private static object V2(Vector3 v) => new { x = v.x, y = v.y };
    }
}
