using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record Vec3Arg(
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("z")] float Z
);

public sealed record GameObjectCreateArgs(
    [property: JsonPropertyName("name")]           string? Name,
    [property: JsonPropertyName("primitive")]      string? Primitive,
    [property: JsonPropertyName("parent_path")]    string? ParentPath,
    [property: JsonPropertyName("position")]       Vec3Arg? Position,
    [property: JsonPropertyName("rotation_euler")] Vec3Arg? RotationEuler,
    [property: JsonPropertyName("scale")]          Vec3Arg? Scale
);

public sealed record GameObjectFindArgs(
    [property: JsonPropertyName("name")]        string? Name,
    [property: JsonPropertyName("tag")]         string? Tag,
    [property: JsonPropertyName("path")]        string? Path,
    [property: JsonPropertyName("instance_id")] int?    InstanceId
);

public sealed record GameObjectDestroyArgs(
    [property: JsonPropertyName("path")]        string? Path,
    [property: JsonPropertyName("instance_id")] int?    InstanceId
);

public sealed record GameObjectModifyArgs(
    [property: JsonPropertyName("path")]        string Path,
    [property: JsonPropertyName("new_name")]    string? NewName,
    [property: JsonPropertyName("reparent_to")] string? ReparentTo,
    [property: JsonPropertyName("active")]      bool? Active,
    [property: JsonPropertyName("layer")]       int? Layer,
    [property: JsonPropertyName("tag")]         string? Tag
);

public sealed record ComponentAddArgs(
    [property: JsonPropertyName("path")]      string Path,
    [property: JsonPropertyName("type_name")] string TypeName
);

public sealed record ComponentGetArgs(
    [property: JsonPropertyName("path")]               string? Path,
    [property: JsonPropertyName("instance_id")]        int? InstanceId,
    [property: JsonPropertyName("include_properties")] bool? IncludeProperties
);
