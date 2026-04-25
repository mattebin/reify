using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class SceneDiffServerTools
{
    [McpServerTool(Name = "scene-snapshot"), Description(
        "Capture a compact, stable inventory of every GameObject in the " +
        "active scene (or all loaded scenes when all_loaded_scenes=true). " +
        "Each entry: instance_id, scene_path (SceneName::Path), name, " +
        "active_self/in_hierarchy, layer, tag, static_flags, " +
        "component_count, component_types[], transform (world " +
        "pos/euler/lossy scale). " +
        "\n\n" +
        "Use before destructive ops; pass the result to scene-diff after " +
        "the op to prove exactly what changed. include_components and " +
        "include_transform default true — flip off for lighter snapshots.")]
    public static async Task<JsonElement> SceneSnapshot(
        UnityClient unity,
        bool? all_loaded_scenes = null,
        bool? include_components = null,
        bool? include_transform = null,
        string? component_encoding = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("scene-snapshot", new
    {
        all_loaded_scenes, include_components, include_transform, component_encoding
    }, ct);

    [McpServerTool(Name = "scene-diff"), Description(
        "Compare a prior scene-snapshot against the current scene state " +
        "and return a structural diff: " +
        "\n" +
        "  - added[]: GOs present now, absent in snapshot. " +
        "\n" +
        "  - removed[]: GOs in snapshot, absent now. " +
        "\n" +
        "  - changed[]: GOs in both, with per-field before/after for " +
        "name/active_self/layer/tag/static_flags/component_count, " +
        "components_added/removed, and transform deltas. " +
        "\n\n" +
        "before_snapshot must be the raw object returned by scene-snapshot. " +
        "Nothing in CoplayDev/IvanMurzak exposes this — use it to prove " +
        "the exact effect of a write without a screenshot.")]
    public static async Task<JsonElement> SceneDiff(
        UnityClient unity,
        JsonElement before_snapshot,
        bool? all_loaded_scenes = null,
        bool? include_components = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("scene-diff", new
    {
        before_snapshot, all_loaded_scenes, include_components
    }, ct);
}
