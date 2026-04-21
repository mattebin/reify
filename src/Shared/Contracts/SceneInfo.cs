using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record SceneInfo(
    [property: JsonPropertyName("name")]              string Name,
    [property: JsonPropertyName("path")]              string Path,
    [property: JsonPropertyName("build_index")]       int BuildIndex,
    [property: JsonPropertyName("is_loaded")]         bool IsLoaded,
    [property: JsonPropertyName("is_dirty")]          bool IsDirty,
    [property: JsonPropertyName("is_active")]         bool IsActive,
    [property: JsonPropertyName("root_count")]        int RootCount,
    [property: JsonPropertyName("root_gameobjects")]  string[] RootGameObjects
);
