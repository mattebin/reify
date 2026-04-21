using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// 2D Tilemap surface: inspect a Tilemap (cell bounds, non-empty tile
    /// count), read a single tile, place/erase tiles, clear the tilemap.
    /// Paint tooling (brushes, prefab brushes) is deferred — most agents
    /// want direct tile reads/writes rather than brush emulation.
    /// </summary>
    internal static class TilemapTools
    {
        // ---------- tilemap-inspect ----------
        [ReifyTool("tilemap-inspect")]
        public static Task<object> Inspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var tm = ResolveTilemap(args);
                var warnings = new List<string>();

                tm.CompressBounds();  // collapse to the live region
                var bounds = tm.cellBounds;
                var origin = tm.origin;
                var size   = tm.size;

                // Count non-empty tiles in the cell bounds.
                var tileCount = 0;
                var positions = new BoundsInt(bounds.position, bounds.size);
                foreach (var pos in positions.allPositionsWithin)
                    if (tm.HasTile(pos)) tileCount++;

                if (tileCount == 0)
                    warnings.Add("Tilemap is empty — no tiles placed.");
                if (!tm.gameObject.activeInHierarchy)
                    warnings.Add("GameObject inactive — tilemap won't render.");

                var grid = tm.layoutGrid;
                var renderer = tm.GetComponent<TilemapRenderer>();

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(tm),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(tm.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(tm.gameObject),
                    origin                 = new { x = origin.x, y = origin.y, z = origin.z },
                    size                   = new { x = size.x,   y = size.y,   z = size.z },
                    cell_bounds            = new {
                        position = new { x = bounds.position.x, y = bounds.position.y, z = bounds.position.z },
                        size     = new { x = bounds.size.x,     y = bounds.size.y,     z = bounds.size.z }
                    },
                    tile_count             = tileCount,
                    cell_total             = bounds.size.x * bounds.size.y * bounds.size.z,
                    tile_anchor            = new { x = tm.tileAnchor.x, y = tm.tileAnchor.y, z = tm.tileAnchor.z },
                    orientation            = tm.orientation.ToString(),
                    color                  = new { r = tm.color.r, g = tm.color.g, b = tm.color.b, a = tm.color.a },
                    animation_frame_rate   = tm.animationFrameRate,
                    grid = grid != null ? new
                    {
                        cell_size   = new { x = grid.cellSize.x, y = grid.cellSize.y, z = grid.cellSize.z },
                        cell_gap    = new { x = grid.cellGap.x,  y = grid.cellGap.y,  z = grid.cellGap.z },
                        cell_layout = grid.cellLayout.ToString(),
                        cell_swizzle = grid.cellSwizzle.ToString()
                    } : null,
                    renderer = renderer != null ? new
                    {
                        sort_order      = renderer.sortOrder.ToString(),
                        mode            = renderer.mode.ToString(),
                        sorting_layer   = renderer.sortingLayerName,
                        sorting_order   = renderer.sortingOrder,
                        material_name   = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : null,
                        mask_interaction = renderer.maskInteraction.ToString(),
                        enabled         = renderer.enabled
                    } : null,
                    warnings               = warnings.ToArray(),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- tilemap-get-tile ----------
        [ReifyTool("tilemap-get-tile")]
        public static Task<object> GetTile(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var tm = ResolveTilemap(args);
                var pos = ReadCellPos(args);
                var has = tm.HasTile(pos);
                var tile = has ? tm.GetTile(pos) : null;
                var sprite = has ? tm.GetSprite(pos) : null;
                return new
                {
                    tilemap_instance_id = GameObjectResolver.InstanceIdOf(tm),
                    tilemap_path        = GameObjectResolver.PathOf(tm.gameObject),
                    cell_position       = new { x = pos.x, y = pos.y, z = pos.z },
                    has_tile            = has,
                    tile = has && tile != null ? (object)new
                    {
                        type_fqn    = tile.GetType().FullName,
                        name        = tile.name,
                        asset_path  = AssetDatabase.GetAssetPath(tile),
                        instance_id = GameObjectResolver.InstanceIdOf(tile),
                        sprite_name = sprite != null ? sprite.name : null,
                        sprite_path = sprite != null ? AssetDatabase.GetAssetPath(sprite) : null
                    } : null,
                    world_position_center = V3(tm.GetCellCenterWorld(pos)),
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- tilemap-set-tile ----------
        [ReifyTool("tilemap-set-tile")]
        public static Task<object> SetTile(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var tm = ResolveTilemap(args);
                var pos = ReadCellPos(args);
                var tilePath = args?.Value<string>("tile_asset_path");
                var clear    = args?.Value<bool?>("clear") ?? false;

                Undo.RecordObject(tm, "Reify: set tile");
                var before = new
                {
                    had_tile    = tm.HasTile(pos),
                    prev_name   = tm.HasTile(pos) && tm.GetTile(pos) != null ? tm.GetTile(pos).name : null
                };

                TileBase tile = null;
                if (!clear)
                {
                    if (string.IsNullOrEmpty(tilePath))
                        throw new ArgumentException("tile_asset_path required unless clear=true.");
                    tile = AssetDatabase.LoadAssetAtPath<TileBase>(tilePath)
                        ?? throw new InvalidOperationException($"No TileBase asset at path: {tilePath}");
                }

                tm.SetTile(pos, tile);
                EditorUtility.SetDirty(tm);

                return new
                {
                    tilemap_instance_id = GameObjectResolver.InstanceIdOf(tm),
                    tilemap_path        = GameObjectResolver.PathOf(tm.gameObject),
                    cell_position       = new { x = pos.x, y = pos.y, z = pos.z },
                    cleared             = clear,
                    tile_asset_path     = tilePath,
                    before,
                    after = new
                    {
                        has_tile  = tm.HasTile(pos),
                        tile_name = tm.HasTile(pos) && tm.GetTile(pos) != null ? tm.GetTile(pos).name : null
                    },
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- tilemap-clear-all ----------
        [ReifyTool("tilemap-clear-all")]
        public static Task<object> ClearAll(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var tm = ResolveTilemap(args);
                Undo.RecordObject(tm, "Reify: clear tilemap");
                tm.CompressBounds();
                var before = tm.cellBounds;

                var tileCountBefore = 0;
                foreach (var pos in before.allPositionsWithin)
                    if (tm.HasTile(pos)) tileCountBefore++;

                tm.ClearAllTiles();
                EditorUtility.SetDirty(tm);

                return new
                {
                    tilemap_instance_id = GameObjectResolver.InstanceIdOf(tm),
                    tilemap_path        = GameObjectResolver.PathOf(tm.gameObject),
                    tiles_cleared       = tileCountBefore,
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static Tilemap ResolveTilemap(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                return obj as Tilemap
                    ?? (obj as GameObject)?.GetComponent<Tilemap>()
                    ?? throw new InvalidOperationException(
                        $"instance_id {instanceId} does not resolve to a Tilemap or a GameObject with one.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            var go = GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
            return go.GetComponent<Tilemap>()
                ?? throw new InvalidOperationException($"GameObject '{goPath}' has no Tilemap component.");
        }

        private static Vector3Int ReadCellPos(JToken args)
        {
            var cell = args?["cell_position"];
            if (cell == null || cell.Type == JTokenType.Null)
                throw new ArgumentException("cell_position {x,y,z} is required (integers).");
            return new Vector3Int(
                cell.Value<int?>("x") ?? 0,
                cell.Value<int?>("y") ?? 0,
                cell.Value<int?>("z") ?? 0);
        }

        private static object V3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    }
}
