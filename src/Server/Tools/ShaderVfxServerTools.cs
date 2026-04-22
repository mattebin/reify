using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class ShaderVfxServerTools
{
    [McpServerTool(Name = "shader-inspect"), Description(
        "Inspect a Shader asset — the shader itself, not a Material " +
        "instance. Pass shader_name ('Universal Render Pipeline/Lit') OR " +
        "asset_path to a .shader file. Returns is_supported (false means " +
        "the shader will fall back pink on this platform — silent failure " +
        "source), render_queue, pass/subshader count, every property " +
        "(name/description/type/hidden/texture dim/range limits), and " +
        "every keyword (global + local with type + overridability). " +
        "Complements material-inspect which covers the instance side.")]
    public static async Task<JsonElement> ShaderInspect(
        UnityClient unity,
        string? shader_name = null,
        string? asset_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("shader-inspect", new
    {
        shader_name, asset_path
    }, ct);

    [McpServerTool(Name = "shader-graph-inspect"), Description(
        "Inspect a Shader Graph asset (.shadergraph or .shadersubgraph). " +
        "Returns importer_type, is_sub_graph, and the compiled Shader " +
        "summary (name, pass_count, property_count, render_queue). " +
        "Package-gated — returns a structured error if " +
        "com.unity.shadergraph isn't installed.")]
    public static async Task<JsonElement> ShaderGraphInspect(
        UnityClient unity,
        string asset_path,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("shader-graph-inspect", new
    {
        asset_path
    }, ct);

    [McpServerTool(Name = "visual-effect-inspect"), Description(
        "Inspect a VisualEffect component (VFX Graph runtime). Returns " +
        "asset_path + guid of the bound VisualEffectAsset, " +
        "alive_particle_count, start_seed, pause, play_rate, culled, and " +
        "every exposed_property (name + type). Package-gated — returns a " +
        "structured error if com.unity.visualeffectgraph isn't installed. " +
        "Resolve by instance_id OR gameobject_path.")]
    public static async Task<JsonElement> VisualEffectInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("visual-effect-inspect", new
    {
        instance_id, gameobject_path
    }, ct);

    [McpServerTool(Name = "visual-effect-asset-inspect"), Description(
        "Inspect a VisualEffectAsset (.vfx file) at rest, without needing " +
        "a scene instance. Returns name, importer_type, guid. " +
        "Package-gated.")]
    public static async Task<JsonElement> VisualEffectAssetInspect(
        UnityClient unity,
        string asset_path,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("visual-effect-asset-inspect", new
    {
        asset_path
    }, ct);
}
