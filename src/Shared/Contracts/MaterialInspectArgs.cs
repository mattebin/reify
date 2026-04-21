using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record MaterialInspectArgs(
    [property: JsonPropertyName("asset_path")]            string? AssetPath,
    [property: JsonPropertyName("renderer_instance_id")]  int?    RendererInstanceId,
    [property: JsonPropertyName("gameobject_path")]       string? GameObjectPath,
    [property: JsonPropertyName("submesh_index")]         int?    SubmeshIndex
);
