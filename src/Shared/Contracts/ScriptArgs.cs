using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record ScriptReadArgs(
    [property: JsonPropertyName("asset_path")] string AssetPath,
    [property: JsonPropertyName("include_content")] bool? IncludeContent);

public sealed record ScriptUpdateOrCreateArgs(
    [property: JsonPropertyName("asset_path")] string AssetPath,
    [property: JsonPropertyName("content")] string Content);

public sealed record ScriptDeleteArgs(
    [property: JsonPropertyName("asset_path")] string AssetPath,
    [property: JsonPropertyName("use_trash")] bool? UseTrash);
