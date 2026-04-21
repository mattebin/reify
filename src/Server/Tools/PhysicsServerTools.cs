using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class PhysicsServerTools
{
    // Args passed as JsonElement pass-through because the shapes involve
    // nested Vec3/layer-mask-union types that are easier to keep loose.
    [McpServerTool(Name = "physics-raycast"), Description(
        "Cast a ray and return the first hit. Args: origin {x,y,z}, " +
        "direction {x,y,z}, optional max_distance (default infinity), " +
        "layer_mask (int bitmask OR string[] of layer names; default " +
        "Physics.DefaultRaycastLayers), query_trigger_interaction " +
        "(UseGlobal/Ignore/Collide). Returns {hit, hit_info with distance/" +
        "point/normal/collider refs}.")]
    public static async Task<JsonElement> PhysicsRaycast(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("physics-raycast", args, ct);

    [McpServerTool(Name = "physics-raycast-all"), Description(
        "Cast a ray and return EVERY hit, sorted by distance ascending. " +
        "Same args as physics-raycast.")]
    public static async Task<JsonElement> PhysicsRaycastAll(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("physics-raycast-all", args, ct);

    [McpServerTool(Name = "physics-spherecast"), Description(
        "Sphere cast (sweep a sphere along a ray). Args: origin, direction, " +
        "radius, max_distance, layer_mask, query_trigger_interaction.")]
    public static async Task<JsonElement> PhysicsSphereCast(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("physics-spherecast", args, ct);

    [McpServerTool(Name = "physics-overlap-sphere"), Description(
        "List every Collider whose bounds intersect a sphere at position " +
        "with given radius. Args: position {x,y,z}, radius, layer_mask, " +
        "query_trigger_interaction.")]
    public static async Task<JsonElement> PhysicsOverlapSphere(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("physics-overlap-sphere", args, ct);

    [McpServerTool(Name = "physics-overlap-box"), Description(
        "List every Collider whose bounds intersect an oriented box. Args: " +
        "center {x,y,z}, half_extents {x,y,z}, orientation {x,y,z,w} " +
        "(quaternion; default identity), layer_mask, " +
        "query_trigger_interaction.")]
    public static async Task<JsonElement> PhysicsOverlapBox(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("physics-overlap-box", args, ct);

    [McpServerTool(Name = "physics-settings"), Description(
        "Read-only dump of global Physics settings: gravity, thresholds, " +
        "default solver iterations, queries_hit_* flags, simulation mode, " +
        "and every non-default entry in the layer-collision matrix. " +
        "Warnings flag non-Earth gravity magnitude and unusual solver " +
        "iteration counts.")]
    public static async Task<JsonElement> PhysicsSettings(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("physics-settings", null, ct);
}
