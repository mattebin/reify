using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class AssetDependentsServerTool
{
    [McpServerTool(Name = "asset-dependents"), Description(
        "7th Phase C philosophy tool. Reverse-dependency lookup — 'what " +
        "breaks if I delete this?' Returns every asset and scene whose " +
        "forward-dependency set contains the target. Direct dependents by " +
        "default; set max_depth up to 4 for transitive. " +
        "\n\n" +
        "Separate arrays for dependents (non-scene assets) and " +
        "scene_references so the caller can answer blast-radius questions " +
        "structurally. Warnings count prefabs and scene references as the " +
        "two highest-impact categories. " +
        "\n\n" +
        "First call builds the full reverse index — O(total assets), slow on " +
        "large projects. Cache invalidates automatically when the " +
        "AssetDatabase changes."
    )]
    public static async Task<JsonElement> AssetDependents(UnityClient unity,
        [Description("Asset path whose dependents to find.")]
        string asset_path,
        [Description("Whether to include scenes that reference the asset. Default true.")]
        bool? include_scene_references,
        [Description("Transitive depth (1–4). Default 1 (direct dependents only).")]
        int? max_depth,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asset-dependents",
        new AssetDependentsArgs(asset_path, include_scene_references, max_depth), ct);
}
