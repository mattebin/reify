using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record SceneHierarchyArgs(
    [property: JsonPropertyName("scene_path")]         string? ScenePath,
    [property: JsonPropertyName("cursor")]             int? Cursor,
    [property: JsonPropertyName("page_size")]          int? PageSize,
    [property: JsonPropertyName("include_components")] bool? IncludeComponents);

public sealed record SceneQueryArgs(
    [property: JsonPropertyName("scene_path")]     string? ScenePath,
    [property: JsonPropertyName("component_type")] string? ComponentType,
    [property: JsonPropertyName("name_pattern")]   string? NamePattern,
    [property: JsonPropertyName("tag")]            string? Tag,
    [property: JsonPropertyName("layer")]          int? Layer,
    [property: JsonPropertyName("active")]         bool? Active,
    [property: JsonPropertyName("limit")]          int? Limit);

public sealed record SceneStatsArgs(
    [property: JsonPropertyName("scene_path")] string? ScenePath);
