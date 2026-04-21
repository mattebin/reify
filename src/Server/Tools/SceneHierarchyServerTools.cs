using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class SceneHierarchyServerTools
{
    [McpServerTool(Name = "scene-hierarchy"), Description(
        "Return a depth-first flattened tree of scene GameObjects as " +
        "structured JSON with pagination. Each node: instance_id, name, " +
        "path, active_self, active_in_hierarchy, layer + layer_name, tag, " +
        "child_count, component_types[] (FQN array). " +
        "\n\nDefaults: scene_path=null (all loaded scenes), cursor=0, " +
        "page_size=500 (max 5000), include_components=true. Response carries " +
        "total_nodes and next_cursor — null when exhausted.")]
    public static async Task<JsonElement> SceneHierarchy(UnityClient unity,
        string? scene_path, int? cursor, int? page_size, bool? include_components, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("scene-hierarchy",
        new SceneHierarchyArgs(scene_path, cursor, page_size, include_components), ct);

    [McpServerTool(Name = "scene-query"), Description(
        "Grep-like structured query over the scene hierarchy. Combine any of: " +
        "component_type (short or FQN), name_pattern (regex), tag, layer, " +
        "active. All conditions are AND. Returns matching nodes (same shape " +
        "as scene-hierarchy) plus scanned count, match_count, truncated flag. " +
        "Philosophy flagship — this is grep-for-Unity.")]
    public static async Task<JsonElement> SceneQuery(UnityClient unity,
        string? scene_path, string? component_type, string? name_pattern,
        string? tag, int? layer, bool? active, int? limit, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("scene-query",
        new SceneQueryArgs(scene_path, component_type, name_pattern, tag, layer, active, limit), ct);

    [McpServerTool(Name = "scene-stats"), Description(
        "One-pass aggregate of a scene: GO count, active/inactive split, root " +
        "count, camera/light/renderer totals, counts per component type, " +
        "counts per tag — plus philosophy warnings for common misconfigurations " +
        "(multiple MainCameras, zero lights, multiple directional lights, " +
        "heavy inactive population).")]
    public static async Task<JsonElement> SceneStats(UnityClient unity,
        string? scene_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("scene-stats", new SceneStatsArgs(scene_path), ct);
}
