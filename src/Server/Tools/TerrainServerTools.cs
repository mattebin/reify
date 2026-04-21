using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class TerrainServerTools
{
    [McpServerTool(Name = "terrain-inspect"), Description(
        "Inspect a Terrain + TerrainData: size, heightmap/alphamap/detail " +
        "resolutions, layer/tree/detail counts, material template, LOD " +
        "pixel error, tree/detail distance settings. Warnings for missing " +
        "TerrainData, no TerrainLayers, missing material (URP/HDRP), very " +
        "large resolutions.")]
    public static async Task<JsonElement> TerrainInspect(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("terrain-inspect", args, ct);

    [McpServerTool(Name = "terrain-sample-height"), Description(
        "Sample height + normal + steepness at a world position on a " +
        "Terrain. Returns {height, height_world_y, normal, " +
        "steepness_degrees, normalized_uv, in_bounds}. Useful for snapping " +
        "objects to the terrain surface without a physics raycast.")]
    public static async Task<JsonElement> TerrainSampleHeight(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("terrain-sample-height", args, ct);

    [McpServerTool(Name = "terrain-layers"), Description(
        "List every TerrainLayer on a Terrain's TerrainData: diffuse + " +
        "normal textures (asset paths), tile_size/offset, metallic/" +
        "smoothness/normal_scale. Evidence for why a surface looks the way " +
        "it does.")]
    public static async Task<JsonElement> TerrainLayers(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("terrain-layers", args, ct);

    [McpServerTool(Name = "terrain-sample-alphamap"), Description(
        "Sample the alphamap (texture blend weights) at a world position. " +
        "Returns per-layer weight + the dominant_layer_index. Answers 'which " +
        "texture is painted here' in one call. Out-of-bounds positions " +
        "return in_bounds=false.")]
    public static async Task<JsonElement> TerrainSampleAlphamap(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("terrain-sample-alphamap", args, ct);

    [McpServerTool(Name = "terrain-trees"), Description(
        "List tree prototypes + instances on a Terrain. group_only=true " +
        "returns just per-prototype counts (fast). Otherwise returns up " +
        "to `limit` TreeInstances (default 500) with normalized AND world " +
        "positions, prototype_index, scale, rotation, color. truncated " +
        "flag when more instances exist.")]
    public static async Task<JsonElement> TerrainTrees(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("terrain-trees", args, ct);
}
