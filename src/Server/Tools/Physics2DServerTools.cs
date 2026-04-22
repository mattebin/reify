using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class Physics2DServerTools
{
    [McpServerTool(Name = "physics2d-raycast"), Description(
        "Cast a 2D ray and return the first hit (mirrors physics-raycast for " +
        "3D). Args: origin {x,y}, direction {x,y}, optional max_distance, " +
        "layer_mask (int bitmask OR string[]), min_depth, max_depth.")]
    public static async Task<JsonElement> Physics2DRaycast(
        UnityClient unity,
        JsonElement origin,
        JsonElement direction,
        float? max_distance = null,
        JsonElement? layer_mask = null,
        float? min_depth = null,
        float? max_depth = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics2d-raycast", new
    {
        origin, direction, max_distance, layer_mask, min_depth, max_depth
    }, ct);

    [McpServerTool(Name = "physics2d-raycast-all"), Description(
        "Cast a 2D ray and return every hit, sorted by distance ascending.")]
    public static async Task<JsonElement> Physics2DRaycastAll(
        UnityClient unity,
        JsonElement origin,
        JsonElement direction,
        float? max_distance = null,
        JsonElement? layer_mask = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics2d-raycast-all", new
    {
        origin, direction, max_distance, layer_mask
    }, ct);

    [McpServerTool(Name = "physics2d-overlap-circle"), Description(
        "List every 2D Collider whose bounds intersect a circle at position " +
        "with given radius.")]
    public static async Task<JsonElement> Physics2DOverlapCircle(
        UnityClient unity,
        JsonElement position,
        float radius,
        JsonElement? layer_mask = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics2d-overlap-circle", new
    {
        position, radius, layer_mask
    }, ct);

    [McpServerTool(Name = "physics2d-overlap-box"), Description(
        "List every 2D Collider whose bounds intersect an oriented box. Args: " +
        "center {x,y}, size {x,y}, angle (degrees, default 0), layer_mask.")]
    public static async Task<JsonElement> Physics2DOverlapBox(
        UnityClient unity,
        JsonElement center,
        JsonElement size,
        float? angle = null,
        JsonElement? layer_mask = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics2d-overlap-box", new
    {
        center, size, angle, layer_mask
    }, ct);

    [McpServerTool(Name = "physics2d-settings"), Description(
        "Read-only dump of 2D Physics settings: gravity, default_contact_offset, " +
        "velocity/position iterations, query flags, simulation_mode, job " +
        "options, and every non-default layer-collision matrix entry.")]
    public static async Task<JsonElement> Physics2DSettings(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("physics2d-settings", null, ct);
}
