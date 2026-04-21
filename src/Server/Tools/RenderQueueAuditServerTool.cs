using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class RenderQueueAuditServerTool
{
    [McpServerTool(Name = "render-queue-audit"), Description(
        "6th Phase C philosophy tool. Scene-wide render-queue audit. Lists " +
        "every Renderer with its material, shader, render_queue, Unity " +
        "bucket (Background/Geometry/AlphaTest/Transparent/Overlay), sorting " +
        "layer + order, and world bounds. Sorted by queue ascending. " +
        "\n\n" +
        "Warnings flag: transparent-renderer bounds overlap (camera-dependent " +
        "sort swaps), SpriteRenderer pileup at identical (sorting_layer, " +
        "sorting_order), renderers with missing materials. Optional filter " +
        "by queue range or renderer_type (MeshRenderer/SkinnedMeshRenderer/" +
        "SpriteRenderer/ParticleSystemRenderer). " +
        "\n\n" +
        "This is the tool that answers 'why is my transparent object " +
        "rendering in the wrong order' in one call — no screenshots."
    )]
    public static async Task<JsonElement> RenderQueueAudit(UnityClient unity,
        [Description("Scene asset path. Omit for active scene.")]
        string? scene_path,
        [Description("Include disabled GameObjects / renderers. Default false.")]
        bool? include_inactive,
        [Description("Optional filter: {queue_min, queue_max, renderer_type}.")]
        RenderQueueAuditFilter? filter,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("render-queue-audit",
        new RenderQueueAuditArgs(scene_path, include_inactive, filter), ct);
}
