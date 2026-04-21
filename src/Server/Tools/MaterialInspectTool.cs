using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class MaterialInspectTool
{
    [McpServerTool(Name = "material-inspect"), Description(
        "Return the full structured state of a Material — shader, keywords, " +
        "render queue, every property with its typed value AND source " +
        "('asset' vs 'material_property_block'). Resolve by exactly one of: " +
        "asset_path, renderer_instance_id, or gameobject_path (+ optional " +
        "submesh_index for multi-material renderers)." +
        "\n\n" +
        "Override-source tracking is the killer feature: when a Renderer uses " +
        "a MaterialPropertyBlock, the values you see on-screen are NOT what " +
        "the asset stores. Reading the asset is a lie. This tool exposes both " +
        "layers and the effective merged view. " +
        "\n\n" +
        "Warnings flag: _EMISSION enabled with black _EmissionColor, " +
        "transparent alpha on opaque render queue, Unity error-pink shaders, " +
        "shared-material side effects across multiple renderers, and " +
        "unpersisted MPB overrides."
    )]
    public static async Task<JsonElement> MaterialInspect(
        UnityClient unity,
        [Description("Inspect a material asset directly (no renderer context).")]
        string? asset_path,
        [Description("Inspect the material used by this Renderer. Takes precedence if both are given.")]
        int? renderer_instance_id,
        [Description("Inspect the material used by the Renderer on this GameObject.")]
        string? gameobject_path,
        [Description("Submesh / materials-array index when the renderer has multiple materials. Default 0.")]
        int? submesh_index,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "material-inspect",
        new MaterialInspectArgs(asset_path, renderer_instance_id, gameobject_path, submesh_index),
        ct);
}
