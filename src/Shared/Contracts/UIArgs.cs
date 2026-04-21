using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record UIObjectRefArgs(
    [property: JsonPropertyName("instance_id")]     int? InstanceId,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath);

public sealed record Vec2Arg(
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y);

public sealed record UIRectTransformSetArgs(
    [property: JsonPropertyName("instance_id")]       int? InstanceId,
    [property: JsonPropertyName("gameobject_path")]   string? GameObjectPath,
    [property: JsonPropertyName("anchor_min")]        Vec2Arg? AnchorMin,
    [property: JsonPropertyName("anchor_max")]        Vec2Arg? AnchorMax,
    [property: JsonPropertyName("pivot")]             Vec2Arg? Pivot,
    [property: JsonPropertyName("anchored_position")] Vec2Arg? AnchoredPosition,
    [property: JsonPropertyName("size_delta")]        Vec2Arg? SizeDelta);

public sealed record UIRaycastAtPointArgs(
    [property: JsonPropertyName("screen_x")] float ScreenX,
    [property: JsonPropertyName("screen_y")] float ScreenY);
