using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class UIServerTools
{
    [McpServerTool(Name = "ui-canvas-inspect"), Description(
        "Inspect a Canvas: render_mode, sorting_layer/order, world_camera, " +
        "pixel_perfect, scale_factor, attached CanvasScaler, has_graphic_" +
        "raycaster flag, scene EventSystem count. Warnings: no raycaster " +
        "(no pointer events), no EventSystem (no input), multiple " +
        "EventSystems, ScreenSpaceCamera/WorldSpace with null worldCamera.")]
    public static async Task<JsonElement> UICanvasInspect(UnityClient unity,
        int? instance_id, string? gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("ui-canvas-inspect",
        new UIObjectRefArgs(instance_id, gameobject_path), ct);

    [McpServerTool(Name = "ui-rect-transform-inspect"), Description(
        "Inspect a RectTransform: anchor_min/max, pivot, anchored_position, " +
        "size_delta, offset_min/max, rect, world_corners, local rotation/" +
        "scale, parent_canvas. Warnings: stretch anchors with unusual " +
        "pivot, no parent Canvas. Key evidence for 'why isn't this UI " +
        "sized/positioned the way I expect'.")]
    public static async Task<JsonElement> UIRectTransformInspect(UnityClient unity,
        int? instance_id, string? gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("ui-rect-transform-inspect",
        new UIObjectRefArgs(instance_id, gameobject_path), ct);

    [McpServerTool(Name = "ui-rect-transform-set"), Description(
        "Modify a RectTransform: any combination of anchor_min, anchor_max, " +
        "pivot, anchored_position, size_delta. At least one field required. " +
        "Undo-backed. Returns {applied, before, after} for read-back.")]
    public static async Task<JsonElement> UIRectTransformSet(UnityClient unity,
        int? instance_id, string? gameobject_path,
        Vec2Arg? anchor_min, Vec2Arg? anchor_max, Vec2Arg? pivot,
        Vec2Arg? anchored_position, Vec2Arg? size_delta, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("ui-rect-transform-set",
        new UIRectTransformSetArgs(instance_id, gameobject_path,
            anchor_min, anchor_max, pivot, anchored_position, size_delta), ct);

    [McpServerTool(Name = "ui-graphic-inspect"), Description(
        "Inspect any UI Graphic (Image, RawImage, Text, TextMeshProUGUI, " +
        "custom). Returns color, material, main_texture, raycast_target, " +
        "parent_canvas, depth, plus subtype-specific fields (sprite for " +
        "Image, texture+uv_rect for RawImage, text/font/alignment for Text). " +
        "Warnings: disabled, alpha=0, raycastTarget=false, empty Text, " +
        "Image with no sprite.")]
    public static async Task<JsonElement> UIGraphicInspect(UnityClient unity,
        int? instance_id, string? gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("ui-graphic-inspect",
        new UIObjectRefArgs(instance_id, gameobject_path), ct);

    [McpServerTool(Name = "ui-selectable-inspect"), Description(
        "Inspect any Selectable (Button, Toggle, Slider, Scrollbar, " +
        "Dropdown, InputField). Returns interactable, current_selection_" +
        "state, transition, target_graphic, plus subtype-specific state " +
        "(Toggle.isOn, Slider.value+min/max, Dropdown.value, InputField.text, " +
        "etc.). Warnings: not interactable, no target_graphic.")]
    public static async Task<JsonElement> UISelectableInspect(UnityClient unity,
        int? instance_id, string? gameobject_path, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("ui-selectable-inspect",
        new UIObjectRefArgs(instance_id, gameobject_path), ct);

    [McpServerTool(Name = "ui-event-system-state"), Description(
        "Read the EventSystem + raycaster landscape: count of EventSystems, " +
        "list of BaseRaycasters with their sort priorities, current.selected, " +
        "first.selected, focus state. Warnings: zero EventSystems (no UI " +
        "input), multiple EventSystems, zero raycasters (no pointer hits).")]
    public static async Task<JsonElement> UIEventSystemState(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("ui-event-system-state", null, ct);

    [McpServerTool(Name = "ui-raycast-at-point"), Description(
        "Run EventSystem.RaycastAll at a screen-space point. Returns every " +
        "hit with gameobject_path, instance_id, distance, screen/world " +
        "position, sorting_layer/order, depth, raycaster_type. Top-of-array " +
        "is what receives the click. Use this to answer 'why is my button " +
        "not clickable' — if the button isn't first in hits, something's " +
        "blocking it.")]
    public static async Task<JsonElement> UIRaycastAtPoint(UnityClient unity,
        float screen_x, float screen_y, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("ui-raycast-at-point",
        new UIRaycastAtPointArgs(screen_x, screen_y), ct);
}
