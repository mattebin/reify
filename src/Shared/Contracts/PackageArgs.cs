using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record PackageSearchArgs(
    [property: JsonPropertyName("query")]           string Query,
    [property: JsonPropertyName("include_preview")] bool? IncludePreview,
    [property: JsonPropertyName("offline_mode")]    bool? OfflineMode);

public sealed record PackageAddArgs(
    [property: JsonPropertyName("package_name")] string PackageName,
    [property: JsonPropertyName("version")]      string? Version);

public sealed record PackageRemoveArgs(
    [property: JsonPropertyName("package_name")] string PackageName);
