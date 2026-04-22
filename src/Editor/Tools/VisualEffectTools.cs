using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// VFX Graph read-only surface via reflection so reify stays free of
    /// a direct com.unity.visualeffectgraph dependency. Covers the
    /// VisualEffect runtime component + VisualEffectAsset.
    ///
    /// When the package isn't loaded, each tool returns a structured error
    /// rather than failing to compile.
    /// </summary>
    internal static class VisualEffectTools
    {
        private const string VfxAsm = "Unity.VisualEffectGraph.Runtime";

        // ---------- visual-effect-inspect ----------
        [ReifyTool("visual-effect-inspect")]
        public static Task<object> Inspect(JToken args)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var vfxType = Type.GetType($"UnityEngine.VFX.VisualEffect, {VfxAsm}")
                    ?? throw new InvalidOperationException(
                        "UnityEngine.VFX.VisualEffect not loaded — install " +
                        "com.unity.visualeffectgraph to use this tool.");

                var go = ResolveGameObject(args);
                var vfx = go.GetComponent(vfxType)
                    ?? throw new InvalidOperationException(
                        $"GameObject '{GameObjectResolver.PathOf(go)}' has no VisualEffect component.");
                var t = vfx.GetType();

                var asset = t.GetProperty("visualEffectAsset")?.GetValue(vfx) as UnityEngine.Object;
                var aliveParticleCount = Get<int>(t, vfx, "aliveParticleCount");
                var initialSeed        = Get<uint>(t, vfx, "startSeed");
                var pause              = Get<bool>(t, vfx, "pause");
                var playRate           = Get<float>(t, vfx, "playRate");
                var culled             = Get<bool>(t, vfx, "culled");

                // Enumerate exposed parameters via IEnumerable<VFXExposedProperty>.
                var exposedProps = new List<object>();
                try
                {
                    var getParameters = t.GetMethod("GetExposedProperties", BindingFlags.Instance | BindingFlags.Public);
                    if (getParameters != null)
                    {
                        // Signature: void GetExposedProperties(List<VFXExposedProperty> outProps)
                        var exposedType = Type.GetType($"UnityEngine.VFX.VFXExposedProperty, {VfxAsm}");
                        if (exposedType != null)
                        {
                            var listType = typeof(List<>).MakeGenericType(exposedType);
                            var listInstance = Activator.CreateInstance(listType);
                            getParameters.Invoke(vfx, new[] { listInstance });
                            foreach (var item in (System.Collections.IEnumerable)listInstance)
                            {
                                var it = item.GetType();
                                exposedProps.Add(new
                                {
                                    name = it.GetField("name")?.GetValue(item)?.ToString(),
                                    type = (it.GetField("type")?.GetValue(item) as Type)?.FullName
                                });
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    exposedProps.Add(new { warning = "exposed-property enumeration failed: " + e.Message });
                }

                return new
                {
                    instance_id            = GameObjectResolver.InstanceIdOf(vfx as UnityEngine.Object),
                    gameobject_instance_id = GameObjectResolver.InstanceIdOf(go),
                    gameobject_path        = GameObjectResolver.PathOf(go),
                    enabled                = (vfx as Behaviour)?.enabled ?? false,
                    asset_path             = asset != null ? AssetDatabase.GetAssetPath(asset) : null,
                    asset_guid             = asset != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)) : null,
                    alive_particle_count   = aliveParticleCount,
                    start_seed             = initialSeed,
                    pause                  = pause,
                    play_rate              = playRate,
                    culled                 = culled,
                    exposed_property_count = exposedProps.Count,
                    exposed_properties     = exposedProps.ToArray(),
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- visual-effect-asset-inspect ----------
        [ReifyTool("visual-effect-asset-inspect")]
        public static Task<object> AssetInspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required (a .vfx file).");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!path.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".vfxoperator", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("visual-effect-asset-inspect expects a .vfx asset.");

                var assetType = Type.GetType($"UnityEngine.VFX.VisualEffectAsset, {VfxAsm}")
                    ?? throw new InvalidOperationException(
                        "UnityEngine.VFX.VisualEffectAsset not loaded — install com.unity.visualeffectgraph.");

                var asset = AssetDatabase.LoadAssetAtPath(path, assetType) as UnityEngine.Object
                    ?? throw new InvalidOperationException($"No VisualEffectAsset at '{path}'.");

                var importer = AssetImporter.GetAtPath(path);

                return new
                {
                    asset_path    = path,
                    guid          = AssetDatabase.AssetPathToGUID(path),
                    name          = asset.name,
                    asset_type    = assetType.FullName,
                    importer_type = importer?.GetType().FullName,
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static GameObject ResolveGameObject(JToken args)
        {
            var instanceId = args?["instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("instance_id") : null;
            var goPath = args?.Value<string>("gameobject_path");

            if (instanceId.HasValue)
            {
                var obj = GameObjectResolver.ByInstanceId(instanceId.Value)
                    ?? throw new InvalidOperationException($"No object with instance_id {instanceId}.");
                if (obj is GameObject go) return go;
                if (obj is Component c) return c.gameObject;
                throw new InvalidOperationException(
                    $"instance_id {instanceId} is neither a GameObject nor a Component.");
            }
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("Provide either instance_id or gameobject_path.");
            return GameObjectResolver.ByPath(goPath)
                ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
        }

        private static T Get<T>(Type type, object instance, string name)
        {
            var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) try { return (T)Convert.ChangeType(p.GetValue(instance), typeof(T)); } catch { }
            var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) try { return (T)Convert.ChangeType(f.GetValue(instance), typeof(T)); } catch { }
            return default;
        }
    }
}
