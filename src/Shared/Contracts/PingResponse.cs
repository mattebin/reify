using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record PingResponse(
    [property: JsonPropertyName("status")]        string Status,
    [property: JsonPropertyName("unity_version")] string UnityVersion,
    [property: JsonPropertyName("project_name")]  string ProjectName,
    [property: JsonPropertyName("project_path")]  string ProjectPath,
    [property: JsonPropertyName("platform")]      string Platform,
    [property: JsonPropertyName("is_play_mode")]  bool IsPlayMode,
    [property: JsonPropertyName("is_compiling")]  bool IsCompiling,
    [property: JsonPropertyName("frame")]         long Frame,
    [property: JsonPropertyName("read_at_utc")]   string ReadAtUtc
);
