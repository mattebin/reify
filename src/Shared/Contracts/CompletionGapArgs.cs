using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record ScriptExecuteArgs(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("type_name")] string? TypeName,
    [property: JsonPropertyName("method_name")] string? MethodName);

public sealed record ObjectGetArgs(
    [property: JsonPropertyName("asset_path")] string? AssetPath,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath,
    [property: JsonPropertyName("component_type")] string? ComponentType,
    [property: JsonPropertyName("instance_id")] int? InstanceId,
    [property: JsonPropertyName("include_properties")] bool? IncludeProperties);

public sealed record ObjectModifyArgs(
    [property: JsonPropertyName("asset_path")] string? AssetPath,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath,
    [property: JsonPropertyName("component_type")] string? ComponentType,
    [property: JsonPropertyName("instance_id")] int? InstanceId,
    [property: JsonPropertyName("properties")] Dictionary<string, JsonElement> Properties);

public sealed record ScenePathArgs(
    [property: JsonPropertyName("path")] string Path);

public sealed record AssetFindBuiltInArgs(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("limit")] int? Limit,
    [property: JsonPropertyName("include_hidden")] bool? IncludeHidden);

public sealed record AssetShaderListAllArgs(
    [property: JsonPropertyName("include_hidden")] bool? IncludeHidden,
    [property: JsonPropertyName("limit")] int? Limit);

public sealed record ComponentListAllArgs(
    [property: JsonPropertyName("name_like")] string? NameLike,
    [property: JsonPropertyName("namespace_like")] string? NamespaceLike,
    [property: JsonPropertyName("limit")] int? Limit,
    [property: JsonPropertyName("include_editor_only")] bool? IncludeEditorOnly);
