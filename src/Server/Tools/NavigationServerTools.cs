using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class NavigationServerTools
{
    [McpServerTool(Name = "nav-agent-inspect"), Description(
        "Read full NavMeshAgent state: enabled, is_on_nav_mesh, is_stopped, " +
        "has_path, path_status (Complete/Partial/Invalid), position, velocity, " +
        "destination, remaining_distance, agent settings (speed, radius, etc.), " +
        "path corners. Warnings for: disabled, off-nav-mesh, stale path, " +
        "partial/invalid path, zero speed. Resolve by instance_id or " +
        "gameobject_path.")]
    public static async Task<JsonElement> NavAgentInspect(UnityClient unity,
        int? instance_id, string? gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("nav-agent-inspect",
        new NavAgentRefArgs(instance_id, gameobject_path), ct);

    [McpServerTool(Name = "nav-agent-set-destination"), Description(
        "Set a NavMeshAgent's destination. Fails structurally if the agent " +
        "is not on a NavMesh or if SetDestination refuses. Returns " +
        "{before, after} including path_pending and path_status. Path " +
        "computation is async — path_pending may be true immediately after.")]
    public static async Task<JsonElement> NavAgentSetDestination(UnityClient unity,
        int? instance_id, string? gameobject_path, Vec3Arg destination, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("nav-agent-set-destination",
        new NavAgentSetDestinationArgs(instance_id, gameobject_path, destination), ct);

    [McpServerTool(Name = "nav-agent-warp"), Description(
        "Teleport a NavMeshAgent to a position. Fails if the target isn't on " +
        "a NavMesh — use nav-sample-position first to find a valid point. " +
        "Returns {before, after} with is_on_nav_mesh for read-back.")]
    public static async Task<JsonElement> NavAgentWarp(UnityClient unity,
        int? instance_id, string? gameobject_path, Vec3Arg position, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("nav-agent-warp",
        new NavAgentWarpArgs(instance_id, gameobject_path, position), ct);

    [McpServerTool(Name = "nav-agent-stop"), Description(
        "Pause agent movement (sets isStopped=true). Set clear_path=true to " +
        "also ResetPath. Returns {before, after}.")]
    public static async Task<JsonElement> NavAgentStop(UnityClient unity,
        int? instance_id, string? gameobject_path, bool? clear_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("nav-agent-stop",
        new NavAgentStopArgs(instance_id, gameobject_path, clear_path), ct);

    [McpServerTool(Name = "nav-agent-resume"), Description(
        "Resume agent movement (sets isStopped=false). Returns {before, after}.")]
    public static async Task<JsonElement> NavAgentResume(UnityClient unity,
        int? instance_id, string? gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("nav-agent-resume",
        new NavAgentRefArgs(instance_id, gameobject_path), ct);

    [McpServerTool(Name = "nav-sample-position"), Description(
        "Find the closest NavMesh point to a world position within " +
        "max_distance (default 5m). Returns {found, hit: {position, normal, " +
        "distance, mask}}. Use before nav-agent-warp to find a valid target.")]
    public static async Task<JsonElement> NavSamplePosition(UnityClient unity,
        Vec3Arg position, float? max_distance, int? area_mask, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("nav-sample-position",
        new NavPositionQueryArgs(position, max_distance, area_mask), ct);

    [McpServerTool(Name = "nav-raycast"), Description(
        "Cast a line on the NavMesh from source to target. Returns both " +
        "`blocked` (Unity's native return — TRUE when the line hits an edge) " +
        "and `clear_path` (the inverse, easier to reason about). Includes " +
        "hit details when blocked.")]
    public static async Task<JsonElement> NavRaycast(UnityClient unity,
        Vec3Arg source, Vec3Arg target, int? area_mask, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("nav-raycast",
        new NavRaycastArgs(source, target, area_mask), ct);

    [McpServerTool(Name = "nav-find-closest-edge"), Description(
        "Find the closest NavMesh edge to a world position. Returns {found, " +
        "edge: {position, normal, distance, mask}}. Useful for spawning " +
        "objects at boundaries or cover.")]
    public static async Task<JsonElement> NavFindClosestEdge(UnityClient unity,
        Vec3Arg position, int? area_mask, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("nav-find-closest-edge",
        new NavFindEdgeArgs(position, area_mask), ct);

    [McpServerTool(Name = "nav-calculate-path"), Description(
        "Compute a NavMesh path between two points without assigning it to an " +
        "agent. Returns {computed, status (Complete/Partial/Invalid), corners, " +
        "corner_count, total_length}. Warnings for partial/invalid paths. " +
        "Use this to check reachability before committing an agent move.")]
    public static async Task<JsonElement> NavCalculatePath(UnityClient unity,
        Vec3Arg source, Vec3Arg target, int? area_mask, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("nav-calculate-path",
        new NavCalculatePathArgs(source, target, area_mask), ct);
}
