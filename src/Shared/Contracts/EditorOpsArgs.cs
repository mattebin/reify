using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record EditorMenuExecuteArgs(
    [property: JsonPropertyName("path")] string Path);

public sealed record EditorSelectionSetArgs(
    [property: JsonPropertyName("instance_ids")] int[]? InstanceIds,
    [property: JsonPropertyName("paths")]        string[]? Paths);
