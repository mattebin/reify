using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record ComponentModifyArgs(
    [property: JsonPropertyName("instance_id")]      int? InstanceId,
    [property: JsonPropertyName("gameobject_path")]  string? GameObjectPath,
    [property: JsonPropertyName("component_type")]   string? ComponentType,
    [property: JsonPropertyName("properties")]       Dictionary<string, JsonElement> Properties
);

public sealed record ComponentRemoveArgs(
    [property: JsonPropertyName("instance_id")]     int? InstanceId,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath,
    [property: JsonPropertyName("component_type")]  string? ComponentType
);

public sealed record ComponentSetPropertyArgs(
    [property: JsonPropertyName("instance_id")]     int? InstanceId,
    [property: JsonPropertyName("gameobject_path")] string? GameObjectPath,
    [property: JsonPropertyName("component_type")]  string? ComponentType,
    [property: JsonPropertyName("property_path")]   string PropertyPath,
    [property: JsonPropertyName("value")]           JsonElement Value
);
