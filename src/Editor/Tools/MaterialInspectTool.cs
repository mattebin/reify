using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Second Phase C philosophy tool. Returns a full structured description
    /// of what actually renders — asset-material properties, shader keywords,
    /// render queue, AND any MaterialPropertyBlock overrides on the resolving
    /// renderer. Override-source tracking is the killer feature: without it,
    /// an LLM can't tell why the material's colour in the Inspector doesn't
    /// match what's on screen.
    /// </summary>
    internal static class MaterialInspectTool
    {
        [ReifyTool("material-inspect")]
        public static Task<object> Handle(JToken args)
        {
            var assetPath  = args?.Value<string>("asset_path");
            var rendererId = args?["renderer_instance_id"]?.Type == JTokenType.Integer
                ? args.Value<int?>("renderer_instance_id") : null;
            var goPath     = args?.Value<string>("gameobject_path");
            var submeshIdx = args?["submesh_index"]?.Type == JTokenType.Integer
                ? args.Value<int>("submesh_index") : 0;

            var sourceCount = 0;
            if (!string.IsNullOrEmpty(assetPath)) sourceCount++;
            if (rendererId.HasValue)              sourceCount++;
            if (!string.IsNullOrEmpty(goPath))    sourceCount++;
            if (sourceCount != 1)
                throw new ArgumentException(
                    "Exactly one of asset_path, renderer_instance_id, or gameobject_path must be provided.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!string.IsNullOrEmpty(assetPath))
                    return InspectAssetMaterial(assetPath);

                Renderer renderer;
                if (rendererId.HasValue)
                {
                    renderer = GameObjectResolver.ByInstanceId(rendererId.Value) as Renderer
                        ?? throw new InvalidOperationException(
                            $"renderer_instance_id {rendererId} does not resolve to a Renderer.");
                }
                else
                {
                    var go = GameObjectResolver.ByPath(goPath)
                        ?? throw new InvalidOperationException($"GameObject not found: {goPath}");
                    renderer = go.GetComponent<Renderer>()
                        ?? throw new InvalidOperationException($"GameObject '{goPath}' has no Renderer component.");
                }

                return InspectRenderer(renderer, submeshIdx);
            });
        }

        // ---------- asset-mode inspection ----------
        private static object InspectAssetMaterial(string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path)
                ?? throw new InvalidOperationException($"Material asset not found: {path}");
            return BuildResponse(mat, path, renderer: null, submeshIndex: 0);
        }

        // ---------- renderer-mode inspection ----------
        private static object InspectRenderer(Renderer renderer, int submeshIndex)
        {
            var materials = renderer.sharedMaterials;
            if (submeshIndex < 0 || submeshIndex >= materials.Length)
                throw new ArgumentException(
                    $"submesh_index {submeshIndex} out of range [0..{materials.Length - 1}] for " +
                    $"renderer on '{renderer.gameObject.name}'.");

            var mat = materials[submeshIndex];
            if (mat == null)
                throw new InvalidOperationException(
                    $"Renderer on '{renderer.gameObject.name}' has no material at submesh index {submeshIndex}.");

            var assetPath = AssetDatabase.GetAssetPath(mat);
            return BuildResponse(mat, assetPath, renderer, submeshIndex);
        }

        // ---------- core ----------
        private static object BuildResponse(Material mat, string assetPath, Renderer renderer, int submeshIndex)
        {
            var warnings = new List<string>();
            var shader   = mat.shader;

            // Shader keywords (enabled set).
            var enabledKeywords = new List<string>();
            foreach (var k in mat.shaderKeywords)
                if (!string.IsNullOrEmpty(k)) enabledKeywords.Add(k);

            // Asset-backed property table.
            var properties = new Dictionary<string, object>();
            var shaderPropCount = shader.GetPropertyCount();
            for (var i = 0; i < shaderPropCount; i++)
            {
                var name = shader.GetPropertyName(i);
                var type = shader.GetPropertyType(i);
                properties[name] = BuildPropValue(mat, name, type, "asset");
            }

            // MPB overrides (renderer mode only).
            Dictionary<string, object> mpbOverrides = null;
            bool hasMpb = false;
            bool renderersShareMaterial = false;
            string rendererInstancedMode = null;

            if (renderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(mpb, submeshIndex);
                hasMpb = !mpb.isEmpty;
                mpbOverrides = new Dictionary<string, object>();

                if (hasMpb)
                {
                    // MPB's API exposes typed readers; we probe each shader
                    // property and flag the ones MPB actually overrides.
                    for (var i = 0; i < shaderPropCount; i++)
                    {
                        var name = shader.GetPropertyName(i);
                        var type = shader.GetPropertyType(i);
                        var over = TryReadMpb(mpb, name, type);
                        if (over != null)
                            mpbOverrides[name] = new
                            {
                                type   = type.ToString(),
                                value  = over,
                                source = "material_property_block",
                                note   = "Overridden at runtime via MaterialPropertyBlock — NOT serialized to the asset; Inspector may differ from what's on screen."
                            };
                    }
                    if (mpbOverrides.Count > 0)
                        warnings.Add(
                            $"Renderer has {mpbOverrides.Count} MaterialPropertyBlock override(s). " +
                            "These bypass the material asset and are not persisted; saving/reloading the scene will NOT keep them.");
                }

                // Shared-material check: other renderers pointing at the same mat.
                // Unity 6 deprecated the FindObjectsSortMode overload, but the
                // replacement (FindObjectsByType<T>()) doesn't exist on the
                // 2021.3 floor we target in package.json.
                #pragma warning disable CS0618
                var allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
                #pragma warning restore CS0618
                var sharers = 0;
                foreach (var r in allRenderers)
                {
                    if (r == renderer) continue;
                    foreach (var rm in r.sharedMaterials)
                        if (rm == mat) { sharers++; break; }
                }
                renderersShareMaterial = sharers > 0;
                if (renderersShareMaterial)
                    warnings.Add(
                        $"Material is shared with {sharers} other renderer(s). " +
                        "Modifying the asset affects all of them. Use MaterialPropertyBlock or Material.Instantiate to isolate.");
                rendererInstancedMode = renderer.sharedMaterial == renderer.sharedMaterials[submeshIndex]
                    ? "shared" : "per-submesh";
            }

            // Philosophy warnings: keyword / property consistency.
            if (enabledKeywords.Contains("_EMISSION"))
            {
                var emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : default;
                if (emissionColor.maxColorComponent < 0.01f)
                    warnings.Add("_EMISSION keyword is enabled but _EmissionColor is effectively black — emission is doing nothing.");
            }
            if (mat.HasProperty("_BaseColor"))
            {
                var bc = mat.GetColor("_BaseColor");
                var rq = mat.renderQueue;
                if (bc.a < 1f && rq < 3000)
                    warnings.Add(
                        $"_BaseColor alpha is {bc.a:F2} but renderQueue is {rq} (Opaque range). " +
                        "Transparent material on an opaque queue → sorting bugs. Move to queue 3000+ (Transparent).");
            }
            if (shader.name.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shader.name.IndexOf("Hidden/InternalErrorShader", StringComparison.Ordinal) >= 0)
                warnings.Add($"Shader is '{shader.name}' — this usually means the intended shader failed to compile or resolve.");

            // Effective merged view.
            var effective = new Dictionary<string, object>();
            foreach (var kv in properties) effective[kv.Key] = kv.Value;
            if (mpbOverrides != null)
                foreach (var kv in mpbOverrides) effective[kv.Key] = kv.Value;

            var sourceType =
                renderer != null ? "renderer" :
                !string.IsNullOrEmpty(assetPath) ? "asset" : "unknown";
            var sourceId =
                renderer != null ? renderer.gameObject.name + "[" + submeshIndex + "]" :
                !string.IsNullOrEmpty(assetPath) ? assetPath : "";

            return new
            {
                source = new { type = sourceType, identifier = sourceId },
                material_name = mat.name,
                asset_path    = assetPath,
                instance_id   = GameObjectResolver.InstanceIdOf(mat),
                shader = new
                {
                    name             = shader.name,
                    render_pipeline  = DetectRenderPipeline(shader.name),
                    keywords_enabled = enabledKeywords.ToArray(),
                    keyword_count    = enabledKeywords.Count,
                    render_queue     = mat.renderQueue,
                    global_il_mask   = mat.globalIlluminationFlags.ToString(),
                    pass_count       = mat.passCount
                },
                properties     = properties,
                mpb_overrides  = mpbOverrides,
                effective_properties = effective,
                renderer_context = renderer != null ? new
                {
                    renderer_type      = renderer.GetType().FullName,
                    renderer_instance_id = GameObjectResolver.InstanceIdOf(renderer),
                    gameobject_path    = GameObjectResolver.PathOf(renderer.gameObject),
                    gameobject_qualified_path = GameObjectResolver.QualifiedPathOf(renderer.gameObject),
                    scene_name         = renderer.gameObject.scene.name,
                    scene_path         = renderer.gameObject.scene.path,
                    submesh_index      = submeshIndex,
                    shared_with_others = renderersShareMaterial,
                    mode               = rendererInstancedMode
                } : null,
                has_mpb       = hasMpb,
                warnings      = warnings.ToArray(),
                read_at_utc   = DateTime.UtcNow.ToString("o"),
                frame         = (long)Time.frameCount
            };
        }

        // ---------- value readers ----------
        private static object BuildPropValue(Material m, string name, ShaderPropertyType type, string source)
        {
            object value;
            switch (type)
            {
                case ShaderPropertyType.Color:  value = ColorToObj(m.GetColor(name)); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:  value = m.GetFloat(name); break;
                case ShaderPropertyType.Vector: value = Vec4ToObj(m.GetVector(name)); break;
                case ShaderPropertyType.Texture:
                {
                    var tex = m.GetTexture(name);
                    value = tex == null ? null : new
                    {
                        type_fqn    = tex.GetType().FullName,
                        name        = tex.name,
                        instance_id = GameObjectResolver.InstanceIdOf(tex),
                        asset_path  = AssetDatabase.GetAssetPath(tex)
                    };
                    break;
                }
                case ShaderPropertyType.Int:    value = m.GetInt(name); break;
                default:                        value = "<unsupported>"; break;
            }
            return new { type = type.ToString(), value, source };
        }

        private static object TryReadMpb(MaterialPropertyBlock mpb, string name, ShaderPropertyType type)
        {
            // MPB doesn't expose a "contains" query, so we read and compare
            // against sentinel. The ID-by-name lookups are cheap.
            var id = Shader.PropertyToID(name);
            switch (type)
            {
                case ShaderPropertyType.Color:
                {
                    var v = mpb.GetVector(id);
                    if (v == Vector4.zero) return null;
                    return ColorToObj(new Color(v.x, v.y, v.z, v.w));
                }
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                {
                    var f = mpb.GetFloat(id);
                    if (f == 0f) return null;
                    return f;
                }
                case ShaderPropertyType.Vector:
                {
                    var v = mpb.GetVector(id);
                    if (v == Vector4.zero) return null;
                    return Vec4ToObj(v);
                }
                case ShaderPropertyType.Texture:
                {
                    var tex = mpb.GetTexture(id);
                    if (tex == null) return null;
                    return new
                    {
                        type_fqn    = tex.GetType().FullName,
                        name        = tex.name,
                        instance_id = GameObjectResolver.InstanceIdOf(tex),
                        asset_path  = AssetDatabase.GetAssetPath(tex)
                    };
                }
                case ShaderPropertyType.Int:
                {
                    var i = mpb.GetInt(id);
                    if (i == 0) return null;
                    return i;
                }
                default: return null;
            }
        }

        private static object ColorToObj(Color c) => new { r = c.r, g = c.g, b = c.b, a = c.a };
        private static object Vec4ToObj(Vector4 v) => new { x = v.x, y = v.y, z = v.z, w = v.w };

        private static string DetectRenderPipeline(string shaderName)
        {
            if (shaderName.StartsWith("Universal Render Pipeline", StringComparison.Ordinal)) return "URP";
            if (shaderName.StartsWith("HDRP",                       StringComparison.Ordinal)) return "HDRP";
            if (shaderName.StartsWith("Hidden/",                    StringComparison.Ordinal)) return "hidden";
            return "built-in-or-custom";
        }
    }
}
