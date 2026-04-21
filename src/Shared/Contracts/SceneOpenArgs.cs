using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record SceneOpenArgs(
    [property: JsonPropertyName("path")]      string Path,
    [property: JsonPropertyName("additive")]  bool? Additive
);

public sealed record SceneSaveArgs(
    [property: JsonPropertyName("path")]          string? Path,
    [property: JsonPropertyName("save_as_copy")]  bool? SaveAsCopy
);

public sealed record SceneCreateArgs(
    [property: JsonPropertyName("path")]           string Path,
    [property: JsonPropertyName("setup_default")]  bool? SetupDefault
);

public sealed record SceneMutationResponse(
    [property: JsonPropertyName("scene")]       SceneInfo Scene,
    [property: JsonPropertyName("read_at_utc")] string ReadAtUtc,
    [property: JsonPropertyName("frame")]       long Frame
);
