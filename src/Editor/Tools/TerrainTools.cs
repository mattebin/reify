using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Terrain read surface: inspect a Terrain + its TerrainData, sample
    /// height/normal/steepness at a world point, list terrain layers, list
    /// tree instances grouped by prototype, sample the alphamap (texture
    /// blend weights) at a world point. Heightmap writes deferred — they
    /// need care to preserve continuity; SerializedProperty access via
    /// component-set-property covers simple cases.
    /// </summary>
    internal static class TerrainTools
    {
        // ---------- terrain-inspect ----------
        [ReifyTool("terrain-inspect")]
        public static Task<object> Inspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var terrain = ResolveTerrain(args);
                var data = terrain.terrainData;
                var warnings = new List<string>();

                if (data == null)
                {
                    warnings.Add("Terrain has no TerrainData assigned — nothing to render.");
                    return BuildMinimalInspect(terrain, warnings);
                }

                if (data.terrainLayers == null || data.terrainLayers.Length == 0)
                    warnings.Add("TerrainData has no TerrainLayers — surface will render with the fallback shader.");
                if (terrain.materialTemplate == null)
                    warnings.Add("Terrain.materialTemplate is null — URP/HDRP projects need an explicit terrain material.");
                if (data.heightmapResolution > 2049)
                    warnings.Add($"heightmapResolution = {data.heightmapResolution} is large; memory cost scales with the square.");
                if (data.detailResolution > 2048)
                    warnings.Add($"detailResolution = {data.detailResolution} is large; per-cell draw cost can explode.");

                var treeInstances = data.treeInstances;
                var treeCount = treeInstances != null ? treeInstances.Length : 0;
                var prototypeCount = data.treePrototypes != null ? data.treePrototypes.Length : 0;
                var detailProtoCount = data.detailPrototypes != null ? data.detailPrototypes.Length : 0;

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(terrain),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(terrain.gameObject),
                    gameobject_path        = GameObjectResolver.PathOf(terrain.gameObject),
                    enabled                = terrain.enabled,
                    terrain_data = new
                    {
                        asset_path              = AssetDatabase.GetAssetPath(data),
                        instance_id             = GameObjectResolver.InstanceIdOf(data),
                        name                    = data.name,
                        size                    = new { x = data.size.x, y = data.size.y, z = data.size.z },
                        bounds                  = new {
                            center = new { x = data.bounds.center.x, y = data.bounds.center.y, z = data.bounds.center.z },
                            size   = new { x = data.bounds.size.x,   y = data.bounds.size.y,   z = data.bounds.size.z   }
                        },
                        heightmap_resolution    = data.heightmapResolution,
                        heightmap_scale         = new { x = data.heightmapScale.x, y = data.heightmapScale.y, z = data.heightmapScale.z },
                        alphamap_resolution     = data.alphamapResolution,
                        alphamap_width          = data.alphamapWidth,
                        alphamap_height         = data.alphamapHeight,
                        base_map_resolution     = data.baseMapResolution,
                        detail_resolution       = data.detailResolution,
                        detail_patch_resolution = data.detailResolutionPerPatch
                    },
                    terrain_layers_count    = data.terrainLayers?.Length ?? 0,
                    tree_prototype_count    = prototypeCount,
                    tree_instance_count     = treeCount,
                    detail_prototype_count  = detailProtoCount,
                    material_template_path  = terrain.materialTemplate != null ? AssetDatabase.GetAssetPath(terrain.materialTemplate) : null,
                    heightmap_pixel_error   = terrain.heightmapPixelError,
                    basemap_distance        = terrain.basemapDistance,
                    draw_trees_and_foliage  = terrain.drawTreesAndFoliage,
                    tree_distance           = terrain.treeDistance,
                    tree_billboard_distance = terrain.treeBillboardDistance,
                    tree_maximum_full_lod_count = terrain.treeMaximumFullLODCount,
                    detail_object_distance  = terrain.detailObjectDistance,
                    detail_object_density   = terrain.detailObjectDensity,
                    shadow_casting_mode     = terrain.shadowCastingMode.ToString(),
                    warnings                = warnings.ToArray(),
                    read_at_utc             = DateTime.UtcNow.ToString("o"),
                    frame                   = (long)Time.frameCount
                };
            });
        }

        // ---------- terrain-sample-height ----------
        [ReifyTool("terrain-sample-height")]
        public static Task<object> SampleHeight(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var terrain = ResolveTerrain(args);
                var data = terrain.terrainData
                    ?? throw new InvalidOperationException("Terrain has no TerrainData — cannot sample.");
                var world = ReadVec3Required(args?["world_position"], "world_position");

                var height = terrain.SampleHeight(world);
                // Normal + steepness at the corresponding normalized coord.
                var tPos = terrain.transform.position;
                var nx = Mathf.Clamp01((world.x - tPos.x) / data.size.x);
                var nz = Mathf.Clamp01((world.z - tPos.z) / data.size.z);
                var normal = data.GetInterpolatedNormal(nx, nz);
                var steepnessDeg = data.GetSteepness(nx, nz);

                return new
                {
                    terrain_instance_id = GameObjectResolver.InstanceIdOf(terrain),
                    gameobject_path     = GameObjectResolver.PathOf(terrain.gameObject),
                    world_position      = V3(world),
                    height              = height,
                    height_world_y      = tPos.y + height,
                    normal              = V3(normal),
                    steepness_degrees   = steepnessDeg,
                    normalized_uv       = new { u = nx, v = nz },
                    in_bounds           = nx > 0 && nx < 1 && nz > 0 && nz < 1,
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- terrain-layers ----------
        [ReifyTool("terrain-layers")]
        public static Task<object> Layers(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var terrain = ResolveTerrain(args);
                var data = terrain.terrainData
                    ?? throw new InvalidOperationException("Terrain has no TerrainData.");
                var layers = data.terrainLayers ?? Array.Empty<TerrainLayer>();

                var result = new List<object>(layers.Length);
                for (var i = 0; i < layers.Length; i++)
                {
                    var l = layers[i];
                    if (l == null) { result.Add(new { index = i, is_null = true }); continue; }
                    result.Add(new
                    {
                        index             = i,
                        name              = l.name,
                        asset_path        = AssetDatabase.GetAssetPath(l),
                        diffuse_texture   = l.diffuseTexture != null ? new {
                            name = l.diffuseTexture.name, asset_path = AssetDatabase.GetAssetPath(l.diffuseTexture)
                        } : null,
                        normal_map        = l.normalMapTexture != null ? new {
                            name = l.normalMapTexture.name, asset_path = AssetDatabase.GetAssetPath(l.normalMapTexture)
                        } : null,
                        tile_size         = new { x = l.tileSize.x, y = l.tileSize.y },
                        tile_offset       = new { x = l.tileOffset.x, y = l.tileOffset.y },
                        metallic          = l.metallic,
                        smoothness        = l.smoothness,
                        normal_scale      = l.normalScale
                    });
                }

                return new
                {
                    terrain_instance_id = GameObjectResolver.InstanceIdOf(terrain),
                    layer_count         = layers.Length,
                    layers              = result.ToArray(),
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- terrain-sample-alphamap ----------
        [ReifyTool("terrain-sample-alphamap")]
        public static Task<object> SampleAlphamap(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var terrain = ResolveTerrain(args);
                var data = terrain.terrainData
                    ?? throw new InvalidOperationException("Terrain has no TerrainData.");
                var world = ReadVec3Required(args?["world_position"], "world_position");

                var tPos = terrain.transform.position;
                var nx = (world.x - tPos.x) / data.size.x;
                var nz = (world.z - tPos.z) / data.size.z;
                if (nx < 0 || nx > 1 || nz < 0 || nz > 1)
                {
                    return new
                    {
                        in_bounds = false,
                        normalized_uv = new { u = nx, v = nz },
                        warning = "World position is outside the terrain bounds; no alphamap sample taken.",
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame = (long)Time.frameCount
                    };
                }

                var x = Mathf.Clamp((int)(nx * data.alphamapWidth),  0, data.alphamapWidth  - 1);
                var z = Mathf.Clamp((int)(nz * data.alphamapHeight), 0, data.alphamapHeight - 1);
                var weights = data.GetAlphamaps(x, z, 1, 1); // [1,1,layerCount]
                var layers = data.terrainLayers ?? Array.Empty<TerrainLayer>();

                var perLayer = new List<object>();
                var dominantIdx = -1;
                var dominantWeight = 0f;
                for (var i = 0; i < weights.GetLength(2); i++)
                {
                    var w = weights[0, 0, i];
                    perLayer.Add(new
                    {
                        index         = i,
                        layer_name    = i < layers.Length && layers[i] != null ? layers[i].name : null,
                        asset_path    = i < layers.Length && layers[i] != null ? AssetDatabase.GetAssetPath(layers[i]) : null,
                        weight        = w
                    });
                    if (w > dominantWeight) { dominantWeight = w; dominantIdx = i; }
                }

                return new
                {
                    terrain_instance_id = GameObjectResolver.InstanceIdOf(terrain),
                    world_position      = V3(world),
                    normalized_uv       = new { u = nx, v = nz },
                    alphamap_xy         = new { x, z },
                    in_bounds           = true,
                    layer_count         = weights.GetLength(2),
                    weights             = perLayer.ToArray(),
                    dominant_layer_index  = dominantIdx,
                    dominant_layer_weight = dominantWeight,
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- terrain-trees ----------
        [ReifyTool("terrain-trees")]
        public static Task<object> Trees(JToken args)
        {
            var limit = args?.Value<int?>("limit") ?? 500;
            var groupOnly = args?.Value<bool?>("group_only") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var terrain = ResolveTerrain(args);
                var data = terrain.terrainData
                    ?? throw new InvalidOperationException("Terrain has no TerrainData.");
                var prototypes = data.treePrototypes ?? Array.Empty<TreePrototype>();
                var instances  = data.treeInstances  ?? Array.Empty<TreeInstance>();

                // Group by prototype — cheap, always returned.
                var byProto = new int[prototypes.Length];
                for (var i = 0; i < instances.Length; i++)
                {
                    var p = instances[i].prototypeIndex;
                    if (p >= 0 && p < byProto.Length) byProto[p]++;
                }

                var protoList = new object[prototypes.Length];
                for (var i = 0; i < prototypes.Length; i++)
                {
                    var proto = prototypes[i];
                    var prefab = proto.prefab;
                    protoList[i] = new
                    {
                        index      = i,
                        prefab_name = prefab != null ? prefab.name : null,
                        asset_path  = prefab != null ? AssetDatabase.GetAssetPath(prefab) : null,
                        bend_factor = proto.bendFactor,
                        instance_count = byProto[i]
                    };
                }

                object instanceSample = null;
                var truncated = false;
                if (!groupOnly)
                {
                    var tPos = terrain.transform.position;
                    var tSize = data.size;
                    var n = Math.Min(instances.Length, limit);
                    var list = new List<object>(n);
                    for (var i = 0; i < n; i++)
                    {
                        var ti = instances[i];
                        var wx = tPos.x + ti.position.x * tSize.x;
                        var wy = tPos.y + ti.position.y * tSize.y;
                        var wz = tPos.z + ti.position.z * tSize.z;
                        list.Add(new
                        {
                            prototype_index = ti.prototypeIndex,
                            normalized      = new { x = ti.position.x, y = ti.position.y, z = ti.position.z },
                            world_position  = new { x = wx, y = wy, z = wz },
                            width_scale     = ti.widthScale,
                            height_scale    = ti.heightScale,
                            rotation_rad    = ti.rotation,
                            color           = new { r = ti.color.r / 255f, g = ti.color.g / 255f, b = ti.color.b / 255f, a = ti.color.a / 255f }
                        });
                    }
                    instanceSample = list.ToArray();
                    truncated = instances.Length > n;
                }

                return new
                {
                    terrain_instance_id = GameObjectResolver.InstanceIdOf(terrain),
                    prototype_count     = prototypes.Length,
                    prototypes          = protoList,
                    instance_total      = instances.Length,
                    returned            = groupOnly ? 0 : (instanceSample != null ? ((object[])instanceSample).Length : 0),
                    truncated,
                    limit               = groupOnly ? (int?)null : limit,
                    instances           = instanceSample,
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static Terrain ResolveTerrain(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                return obj as Terrain
                    ?? (obj as GameObject)?.GetComponent<Terrain>()
                    ?? throw new InvalidOperationException(
                        $"instance_id {instanceId} does not resolve to a Terrain or a GameObject with one.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            var go = GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
            return go.GetComponent<Terrain>()
                ?? throw new InvalidOperationException($"GameObject '{goPath}' has no Terrain component.");
        }

        private static object BuildMinimalInspect(Terrain t, List<string> warnings) => new
        {
            instance_id     = GameObjectResolver.InstanceIdOf(t),
            gameobject_path = GameObjectResolver.PathOf(t.gameObject),
            enabled         = t.enabled,
            terrain_data    = (object)null,
            warnings        = warnings.ToArray(),
            read_at_utc     = DateTime.UtcNow.ToString("o"),
            frame           = (long)Time.frameCount
        };

        private static Vector3 ReadVec3Required(JToken t, string field)
        {
            if (t == null || t.Type == JTokenType.Null)
                throw new ArgumentException($"{field} is required.");
            return new Vector3(
                t.Value<float?>("x") ?? 0, t.Value<float?>("y") ?? 0, t.Value<float?>("z") ?? 0);
        }

        private static object V3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    }
}
