using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record AssetDependentsArgs(
    [property: JsonPropertyName("asset_path")]               string AssetPath,
    [property: JsonPropertyName("include_scene_references")] bool? IncludeSceneReferences,
    [property: JsonPropertyName("max_depth")]                int? MaxDepth
);
