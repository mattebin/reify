using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class SpatialEvidenceServerTools
{
    [McpServerTool(Name = "spatial-primitive-evidence"), Description(
        "Return structured spatial evidence for a mesh-bearing GameObject: local/world bounds, " +
        "estimated primitive kind, and useful world-space anchors such as center, top, bottom, " +
        "left, right, top_left, and top_right. Use this before making claims about size, contact, " +
        "or alignment.")]
    public static async Task<JsonElement> SpatialPrimitiveEvidence(
        UnityClient unity,
        [Description("Scene path of the GameObject to inspect.")]
        string gameobject_path,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "spatial-primitive-evidence",
        new { gameobject_path },
        ct);

    [McpServerTool(Name = "spatial-anchor-distance"), Description(
        "Measure the world-space distance between two named anchors on two GameObjects. Anchors " +
        "support center, top, bottom, left, right, front, back, and combinations like top_left, " +
        "top_right, bottom_left, or bottom_right. Returns the two anchor points, delta vector, " +
        "distance in meters, and whether the distance is within tolerance.")]
    public static async Task<JsonElement> SpatialAnchorDistance(
        UnityClient unity,
        [Description("Scene path of the first GameObject.")]
        string a_path,
        [Description("Anchor on the first GameObject.")]
        string a_anchor,
        [Description("Scene path of the second GameObject.")]
        string b_path,
        [Description("Anchor on the second GameObject.")]
        string b_anchor,
        [Description("Distance threshold for 'within_tolerance'. Default 0.025 meters.")]
        float? tolerance,
        CancellationToken ct
    ) => await unity.CallAsync<JsonElement>(
        "spatial-anchor-distance",
        new { a_path, a_anchor, b_path, b_anchor, tolerance },
        ct);
}
