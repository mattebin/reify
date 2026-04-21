using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record AnimatorStateArgs(
    [property: JsonPropertyName("animator_instance_id")] int? AnimatorInstanceId,
    [property: JsonPropertyName("gameobject_path")]      string? GameObjectPath
);
