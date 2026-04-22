using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class UIToolkitServerTools
{
    [McpServerTool(Name = "ui-toolkit-document-inspect"), Description(
        "Inspect a UIDocument (UI Toolkit) host: enabled, sort_order, " +
        "panel_settings + visualTreeAsset asset paths, root element name, " +
        "child_count + descendant_count. Warnings for disabled, missing " +
        "UXML, missing panelSettings. Resolve by instance_id or " +
        "gameobject_path.")]
    public static async Task<JsonElement> UIToolkitDocumentInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("ui-toolkit-document-inspect",
        new { instance_id, gameobject_path }, ct);

    [McpServerTool(Name = "ui-toolkit-element-tree"), Description(
        "Walk the live VisualElement tree under a UIDocument. Returns a " +
        "flattened depth-first list of {path, depth, type_fqn, name, " +
        "classes[], child_count, enabled, picking_mode, layout}. Set " +
        "include_styles=true to also emit resolved display/visibility/" +
        "opacity/width/height/color. Paginated by `limit` (default 500).")]
    public static async Task<JsonElement> UIToolkitElementTree(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        int? limit = null,
        bool? include_styles = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("ui-toolkit-element-tree",
        new { instance_id, gameobject_path, limit, include_styles }, ct);

    [McpServerTool(Name = "ui-toolkit-uxml-inspect"), Description(
        "Inspect a .uxml asset (VisualTreeAsset). Instantiates a temporary " +
        "copy to report the resulting child_count and total element count. " +
        "Lists forward AssetDatabase dependencies (other UXML/USS/sprite " +
        "assets referenced by this template).")]
    public static async Task<JsonElement> UIToolkitUxmlInspect(
        UnityClient unity,
        string asset_path,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("ui-toolkit-uxml-inspect",
        new { asset_path }, ct);

    [McpServerTool(Name = "ui-toolkit-uss-inspect"), Description(
        "Inspect a .uss asset (StyleSheet). Returns name + compiler_version " +
        "marker + forward dependencies. Cheaper than instantiating the " +
        "sheet against a live document.")]
    public static async Task<JsonElement> UIToolkitUssInspect(
        UnityClient unity,
        string asset_path,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("ui-toolkit-uss-inspect",
        new { asset_path }, ct);
}
