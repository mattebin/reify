using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

// ---------- asset ----------

public sealed record AssetFindArgs(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("guid")] string? Guid,
    [property: JsonPropertyName("path")] string? Path);

public sealed record AssetCreateArgs(
    [property: JsonPropertyName("kind")]      string Kind,
    [property: JsonPropertyName("path")]      string Path,
    [property: JsonPropertyName("type_name")] string? TypeName,
    [property: JsonPropertyName("shader")]    string? Shader);

public sealed record AssetDeleteArgs(
    [property: JsonPropertyName("path")]      string Path,
    [property: JsonPropertyName("use_trash")] bool? UseTrash);

public sealed record AssetGetArgs(
    [property: JsonPropertyName("path")]               string Path,
    [property: JsonPropertyName("include_properties")] bool? IncludeProperties);

public sealed record AssetRenameArgs(
    [property: JsonPropertyName("path")]     string Path,
    [property: JsonPropertyName("new_name")] string NewName);

public sealed record AssetMoveArgs(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")]   string To);

// ---------- prefab ----------

public sealed record PrefabCreateArgs(
    [property: JsonPropertyName("gameobject_path")]   string GameObjectPath,
    [property: JsonPropertyName("asset_path")]        string AssetPath,
    [property: JsonPropertyName("connect_instance")]  bool? ConnectInstance);

public sealed record PrefabInstantiateArgs(
    [property: JsonPropertyName("asset_path")]     string AssetPath,
    [property: JsonPropertyName("parent_path")]    string? ParentPath,
    [property: JsonPropertyName("position")]       Vec3Arg? Position,
    [property: JsonPropertyName("rotation_euler")] Vec3Arg? RotationEuler);

public sealed record PrefabOpenArgs(
    [property: JsonPropertyName("asset_path")] string AssetPath);

public sealed record PrefabGameObjectArgs(
    [property: JsonPropertyName("gameobject_path")] string GameObjectPath);
