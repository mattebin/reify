using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record AudioSourceRefArgs(
    [property: JsonPropertyName("instance_id")]     int? InstanceId,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath);

public sealed record AudioSourcePlayArgs(
    [property: JsonPropertyName("instance_id")]       int? InstanceId,
    [property: JsonPropertyName("gameobject_path")]   string? GameObjectPath,
    [property: JsonPropertyName("one_shot")]          bool? OneShot,
    [property: JsonPropertyName("volume_scale")]      float? VolumeScale,
    [property: JsonPropertyName("clip_asset_path")]   string? ClipAssetPath);

public sealed record AudioMixerRefArgs(
    [property: JsonPropertyName("asset_path")]  string? AssetPath,
    [property: JsonPropertyName("instance_id")] int? InstanceId);

public sealed record AudioMixerSetExposedArgs(
    [property: JsonPropertyName("asset_path")]     string? AssetPath,
    [property: JsonPropertyName("instance_id")]    int? InstanceId,
    [property: JsonPropertyName("parameter_name")] string ParameterName,
    [property: JsonPropertyName("value")]          float? Value,
    [property: JsonPropertyName("clear")]          bool? Clear);

public sealed record AudioClipInspectArgs(
    [property: JsonPropertyName("asset_path")] string AssetPath);
