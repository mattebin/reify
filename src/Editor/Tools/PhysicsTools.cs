using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Read-only physics query domain. All six tools wrap Unity's Physics
    /// static methods and return structured-state JSON. Works in edit and
    /// play mode — in edit mode the physics world reflects scene colliders
    /// without simulation step.
    /// </summary>
    internal static class PhysicsTools
    {
        // ---------- physics-raycast ----------
        public static Task<object> Raycast(JToken args)
        {
            var (origin, dir) = ReadRay(args);
            var max        = args?.Value<float?>("max_distance") ?? Mathf.Infinity;
            var maskTok    = args?["layer_mask"];
            var qti        = ReadQti(args?.Value<string>("query_trigger_interaction"));

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mask = ReadLayerMask(maskTok);
                var hit = new RaycastHit();
                var ok  = Physics.Raycast(origin, dir, out hit, max, mask, qti);
                return new
                {
                    hit          = ok,
                    hit_info     = ok ? HitDto(hit) : null,
                    query = new { origin = V(origin), direction = V(dir.normalized), max_distance = max, layer_mask = mask, query_trigger_interaction = qti.ToString() },
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- physics-raycast-all ----------
        public static Task<object> RaycastAll(JToken args)
        {
            var (origin, dir) = ReadRay(args);
            var max        = args?.Value<float?>("max_distance") ?? Mathf.Infinity;
            var maskTok    = args?["layer_mask"];
            var qti        = ReadQti(args?.Value<string>("query_trigger_interaction"));

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mask = ReadLayerMask(maskTok);
                var hits = Physics.RaycastAll(origin, dir, max, mask, qti);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                var dtos = new object[hits.Length];
                for (var i = 0; i < hits.Length; i++) dtos[i] = HitDto(hits[i]);
                return new
                {
                    hit_count    = hits.Length,
                    hits         = dtos,
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- physics-spherecast ----------
        public static Task<object> SphereCast(JToken args)
        {
            var (origin, dir) = ReadRay(args);
            var radius     = args?.Value<float?>("radius")       ?? throw new ArgumentException("radius is required.");
            var max        = args?.Value<float?>("max_distance") ?? Mathf.Infinity;
            var maskTok    = args?["layer_mask"];
            var qti        = ReadQti(args?.Value<string>("query_trigger_interaction"));

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mask = ReadLayerMask(maskTok);
                var hit = new RaycastHit();
                var ok  = Physics.SphereCast(origin, radius, dir, out hit, max, mask, qti);
                return new
                {
                    hit         = ok,
                    hit_info    = ok ? HitDto(hit) : null,
                    query = new { origin = V(origin), direction = V(dir.normalized), radius, max_distance = max, layer_mask = mask },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- physics-overlap-sphere ----------
        public static Task<object> OverlapSphere(JToken args)
        {
            var pos    = ReadVec3Required(args?["position"], "position");
            var radius = args?.Value<float?>("radius") ?? throw new ArgumentException("radius is required.");
            var maskTok    = args?["layer_mask"];
            var qti    = ReadQti(args?.Value<string>("query_trigger_interaction"));

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mask = ReadLayerMask(maskTok);
                var cols = Physics.OverlapSphere(pos, radius, mask, qti);
                var dtos = new object[cols.Length];
                for (var i = 0; i < cols.Length; i++) dtos[i] = ColliderDto(cols[i]);
                return new
                {
                    count       = cols.Length,
                    colliders   = dtos,
                    query = new { position = V(pos), radius, layer_mask = mask },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- physics-overlap-box ----------
        public static Task<object> OverlapBox(JToken args)
        {
            var center       = ReadVec3Required(args?["center"],       "center");
            var halfExtents  = ReadVec3Required(args?["half_extents"], "half_extents");
            var rot          = ReadQuat(args?["orientation"]) ?? Quaternion.identity;
            var maskTok      = args?["layer_mask"];
            var qti          = ReadQti(args?.Value<string>("query_trigger_interaction"));

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var mask = ReadLayerMask(maskTok);
                var cols = Physics.OverlapBox(center, halfExtents, rot, mask, qti);
                var dtos = new object[cols.Length];
                for (var i = 0; i < cols.Length; i++) dtos[i] = ColliderDto(cols[i]);
                return new
                {
                    count       = cols.Length,
                    colliders   = dtos,
                    query = new { center = V(center), half_extents = V(halfExtents), orientation = Q(rot), layer_mask = mask },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- physics-settings ----------
        public static Task<object> Settings(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var warnings = new List<string>();

                var gravity = Physics.gravity;
                if (Mathf.Abs(gravity.magnitude - 9.81f) > 0.01f)
                    warnings.Add($"Gravity magnitude is {gravity.magnitude:F3} (expected ~9.81). Non-Earth gravity — verify this is intentional.");
                if (Physics.defaultSolverIterations < 4)
                    warnings.Add($"Default solver iterations = {Physics.defaultSolverIterations}. < 4 often produces jittery/interpenetrating contacts.");
                if (Physics.defaultSolverIterations > 20)
                    warnings.Add($"Default solver iterations = {Physics.defaultSolverIterations}. > 20 is expensive; consider reducing unless physical instability demands it.");

                // Non-default layer-collision matrix entries (ignored pairs).
                var ignoredPairs = new List<object>();
                for (var a = 0; a < 32; a++)
                {
                    var nameA = LayerMask.LayerToName(a);
                    if (string.IsNullOrEmpty(nameA)) continue;
                    for (var b = a; b < 32; b++)
                    {
                        var nameB = LayerMask.LayerToName(b);
                        if (string.IsNullOrEmpty(nameB)) continue;
                        if (Physics.GetIgnoreLayerCollision(a, b))
                            ignoredPairs.Add(new { a, a_name = nameA, b, b_name = nameB });
                    }
                }

                return new
                {
                    gravity                    = V(gravity),
                    bounce_threshold           = Physics.bounceThreshold,
                    sleep_threshold            = Physics.sleepThreshold,
                    default_contact_offset     = Physics.defaultContactOffset,
                    default_solver_iterations  = Physics.defaultSolverIterations,
                    default_solver_velocity_iterations = Physics.defaultSolverVelocityIterations,
                    default_max_angular_speed  = Physics.defaultMaxAngularSpeed,
                    queries_hit_backfaces      = Physics.queriesHitBackfaces,
                    queries_hit_triggers       = Physics.queriesHitTriggers,
                    auto_simulation            = Physics.simulationMode.ToString(),
                    ignored_layer_pairs        = ignoredPairs.ToArray(),
                    ignored_layer_pair_count   = ignoredPairs.Count,
                    warnings                   = warnings.ToArray(),
                    read_at_utc                = DateTime.UtcNow.ToString("o"),
                    frame                      = (long)Time.frameCount
                };
            });
        }

        // ---------- arg helpers ----------

        private static (Vector3 origin, Vector3 direction) ReadRay(JToken args)
        {
            var origin = ReadVec3Required(args?["origin"],    "origin");
            var dir    = ReadVec3Required(args?["direction"], "direction");
            if (dir.sqrMagnitude < 0.0001f) throw new ArgumentException("direction magnitude is ~0.");
            return (origin, dir);
        }

        private static Vector3 ReadVec3Required(JToken t, string fieldName)
        {
            if (t == null || t.Type == JTokenType.Null)
                throw new ArgumentException($"{fieldName} is required.");
            return new Vector3(
                t.Value<float?>("x") ?? 0,
                t.Value<float?>("y") ?? 0,
                t.Value<float?>("z") ?? 0);
        }

        private static Quaternion? ReadQuat(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            return new Quaternion(
                t.Value<float?>("x") ?? 0,
                t.Value<float?>("y") ?? 0,
                t.Value<float?>("z") ?? 0,
                t.Value<float?>("w") ?? 1);
        }

        private static int ReadLayerMask(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return Physics.DefaultRaycastLayers;
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

        private static QueryTriggerInteraction ReadQti(string s)
        {
            if (string.IsNullOrEmpty(s)) return QueryTriggerInteraction.UseGlobal;
            if (Enum.TryParse<QueryTriggerInteraction>(s, ignoreCase: true, out var v)) return v;
            throw new ArgumentException($"query_trigger_interaction must be UseGlobal, Ignore, or Collide. Got: {s}");
        }

        private static object HitDto(RaycastHit h) => new
        {
            collider_instance_id  = h.collider != null ? GameObjectResolver.InstanceIdOf(h.collider) : 0,
            collider_type         = h.collider != null ? h.collider.GetType().FullName : null,
            gameobject_instance_id = h.collider != null ? GameObjectResolver.InstanceIdOf(h.collider.gameObject) : 0,
            gameobject_path        = h.collider != null ? GameObjectResolver.PathOf(h.collider.gameObject) : null,
            point       = V(h.point),
            normal      = V(h.normal),
            distance    = h.distance,
            is_trigger  = h.collider != null && h.collider.isTrigger
        };

        private static object ColliderDto(Collider c) => new
        {
            instance_id            = GameObjectResolver.InstanceIdOf(c),
            collider_type          = c.GetType().FullName,
            gameobject_instance_id = GameObjectResolver.InstanceIdOf(c.gameObject),
            gameobject_path        = GameObjectResolver.PathOf(c.gameObject),
            bounds_center          = V(c.bounds.center),
            bounds_size            = V(c.bounds.size),
            is_trigger             = c.isTrigger,
            enabled                = c.enabled
        };

        private static object V(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
        private static object Q(Quaternion q) => new { x = q.x, y = q.y, z = q.z, w = q.w };
    }
}
