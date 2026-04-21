using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Navigation / NavMesh domain. Covers NavMeshAgent runtime state +
    /// static NavMesh query helpers (sample, raycast, find edge, calculate
    /// path). Bake is deferred to a future batch — it needs the AI
    /// Navigation package surface (NavMeshSurface) and is stateful enough
    /// to deserve its own design pass.
    /// </summary>
    internal static class NavigationTools
    {
        // ---------- nav-agent-inspect ----------
        [ReifyTool("nav-agent-inspect")]
        public static Task<object> AgentInspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var a = ResolveAgent(args);
                var warnings = new List<string>();

                if (!a.enabled)
                    warnings.Add("NavMeshAgent is disabled — Move/SetDestination no-ops until re-enabled.");
                if (!a.isOnNavMesh)
                    warnings.Add("Agent is not on a NavMesh — path queries will fail. Check NavMesh bake coverage and agent.Warp to a known nav surface.");
                if (a.isPathStale)
                    warnings.Add("Agent path is stale — the NavMesh changed under it. Will re-path on next frame.");
                if (a.hasPath && a.pathStatus == NavMeshPathStatus.PathPartial)
                    warnings.Add("Agent path is PARTIAL — destination is unreachable; agent will stop at the nearest reachable point.");
                if (a.hasPath && a.pathStatus == NavMeshPathStatus.PathInvalid)
                    warnings.Add("Agent path is INVALID — no path exists.");
                if (a.speed <= 0f)
                    warnings.Add($"Agent speed is {a.speed:F3} — won't move even with a valid path.");

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(a),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(a.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(a.gameObject),
                    enabled                = a.enabled,
                    is_on_nav_mesh         = a.isOnNavMesh,
                    is_stopped             = a.isStopped,
                    is_path_stale          = a.isPathStale,
                    has_path               = a.hasPath,
                    path_pending           = a.pathPending,
                    path_status            = a.pathStatus.ToString(),
                    position               = V(a.transform.position),
                    velocity               = V(a.velocity),
                    desired_velocity       = V(a.desiredVelocity),
                    destination            = a.hasPath ? (object)V(a.destination) : null,
                    next_position          = V(a.nextPosition),
                    remaining_distance     = a.remainingDistance,
                    stopping_distance      = a.stoppingDistance,
                    speed                  = a.speed,
                    angular_speed          = a.angularSpeed,
                    acceleration           = a.acceleration,
                    radius                 = a.radius,
                    height                 = a.height,
                    base_offset            = a.baseOffset,
                    auto_braking           = a.autoBraking,
                    auto_repath            = a.autoRepath,
                    auto_traverse_off_mesh_link = a.autoTraverseOffMeshLink,
                    agent_type_id          = a.agentTypeID,
                    area_mask              = a.areaMask,
                    obstacle_avoidance_type = a.obstacleAvoidanceType.ToString(),
                    avoidance_priority     = a.avoidancePriority,
                    updatePosition         = a.updatePosition,
                    updateRotation         = a.updateRotation,
                    updateUpAxis           = a.updateUpAxis,
                    corners                = CornerArray(a),
                    warnings               = warnings.ToArray(),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- nav-agent-set-destination ----------
        [ReifyTool("nav-agent-set-destination")]
        public static Task<object> AgentSetDestination(JToken args)
        {
            var dest = ReadVec3Required(args?["destination"], "destination");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var a = ResolveAgent(args);
                if (!a.isOnNavMesh)
                    throw new InvalidOperationException(
                        "Agent is not on a NavMesh — use nav-agent-warp first to place it on a baked surface.");

                var before = new { destination = a.hasPath ? (object)V(a.destination) : null, has_path = a.hasPath };
                var ok = a.SetDestination(dest);
                if (!ok)
                    throw new InvalidOperationException(
                        $"SetDestination({V(dest)}) returned false — destination likely unreachable or off the NavMesh.");

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(a),
                    gameobject_path = GameObjectResolver.PathOf(a.gameObject),
                    requested       = V(dest),
                    before,
                    after           = new {
                        destination  = V(a.destination),
                        has_path     = a.hasPath,
                        path_pending = a.pathPending,
                        path_status  = a.pathStatus.ToString()
                    },
                    note            = "Path computation is async — pathPending may be true immediately after SetDestination.",
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- nav-agent-warp ----------
        [ReifyTool("nav-agent-warp")]
        public static Task<object> AgentWarp(JToken args)
        {
            var pos = ReadVec3Required(args?["position"], "position");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var a = ResolveAgent(args);
                var before = new { position = V(a.transform.position), is_on_nav_mesh = a.isOnNavMesh };
                var ok = a.Warp(pos);
                if (!ok)
                    throw new InvalidOperationException(
                        $"Warp({V(pos)}) returned false — position may not be on a NavMesh. Use nav-sample-position first to find a valid target.");

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(a),
                    gameobject_path = GameObjectResolver.PathOf(a.gameObject),
                    requested       = V(pos),
                    before,
                    after           = new { position = V(a.transform.position), is_on_nav_mesh = a.isOnNavMesh },
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- nav-agent-stop ----------
        [ReifyTool("nav-agent-stop")]
        public static Task<object> AgentStop(JToken args)
        {
            var clearPath = args?.Value<bool?>("clear_path") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var a = ResolveAgent(args);
                var before = new { is_stopped = a.isStopped, has_path = a.hasPath };
                a.isStopped = true;
                if (clearPath && a.hasPath) a.ResetPath();

                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(a),
                    gameobject_path = GameObjectResolver.PathOf(a.gameObject),
                    cleared_path    = clearPath,
                    before,
                    after           = new { is_stopped = a.isStopped, has_path = a.hasPath },
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- nav-agent-resume ----------
        [ReifyTool("nav-agent-resume")]
        public static Task<object> AgentResume(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var a = ResolveAgent(args);
                var before = new { is_stopped = a.isStopped };
                a.isStopped = false;
                return new
                {
                    instance_id     = GameObjectResolver.InstanceIdOf(a),
                    gameobject_path = GameObjectResolver.PathOf(a.gameObject),
                    before,
                    after           = new { is_stopped = a.isStopped },
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- nav-sample-position ----------
        [ReifyTool("nav-sample-position")]
        public static Task<object> SamplePosition(JToken args)
        {
            var pos         = ReadVec3Required(args?["position"], "position");
            var maxDistance = args?.Value<float?>("max_distance") ?? 5f;
            var areaMask    = args?.Value<int?>("area_mask") ?? NavMesh.AllAreas;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                NavMeshHit hit;
                var ok = NavMesh.SamplePosition(pos, out hit, maxDistance, areaMask);
                return new
                {
                    found         = ok,
                    query         = new { position = V(pos), max_distance = maxDistance, area_mask = areaMask },
                    hit           = ok ? (object)new {
                        position     = V(hit.position),
                        normal       = V(hit.normal),
                        distance     = hit.distance,
                        mask         = hit.mask,
                        hit_flag     = hit.hit
                    } : null,
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        // ---------- nav-raycast ----------
        [ReifyTool("nav-raycast")]
        public static Task<object> Raycast(JToken args)
        {
            var source   = ReadVec3Required(args?["source"],   "source");
            var target   = ReadVec3Required(args?["target"],   "target");
            var areaMask = args?.Value<int?>("area_mask") ?? NavMesh.AllAreas;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                NavMeshHit hit;
                var blocked = NavMesh.Raycast(source, target, out hit, areaMask);
                return new
                {
                    // NavMesh.Raycast returns TRUE if the line is BLOCKED —
                    // flip to a `clear_path` boolean for sanity.
                    blocked       = blocked,
                    clear_path    = !blocked,
                    query         = new { source = V(source), target = V(target), area_mask = areaMask },
                    hit           = new {
                        position = V(hit.position),
                        normal   = V(hit.normal),
                        distance = hit.distance,
                        mask     = hit.mask,
                        hit_flag = hit.hit
                    },
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        // ---------- nav-find-closest-edge ----------
        [ReifyTool("nav-find-closest-edge")]
        public static Task<object> FindClosestEdge(JToken args)
        {
            var pos      = ReadVec3Required(args?["position"], "position");
            var areaMask = args?.Value<int?>("area_mask") ?? NavMesh.AllAreas;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                NavMeshHit hit;
                var ok = NavMesh.FindClosestEdge(pos, out hit, areaMask);
                return new
                {
                    found         = ok,
                    query         = new { position = V(pos), area_mask = areaMask },
                    edge          = ok ? (object)new {
                        position = V(hit.position),
                        normal   = V(hit.normal),
                        distance = hit.distance,
                        mask     = hit.mask,
                        hit_flag = hit.hit
                    } : null,
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        // ---------- nav-calculate-path ----------
        [ReifyTool("nav-calculate-path")]
        public static Task<object> CalculatePath(JToken args)
        {
            var source   = ReadVec3Required(args?["source"], "source");
            var target   = ReadVec3Required(args?["target"], "target");
            var areaMask = args?.Value<int?>("area_mask") ?? NavMesh.AllAreas;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var path = new NavMeshPath();
                var ok = NavMesh.CalculatePath(source, target, areaMask, path);

                var corners = path.corners ?? Array.Empty<Vector3>();
                var cornerDtos = new object[corners.Length];
                var totalLength = 0f;
                for (var i = 0; i < corners.Length; i++)
                {
                    cornerDtos[i] = V(corners[i]);
                    if (i > 0) totalLength += Vector3.Distance(corners[i - 1], corners[i]);
                }

                var warnings = new List<string>();
                if (!ok)
                    warnings.Add("CalculatePath returned false — one or both endpoints may be off the NavMesh.");
                if (path.status == NavMeshPathStatus.PathPartial)
                    warnings.Add("Path is PARTIAL — target unreachable; corners terminate at nearest reachable point.");
                if (path.status == NavMeshPathStatus.PathInvalid)
                    warnings.Add("Path is INVALID — no path exists between source and target.");

                return new
                {
                    computed      = ok,
                    status        = path.status.ToString(),
                    corner_count  = corners.Length,
                    corners       = cornerDtos,
                    total_length  = totalLength,
                    query         = new { source = V(source), target = V(target), area_mask = areaMask },
                    warnings      = warnings.ToArray(),
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static NavMeshAgent ResolveAgent(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                return obj as NavMeshAgent
                    ?? (obj as GameObject)?.GetComponent<NavMeshAgent>()
                    ?? throw new InvalidOperationException(
                        $"instance_id {instanceId} does not resolve to a NavMeshAgent or a GameObject with one.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            var go = GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
            return go.GetComponent<NavMeshAgent>()
                ?? throw new InvalidOperationException($"GameObject '{goPath}' has no NavMeshAgent component.");
        }

        private static Vector3 ReadVec3Required(JToken t, string field)
        {
            if (t == null || t.Type == JTokenType.Null)
                throw new ArgumentException($"{field} is required.");
            return new Vector3(
                t.Value<float?>("x") ?? 0,
                t.Value<float?>("y") ?? 0,
                t.Value<float?>("z") ?? 0);
        }

        private static object V(Vector3 v) => new { x = v.x, y = v.y, z = v.z };

        private static object[] CornerArray(NavMeshAgent a)
        {
            if (!a.hasPath || a.path == null) return Array.Empty<object>();
            var corners = a.path.corners ?? Array.Empty<Vector3>();
            var r = new object[corners.Length];
            for (var i = 0; i < corners.Length; i++) r[i] = V(corners[i]);
            return r;
        }
    }
}
