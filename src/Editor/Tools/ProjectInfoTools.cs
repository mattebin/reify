using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reify.Editor.Tools
{
    internal static class ProjectInfoTools
    {
        // ---------- project-info ----------
        public static Task<object> Info(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var rp = GraphicsSettings.defaultRenderPipeline;
                var rpName = DetectPipeline(rp);
                var target = EditorUserBuildSettings.activeBuildTarget;
                var group  = BuildPipeline.GetBuildTargetGroup(target);

                return new
                {
                    unity_version  = Application.unityVersion,
                    project_path   = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/'),
                    project_name   = new DirectoryInfo(
                                        Path.GetDirectoryName(Application.dataPath) ?? ".").Name,
                    product_name   = PlayerSettings.productName,
                    company_name   = PlayerSettings.companyName,
                    render_pipeline = new
                    {
                        detected     = rpName,
                        asset_type   = rp != null ? rp.GetType().FullName : null,
                        asset_path   = rp != null ? AssetDatabase.GetAssetPath(rp) : null,
                        asset_name   = rp != null ? rp.name : null
                    },
                    scripting_backend = PlayerSettings.GetScriptingBackend(
                                            UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group)).ToString(),
                    api_compatibility_level = PlayerSettings.GetApiCompatibilityLevel(
                                            UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group)).ToString(),
                    build_target            = target.ToString(),
                    build_target_group      = group.ToString(),
                    color_space             = PlayerSettings.colorSpace.ToString(),
                    graphics_apis           = ToStrings(PlayerSettings.GetGraphicsAPIs(target)),
                    is_play_mode   = EditorApplication.isPlayingOrWillChangePlaymode,
                    is_compiling   = EditorApplication.isCompiling,
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- project-packages ----------
        public static Task<object> Packages(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                var list = new List<object>(packages.Length);
                foreach (var p in packages)
                {
                    list.Add(new
                    {
                        name             = p.name,
                        display_name     = p.displayName,
                        version          = p.version,
                        source           = p.source.ToString(),
                        is_direct_dep    = p.isDirectDependency,
                        resolved_path    = p.resolvedPath,
                        dependencies     = p.dependencies != null
                            ? DependenciesToArray(p.dependencies) : Array.Empty<object>()
                    });
                }
                return new
                {
                    package_count = list.Count,
                    packages      = list.ToArray(),
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        // ---------- project-build-settings ----------
        public static Task<object> BuildSettings(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var scenes = EditorBuildSettings.scenes;
                var list = new List<object>(scenes.Length);
                for (var i = 0; i < scenes.Length; i++)
                {
                    var s = scenes[i];
                    list.Add(new
                    {
                        index   = i,
                        path    = s.path,
                        enabled = s.enabled,
                        guid    = s.guid.ToString()
                    });
                }
                return new
                {
                    scenes_in_build     = list.ToArray(),
                    enabled_scene_count = CountEnabled(scenes),
                    active_build_target = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    development_build   = EditorUserBuildSettings.development,
                    allow_debugging     = EditorUserBuildSettings.allowDebugging,
                    connect_profiler    = EditorUserBuildSettings.connectProfiler,
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }

        // ---------- project-layers-tags ----------
        public static Task<object> LayersTags(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var layers = new List<object>(32);
                for (var i = 0; i < 32; i++)
                {
                    var name = LayerMask.LayerToName(i);
                    layers.Add(new
                    {
                        index = i,
                        name  = name,
                        is_builtin = i < 8,
                        is_unused  = string.IsNullOrEmpty(name) && i >= 8
                    });
                }

                var tags = UnityEditorInternal.InternalEditorUtility.tags;
                var tagList = new List<object>(tags.Length);
                var seen = new HashSet<string>();
                var duplicates = new List<string>();
                foreach (var t in tags)
                {
                    if (!seen.Add(t)) duplicates.Add(t);
                    tagList.Add(new { name = t });
                }

                var warnings = new List<string>();
                if (duplicates.Count > 0)
                    warnings.Add($"Duplicate tag(s) in Tags & Layers: {string.Join(", ", duplicates)}.");

                return new
                {
                    layers   = layers.ToArray(),
                    tags     = tagList.ToArray(),
                    layer_count = 32,
                    tag_count   = tags.Length,
                    warnings = warnings.ToArray(),
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- project-render-pipeline-state (philosophy tool) ----------
        public static Task<object> RenderPipelineState(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var rp = GraphicsSettings.defaultRenderPipeline;
                var warnings = new List<string>();
                var pipeline = DetectPipeline(rp);

                if (rp == null)
                    warnings.Add("GraphicsSettings.defaultRenderPipeline is null — project is using the Built-in Render Pipeline. reify's render-pipeline-state is URP-focused; Built-in diagnostics are limited.");

                object urp = null;
                if (pipeline == "URP" && rp != null)
                    urp = BuildUrpDiagnostic(rp, warnings);

                return new
                {
                    pipeline,
                    pipeline_asset = rp != null ? new
                    {
                        type_fqn   = rp.GetType().FullName,
                        asset_path = AssetDatabase.GetAssetPath(rp),
                        asset_name = rp.name
                    } : null,
                    urp,
                    warnings     = warnings.ToArray(),
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- URP reflection ----------
        // We read URP fields reflectively so reify does not take a
        // com.unity.render-pipelines.universal dependency. On non-URP
        // projects the asset types simply won't be present and we return
        // a skeleton diagnostic.
        private static object BuildUrpDiagnostic(RenderPipelineAsset rp, List<string> warnings)
        {
            var t = rp.GetType();
            int  msaa           = Get<int>(t, rp, "msaaSampleCount");
            bool hdr            = Get<bool>(t, rp, "supportsHDR");
            float renderScale   = Get<float>(t, rp, "renderScale");
            float shadowDistance = Get<float>(t, rp, "shadowDistance");
            int  shadowCascades  = Get<int>(t, rp, "shadowCascadeCount");
            bool softShadows     = Get<bool>(t, rp, "supportsSoftShadows");
            int  opaqueMask      = Get<int>(t, rp, "opaqueLayerMask", defaultValue: -1);
            int  transparentMask = Get<int>(t, rp, "transparentLayerMask", defaultValue: -1);

            // Warnings — the philosophy layer.
            if (msaa == 1)
                warnings.Add("MSAA sample count is 1 (disabled). Edges will alias unless post-process FXAA/SMAA is active.");
            if (!hdr)
                warnings.Add("HDR disabled on URP asset — bloom, tonemapping, and emissive materials will clip at 1.0.");
            if (renderScale < 0.9f || renderScale > 1.1f)
                warnings.Add($"Render scale is {renderScale:F2} — not 1.0. Game View may look soft or expensive without an obvious cause.");
            if (shadowDistance <= 0f)
                warnings.Add($"Shadow distance is {shadowDistance:F1} — shadows are effectively disabled scene-wide.");
            if (shadowCascades > 1 && shadowDistance < 10f)
                warnings.Add($"Shadow cascades > 1 ({shadowCascades}) but shadow distance is only {shadowDistance:F1}m — cascades wasted.");
            if (opaqueMask == 0)
                warnings.Add("opaqueLayerMask is 0 — nothing in the opaque pass will render. Likely a configuration mistake.");
            if (transparentMask == 0)
                warnings.Add("transparentLayerMask is 0 — nothing in the transparent pass will render.");

            return new
            {
                msaa_sample_count  = msaa,
                supports_hdr       = hdr,
                render_scale       = renderScale,
                shadow_distance    = shadowDistance,
                shadow_cascade_count = shadowCascades,
                supports_soft_shadows = softShadows,
                opaque_layer_mask      = opaqueMask,
                transparent_layer_mask = transparentMask,
                note = "URP asset read via reflection to avoid a package dependency. Fields absent in your URP version return defaults."
            };
        }

        private static string DetectPipeline(RenderPipelineAsset rp)
        {
            if (rp == null) return "Built-in";
            var n = rp.GetType().FullName ?? "";
            if (n.Contains("Universal")) return "URP";
            if (n.Contains("HighDefinition") || n.Contains("HDRP")) return "HDRP";
            return "custom";
        }

        private static T Get<T>(Type t, object instance, string propertyOrField, T defaultValue = default)
        {
            var p = t.GetProperty(propertyOrField, BindingFlags.Instance | BindingFlags.Public);
            if (p != null) try { return (T)Convert.ChangeType(p.GetValue(instance), typeof(T)); } catch { }
            var f = t.GetField(propertyOrField, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) try { return (T)Convert.ChangeType(f.GetValue(instance), typeof(T)); } catch { }
            return defaultValue;
        }

        private static string[] ToStrings<T>(T[] arr)
        {
            if (arr == null) return Array.Empty<string>();
            var r = new string[arr.Length];
            for (var i = 0; i < arr.Length; i++) r[i] = arr[i].ToString();
            return r;
        }

        private static object[] DependenciesToArray(IEnumerable<UnityEditor.PackageManager.DependencyInfo> deps)
        {
            var list = new List<object>();
            foreach (var d in deps)
                list.Add(new { name = d.name, version = d.version });
            return list.ToArray();
        }

        private static int CountEnabled(EditorBuildSettingsScene[] scenes)
        {
            var n = 0;
            foreach (var s in scenes) if (s.enabled) n++;
            return n;
        }
    }
}
