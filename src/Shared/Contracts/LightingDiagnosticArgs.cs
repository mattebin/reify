using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record LightingDiagnosticArgs(
    [property: JsonPropertyName("scene_path")] string? ScenePath
);
