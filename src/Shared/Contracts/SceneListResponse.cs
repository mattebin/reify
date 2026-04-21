using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record SceneListResponse(
    [property: JsonPropertyName("open_scene_count")] int OpenSceneCount,
    [property: JsonPropertyName("scenes")]           SceneInfo[] Scenes,
    [property: JsonPropertyName("read_at_utc")]      string ReadAtUtc,
    [property: JsonPropertyName("frame")]            long Frame
);
