using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class ConstraintLodServerTools
{
    [McpServerTool(Name = "constraint-inspect"), Description(
        "Inspect any IConstraint-derived component (Position/Rotation/Scale/" +
        "Parent/Aim/LookAt). Returns common fields (constraint_active, locked, " +
        "weight, source_count, sources with per-source weight + transform " +
        "path) plus a `specific` block with type-specific data (axis flags, " +
        "offsets, aim vectors, look-at roll). Pass constraint_type to " +
        "disambiguate when a GameObject carries multiple constraints.")]
    public static async Task<JsonElement> ConstraintInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        string? constraint_type = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("constraint-inspect", new
    {
        instance_id, gameobject_path, constraint_type
    }, ct);

    [McpServerTool(Name = "constraint-source-add"), Description(
        "Add a source Transform to a constraint. source_path is the scene " +
        "path of the source GameObject. weight defaults to 1. Returns the " +
        "new source index + new source_count. Undo-backed.")]
    public static async Task<JsonElement> ConstraintSourceAdd(
        UnityClient unity,
        string source_path,
        int? instance_id = null,
        string? gameobject_path = null,
        string? constraint_type = null,
        float? weight = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("constraint-source-add", new
    {
        instance_id, gameobject_path, constraint_type, source_path, weight
    }, ct);

    [McpServerTool(Name = "constraint-source-remove"), Description(
        "Remove a constraint source by index. Returns the removed source's " +
        "transform path. Undo-backed.")]
    public static async Task<JsonElement> ConstraintSourceRemove(
        UnityClient unity,
        int source_index,
        int? instance_id = null,
        string? gameobject_path = null,
        string? constraint_type = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("constraint-source-remove", new
    {
        instance_id, gameobject_path, constraint_type, source_index
    }, ct);

    [McpServerTool(Name = "lod-group-inspect"), Description(
        "Inspect an LODGroup: lod_count, size, fade_mode, each LOD with " +
        "screen_relative_height + fade_transition_width + renderer list " +
        "(type + instance_id + gameobject_path). Warnings for zero LODs " +
        "or empty renderer arrays.")]
    public static async Task<JsonElement> LodGroupInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("lod-group-inspect", new
    {
        instance_id, gameobject_path
    }, ct);

    [McpServerTool(Name = "lod-group-force"), Description(
        "Force an LODGroup to a specific LOD index (useful for testing / " +
        "thumbnailing). Pass lod_index=-1 (or omit) to unforce. Note: " +
        "ForceLOD state is not persisted across domain reloads.")]
    public static async Task<JsonElement> LodGroupForce(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        int? lod_index = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("lod-group-force", new
    {
        instance_id, gameobject_path, lod_index
    }, ct);
}
