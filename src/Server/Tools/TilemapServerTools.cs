using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class TilemapServerTools
{
    [McpServerTool(Name = "tilemap-inspect"), Description(
        "Inspect a Tilemap: origin, size, cell_bounds (post-Compress), " +
        "non-empty tile_count, cell_total, tile_anchor, orientation, color, " +
        "animation_frame_rate, parent Grid (cell_size/gap/layout/swizzle), " +
        "TilemapRenderer state (mode, sorting, material, mask). Warnings " +
        "for empty tilemap and inactive GameObject.")]
    public static async Task<JsonElement> TilemapInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("tilemap-inspect", new
    {
        instance_id, gameobject_path
    }, ct);

    [McpServerTool(Name = "tilemap-get-tile"), Description(
        "Read the tile at a cell position. cell_position is {x,y,z} integers. " +
        "Returns {has_tile, tile: {type_fqn, name, asset_path, sprite_name, " +
        "sprite_path}, world_position_center}.")]
    public static async Task<JsonElement> TilemapGetTile(
        UnityClient unity,
        JsonElement cell_position,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("tilemap-get-tile", new
    {
        instance_id, gameobject_path, cell_position
    }, ct);

    [McpServerTool(Name = "tilemap-set-tile"), Description(
        "Place or clear a tile at a cell position. tile_asset_path is " +
        "required unless clear=true. Undo-backed. Returns {before, after} " +
        "with has_tile + name for verification.")]
    public static async Task<JsonElement> TilemapSetTile(
        UnityClient unity,
        JsonElement cell_position,
        int? instance_id = null,
        string? gameobject_path = null,
        string? tile_asset_path = null,
        bool? clear = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("tilemap-set-tile", new
    {
        instance_id, gameobject_path, cell_position, tile_asset_path, clear
    }, ct);

    [McpServerTool(Name = "tilemap-clear-all"), Description(
        "Clear every tile from a Tilemap. Undo-backed. Returns the number " +
        "of tiles that were present before clearing.")]
    public static async Task<JsonElement> TilemapClearAll(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("tilemap-clear-all", new
    {
        instance_id, gameobject_path
    }, ct);
}
