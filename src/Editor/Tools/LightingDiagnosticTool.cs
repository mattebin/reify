using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// 8th Phase C philosophy tool. Structured full state of a scene's
    /// lighting environment: skybox, ambient, fog, lights, probes, lightmap
    /// config, post-process volumes. Warnings catch the diagnostics-by-
    /// screenshot wall — why no lighting, why dim, why no reflections,
    /// why bloom not visible.
    /// </summary>
    internal static class LightingDiagnosticTool
    {
        public static Task<object> Handle(JToken args)
        {
            var scenePath = args?.Value<string>("scene_path");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scene = ResolveScene(scenePath);
                var warnings = new List<string>();

                // ---- environment ----
                var skybox       = RenderSettings.skybox;
                var sun          = RenderSettings.sun;
                var ambientMode  = RenderSettings.ambientMode;
                var reflectSrc   = RenderSettings.defaultReflectionMode;

                // ---- lights in scene ----
                var lights = CollectLights(scene);
                var dirLights        = new List<Light>();
                var pointLights      = new List<Light>();
                var spotLights       = new List<Light>();
                var areaLights       = new List<Light>();
                foreach (var l in lights)
                {
                    switch (l.type)
                    {
                        case LightType.Directional: dirLights.Add(l); break;
                        case LightType.Point:       pointLights.Add(l); break;
                        case LightType.Spot:        spotLights.Add(l); break;
                        default:
                            // LightType.Rectangle / LightType.Disc / Area enum name varies across Unity versions.
                            areaLights.Add(l); break;
                    }
                }

                // ---- probes ----
                #pragma warning disable CS0618
                var lightProbeGroups    = UnityEngine.Object.FindObjectsByType<LightProbeGroup>(FindObjectsSortMode.None);
                var reflectionProbes    = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);
                #pragma warning restore CS0618
                var lightProbeCount = 0;
                foreach (var g in lightProbeGroups)
                    if (g.probePositions != null) lightProbeCount += g.probePositions.Length;

                var bakedReflections = 0;
                var realtimeReflections = 0;
                foreach (var rp in reflectionProbes)
                {
                    if (rp.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime) realtimeReflections++;
                    else bakedReflections++;
                }

                // ---- URP volumes (via reflection to avoid URP package dep) ----
                var urpVolumes = CollectUrpVolumes(scene);

                // ---- warnings ----
                if (dirLights.Count == 0)
                    warnings.Add("No Directional Light in scene — outdoor/ambient-heavy lighting will look flat; sun-dependent shaders will fall back.");
                if (dirLights.Count > 1)
                    warnings.Add($"{dirLights.Count} Directional Lights — usually one is intended; shadow maps stack and ambient contributions compound.");
                if (skybox == null && ambientMode == UnityEngine.Rendering.AmbientMode.Skybox)
                    warnings.Add("Ambient source is Skybox but skybox material is null — ambient will be black scene-wide.");
                if (skybox != null && skybox.shader != null && skybox.shader.name.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
                    warnings.Add($"Skybox material uses '{skybox.shader.name}' — likely a shader-compile failure.");
                if (reflectSrc == UnityEngine.Rendering.DefaultReflectionMode.Skybox && skybox == null)
                    warnings.Add("Reflection source is Skybox but no skybox is assigned — metallic surfaces will reflect a flat/black environment.");
                if (RenderSettings.fog && RenderSettings.fogDensity <= 0f && RenderSettings.fogMode != FogMode.Linear)
                    warnings.Add("Fog enabled but fogDensity is 0 — fog has no visible effect.");
                if (sun == null && dirLights.Count > 0)
                    warnings.Add("RenderSettings.sun is unassigned despite Directional Lights in scene — procedural skyboxes won't rotate with a directional light.");
                if (lightProbeCount == 0 && lights.Count > 0)
                    warnings.Add("No light probes — dynamic (non-static) objects will not receive baked GI contributions; they'll look unlit in baked scenes.");

                return new
                {
                    scene = scene.path,
                    environment = new
                    {
                        skybox_material = skybox != null ? AssetDatabase.GetAssetPath(skybox) : null,
                        skybox_shader   = skybox != null && skybox.shader != null ? skybox.shader.name : null,
                        sun_source      = sun != null ? GameObjectResolver.PathOf(sun.gameObject) : null,
                        ambient_mode    = ambientMode.ToString(),
                        ambient_intensity = RenderSettings.ambientIntensity,
                        ambient_color   = ToColor(RenderSettings.ambientLight),
                        reflection_source = reflectSrc.ToString(),
                        reflection_intensity = RenderSettings.reflectionIntensity,
                        reflection_bounces   = RenderSettings.reflectionBounces
                    },
                    fog = new
                    {
                        enabled = RenderSettings.fog,
                        mode    = RenderSettings.fogMode.ToString(),
                        color   = ToColor(RenderSettings.fogColor),
                        density = RenderSettings.fogDensity,
                        start_distance = RenderSettings.fogStartDistance,
                        end_distance   = RenderSettings.fogEndDistance
                    },
                    lights = LightsDto(lights),
                    light_counts = new
                    {
                        directional = dirLights.Count,
                        point       = pointLights.Count,
                        spot        = spotLights.Count,
                        area        = areaLights.Count,
                        total       = lights.Count
                    },
                    light_probes = new
                    {
                        group_count = lightProbeGroups.Length,
                        probe_count = lightProbeCount
                    },
                    reflection_probes = new
                    {
                        count          = reflectionProbes.Length,
                        baked_count    = bakedReflections,
                        realtime_count = realtimeReflections
                    },
                    lightmap_settings = new
                    {
                        lightmapper  = ReadLightmapper(),
                        realtime_gi  = Lightmapping.realtimeGI,
                        baked_gi     = Lightmapping.bakedGI
                    },
                    post_processing = new
                    {
                        urp_volumes = urpVolumes
                    },
                    warnings    = warnings.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static Scene ResolveScene(string path)
        {
            if (string.IsNullOrEmpty(path)) return SceneManager.GetActiveScene();
            var s = SceneManager.GetSceneByPath(path);
            if (!s.IsValid() || !s.isLoaded)
                throw new InvalidOperationException($"Scene not loaded: {path}");
            return s;
        }

        private static List<Light> CollectLights(Scene scene)
        {
            var result = new List<Light>();
            foreach (var root in scene.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<Light>(includeInactive: true));
            return result;
        }

        private static object[] LightsDto(List<Light> lights)
        {
            var r = new object[lights.Count];
            for (var i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                r[i] = new
                {
                    gameobject_path = GameObjectResolver.PathOf(l.gameObject),
                    instance_id     = GameObjectResolver.InstanceIdOf(l),
                    type            = l.type.ToString(),
                    mode            = l.lightmapBakeType.ToString(),
                    intensity       = l.intensity,
                    range           = l.range,
                    shadows         = l.shadows.ToString(),
                    shadow_strength = l.shadowStrength,
                    color           = ToColor(l.color),
                    culling_mask    = l.cullingMask,
                    render_mode     = l.renderMode.ToString(),
                    active_self     = l.gameObject.activeSelf,
                    active_in_hierarchy = l.gameObject.activeInHierarchy
                };
            }
            return r;
        }

        // URP Volume component lives in UnityEngine.Rendering (core rendering
        // package, always in URP/HDRP projects). We reach it by type-name to
        // keep reify free of a direct URP dependency.
        private static object[] CollectUrpVolumes(Scene scene)
        {
            var volumeType = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime")
                          ?? Type.GetType("UnityEngine.Rendering.Volume, UnityEngine.Rendering");
            if (volumeType == null) return Array.Empty<object>();

            var result = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
            {
                var comps = root.GetComponentsInChildren(volumeType, includeInactive: true);
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    var isGlobal = GetProp<bool>(c, "isGlobal");
                    var priority = GetProp<float>(c, "priority");
                    var weight   = GetProp<float>(c, "weight");
                    var profile  = GetProp<UnityEngine.Object>(c, "sharedProfile")
                                ?? GetProp<UnityEngine.Object>(c, "profile");
                    result.Add(new
                    {
                        gameobject_path = GameObjectResolver.PathOf((c as Component)?.gameObject),
                        is_global       = isGlobal,
                        priority,
                        weight,
                        profile_path    = profile != null ? AssetDatabase.GetAssetPath(profile) : null,
                        profile_name    = profile != null ? profile.name : null
                    });
                }
            }
            return result.ToArray();
        }

        private static T GetProp<T>(object instance, string name)
        {
            if (instance == null) return default;
            var t = instance.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (p != null) try { return (T)p.GetValue(instance); } catch { }
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) try { return (T)f.GetValue(instance); } catch { }
            return default;
        }

        private static object ToColor(Color c) => new { r = c.r, g = c.g, b = c.b, a = c.a };

        // Unity deprecated LightmapEditorSettings.lightmapper in favour of
        // LightingSettings.lightmapper (accessed via Lightmapping.lightingSettings).
        // The new property may not exist on older Unity versions, so we
        // probe reflectively and fall back to the old API with the pragma.
        private static string ReadLightmapper()
        {
            try
            {
                var ls = Lightmapping.lightingSettings;
                if (ls != null) return ls.lightmapper.ToString();
            }
            catch { /* older Unity: property not present */ }
            #pragma warning disable CS0618
            return LightmapEditorSettings.lightmapper.ToString();
            #pragma warning restore CS0618
        }
    }
}
