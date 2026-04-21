using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record NavAgentRefArgs(
    [property: JsonPropertyName("instance_id")]     int? InstanceId,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath);

public sealed record NavAgentSetDestinationArgs(
    [property: JsonPropertyName("instance_id")]     int? InstanceId,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath,
    [property: JsonPropertyName("destination")]     Vec3Arg Destination);

public sealed record NavAgentWarpArgs(
    [property: JsonPropertyName("instance_id")]     int? InstanceId,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath,
    [property: JsonPropertyName("position")]        Vec3Arg Position);

public sealed record NavAgentStopArgs(
    [property: JsonPropertyName("instance_id")]     int? InstanceId,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath,
    [property: JsonPropertyName("clear_path")]      bool? ClearPath);

public sealed record NavPositionQueryArgs(
    [property: JsonPropertyName("position")]     Vec3Arg Position,
    [property: JsonPropertyName("max_distance")] float? MaxDistance,
    [property: JsonPropertyName("area_mask")]    int? AreaMask);

public sealed record NavRaycastArgs(
    [property: JsonPropertyName("source")]    Vec3Arg Source,
    [property: JsonPropertyName("target")]    Vec3Arg Target,
    [property: JsonPropertyName("area_mask")] int? AreaMask);

public sealed record NavFindEdgeArgs(
    [property: JsonPropertyName("position")]  Vec3Arg Position,
    [property: JsonPropertyName("area_mask")] int? AreaMask);

public sealed record NavCalculatePathArgs(
    [property: JsonPropertyName("source")]    Vec3Arg Source,
    [property: JsonPropertyName("target")]    Vec3Arg Target,
    [property: JsonPropertyName("area_mask")] int? AreaMask);
