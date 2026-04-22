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
    /// Shader read-only surface. Covers built-in Unity shaders (always
    /// available) via ShaderUtil, plus Shader Graph assets (.shadergraph)
    /// via AssetImporter reflection when com.unity.shadergraph is installed.
    ///
    /// The existing material-inspect tool covers per-material property
    /// values; this tool covers the shader itself — property declarations,
    /// keywords, subshader/pass count, render queue, platform support.
    /// </summary>
    internal static class ShaderTools
    {
        // ---------- shader-inspect ----------
        [ReifyTool("shader-inspect")]
        public static Task<object> Inspect(JToken args)
        {
            var byName = args?.Value<string>("shader_name");
            var byPath = args?.Value<string>("asset_path");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                Shader shader = null;
                string resolvedPath = null;

                if (!string.IsNullOrEmpty(byPath))
                {
                    shader = AssetDatabase.LoadAssetAtPath<Shader>(byPath)
                        ?? throw new InvalidOperationException($"No Shader at asset_path '{byPath}'.");
                    resolvedPath = byPath;
                }
                else if (!string.IsNullOrEmpty(byName))
                {
                    shader = Shader.Find(byName)
                        ?? throw new InvalidOperationException(
                            $"Shader.Find('{byName}') returned null. Exact name required, e.g. " +
                            "'Universal Render Pipeline/Lit'.");
                    resolvedPath = AssetDatabase.GetAssetPath(shader);
                }
                else
                {
                    throw new ArgumentException("Provide either shader_name or asset_path.");
                }

                var warnings = new List<string>();
                if (!shader.isSupported) warnings.Add("shader.isSupported == false on this platform.");

                // Property enumeration via ShaderUtil (editor-only).
                var propCount = ShaderUtil.GetPropertyCount(shader);
                var props = new List<object>(propCount);
                for (var i = 0; i < propCount; i++)
                {
                    props.Add(new
                    {
                        index        = i,
                        name         = ShaderUtil.GetPropertyName(shader, i),
                        description  = ShaderUtil.GetPropertyDescription(shader, i),
                        type         = ShaderUtil.GetPropertyType(shader, i).ToString(),
                        is_hidden    = ShaderUtil.IsShaderPropertyHidden(shader, i),
                        texture_dim  = ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv
                            ? ShaderUtil.GetTexDim(shader, i).ToString() : null,
                        range_limits = ReadRangeLimits(shader, i)
                    });
                }

                // Global + local keywords (Unity 2021.2+). Wrapped because
                // LocalKeyword fields shifted between 2021.2 → 2023.
                var keywords = new List<object>();
                try
                {
                    foreach (var kw in shader.keywordSpace.keywords)
                    {
                        keywords.Add(new
                        {
                            name = kw.name,
                            type = kw.type.ToString(),
                            is_override = kw.isOverridable
                        });
                    }
                }
                catch (Exception e)
                {
                    warnings.Add("keyword enumeration unavailable on this Unity build: " + e.Message);
                }

                return new
                {
                    shader_name     = shader.name,
                    asset_path      = resolvedPath,
                    guid            = !string.IsNullOrEmpty(resolvedPath)
                                        ? AssetDatabase.AssetPathToGUID(resolvedPath) : null,
                    is_supported    = shader.isSupported,
                    render_queue    = shader.renderQueue,
                    maximum_lod     = shader.maximumLOD,
                    pass_count      = shader.passCount,
                    subshader_count = shader.subshaderCount,
                    property_count  = propCount,
                    properties      = props.ToArray(),
                    keyword_count   = keywords.Count,
                    keywords        = keywords.ToArray(),
                    warnings        = warnings.ToArray(),
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        private static object ReadRangeLimits(Shader shader, int i)
        {
            if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.Range)
                return null;
            try
            {
                return new
                {
                    def = ShaderUtil.GetRangeLimits(shader, i, 0),
                    min = ShaderUtil.GetRangeLimits(shader, i, 1),
                    max = ShaderUtil.GetRangeLimits(shader, i, 2)
                };
            }
            catch { return null; }
        }

        // ---------- shader-graph-inspect ----------
        // Package-gated. When com.unity.shadergraph is installed, we read
        // the .shadergraph asset's JSON via AssetDatabase.LoadAssetAtPath
        // and the sidecar ShaderGraphImporter. Without the package, the
        // .shadergraph importer falls back and shader is null — we surface
        // a structured error.
        [ReifyTool("shader-graph-inspect")]
        public static Task<object> ShaderGraphInspect(JToken args)
        {
            var path = args?.Value<string>("asset_path")
                ?? throw new ArgumentException("asset_path is required (a .shadergraph file).");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (!path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        "shader-graph-inspect expects a .shadergraph or .shadersubgraph asset.");

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                var importer = AssetImporter.GetAtPath(path);
                if (importer == null)
                    throw new InvalidOperationException(
                        $"No AssetImporter for '{path}' — file missing or package not installed.");

                var importerType = importer.GetType();
                // ShaderGraphImporter lives in Unity.ShaderGraph.Editor when the package is installed.
                var isShaderGraphImporter = importerType.FullName == "UnityEditor.ShaderGraph.ShaderGraphImporter"
                    || importerType.FullName == "UnityEditor.ShaderGraph.ShaderSubGraphImporter";

                if (!isShaderGraphImporter)
                    throw new InvalidOperationException(
                        $"Importer for '{path}' is {importerType.FullName}, not a ShaderGraph importer. " +
                        "Install com.unity.shadergraph or confirm the asset.");

                var isSubGraph = importerType.FullName == "UnityEditor.ShaderGraph.ShaderSubGraphImporter";

                object shaderBlock = null;
                if (shader != null)
                {
                    shaderBlock = new
                    {
                        name           = shader.name,
                        is_supported   = shader.isSupported,
                        render_queue   = shader.renderQueue,
                        pass_count     = shader.passCount,
                        property_count = ShaderUtil.GetPropertyCount(shader)
                    };
                }

                return new
                {
                    asset_path      = path,
                    guid            = AssetDatabase.AssetPathToGUID(path),
                    importer_type   = importerType.FullName,
                    is_sub_graph    = isSubGraph,
                    compiled_shader = shaderBlock,
                    note            = shader == null
                        ? "Compiled Shader asset not yet available — reimport the .shadergraph first."
                        : null,
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }
    }
}
