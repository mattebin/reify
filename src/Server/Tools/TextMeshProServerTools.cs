using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class TextMeshProServerTools
{
    [McpServerTool(Name = "tmp-text-inspect"), Description(
        "Inspect a TMP_Text-derived component (TextMeshProUGUI, TextMeshPro " +
        "3D, or any subclass) via reflection. Returns text, font_size, " +
        "auto-sizing config, color, alignment, rich_text, raycast_target, " +
        "outline, font asset path, material asset path. Package-gated on " +
        "com.unity.textmeshpro.")]
    public static async Task<JsonElement> TmpTextInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("tmp-text-inspect",
        new { instance_id, gameobject_path }, ct);

    [McpServerTool(Name = "tmp-text-set"), Description(
        "Modify a TMP_Text component. Any combination of: text, font_size, " +
        "color {r,g,b,a}, alignment (TMPro.TextAlignmentOptions name), " +
        "rich_text, raycast_target. Undo-backed. Structured error when a " +
        "value is invalid.")]
    public static async Task<JsonElement> TmpTextSet(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        string? text = null,
        float? font_size = null,
        JsonElement? color = null,
        string? alignment = null,
        bool? rich_text = null,
        bool? raycast_target = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("tmp-text-set", new
    {
        instance_id, gameobject_path, text, font_size, color, alignment,
        rich_text, raycast_target
    }, ct);

    [McpServerTool(Name = "tmp-font-asset-inspect"), Description(
        "Inspect a TMP_FontAsset by asset_path. Returns source_font_path, " +
        "atlas width/height/padding, face family/style/pointSize, " +
        "character_count, glyph_count. Reflection-based; package-gated.")]
    public static async Task<JsonElement> TmpFontAssetInspect(
        UnityClient unity,
        string asset_path,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("tmp-font-asset-inspect",
        new { asset_path }, ct);
}
