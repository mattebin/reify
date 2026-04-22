using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class PhysicsServerTools
{
    // Nested Vec3/quaternion/layer-mask-union shapes are passed as JsonElement
    // so callers can feed them verbatim without the server inventing types.
    [McpServerTool(Name = "physics-raycast"), Description(
        "Cast a ray and return the first hit. origin {x,y,z}, direction " +
        "{x,y,z}. layer_mask is int bitmask OR string[] of layer names " +
        "(default Physics.DefaultRaycastLayers). query_trigger_interaction " +
        "∈ UseGlobal/Ignore/Collide. Returns {hit, hit_info with distance/" +
        "point/normal/collider refs}.")]
    public static async Task<JsonElement> PhysicsRaycast(
        UnityClient unity,
        JsonElement origin,
        JsonElement direction,
        float? max_distance = null,
        JsonElement? layer_mask = null,
        string? query_trigger_interaction = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics-raycast", new
    {
        origin, direction, max_distance, layer_mask, query_trigger_interaction
    }, ct);

    [McpServerTool(Name = "physics-raycast-all"), Description(
        "Cast a ray and return EVERY hit, sorted by distance ascending. " +
        "Same args as physics-raycast.")]
    public static async Task<JsonElement> PhysicsRaycastAll(
        UnityClient unity,
        JsonElement origin,
        JsonElement direction,
        float? max_distance = null,
        JsonElement? layer_mask = null,
        string? query_trigger_interaction = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics-raycast-all", new
    {
        origin, direction, max_distance, layer_mask, query_trigger_interaction
    }, ct);

    [McpServerTool(Name = "physics-spherecast"), Description(
        "Sphere cast (sweep a sphere along a ray).")]
    public static async Task<JsonElement> PhysicsSphereCast(
        UnityClient unity,
        JsonElement origin,
        JsonElement direction,
        float radius,
        float? max_distance = null,
        JsonElement? layer_mask = null,
        string? query_trigger_interaction = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics-spherecast", new
    {
        origin, direction, radius, max_distance, layer_mask, query_trigger_interaction
    }, ct);

    [McpServerTool(Name = "physics-overlap-sphere"), Description(
        "List every Collider whose bounds intersect a sphere at position " +
        "with given radius. position {x,y,z}.")]
    public static async Task<JsonElement> PhysicsOverlapSphere(
        UnityClient unity,
        JsonElement position,
        float radius,
        JsonElement? layer_mask = null,
        string? query_trigger_interaction = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics-overlap-sphere", new
    {
        position, radius, layer_mask, query_trigger_interaction
    }, ct);

    [McpServerTool(Name = "physics-overlap-box"), Description(
        "List every Collider whose bounds intersect an oriented box. " +
        "center {x,y,z}, half_extents {x,y,z}, orientation {x,y,z,w} " +
        "(quaternion; default identity).")]
    public static async Task<JsonElement> PhysicsOverlapBox(
        UnityClient unity,
        JsonElement center,
        JsonElement half_extents,
        JsonElement? orientation = null,
        JsonElement? layer_mask = null,
        string? query_trigger_interaction = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics-overlap-box", new
    {
        center, half_extents, orientation, layer_mask, query_trigger_interaction
    }, ct);

    [McpServerTool(Name = "physics-settings"), Description(
        "Read-only dump of global Physics settings: gravity, thresholds, " +
        "default solver iterations, queries_hit_* flags, simulation mode, " +
        "and every non-default entry in the layer-collision matrix. " +
        "Warnings flag non-Earth gravity magnitude and unusual solver " +
        "iteration counts.")]
    public static async Task<JsonElement> PhysicsSettings(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics-settings", null, ct);
}
