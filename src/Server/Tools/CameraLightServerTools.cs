using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class CameraLightServerTools
{
    [McpServerTool(Name = "camera-inspect"), Description(
        "Inspect a Camera: enabled, tag, is_main, orthographic/fov, clip " +
        "planes, depth, culling_mask, clear_flags, background_color, " +
        "viewport rect, pixel rect, aspect, HDR/MSAA, target_texture, " +
        "target_display. Warnings: cullingMask=0 (nothing renders), zero " +
        "fov/orthoSize, near>=far, tiny near (z-fighting), main mismatch. " +
        "Resolve by instance_id, gameobject_path, or use_main=true.")]
    public static async Task<JsonElement> CameraInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        bool? use_main = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("camera-inspect", new
    {
        instance_id, gameobject_path, use_main
    }, ct);

    [McpServerTool(Name = "camera-set"), Description(
        "Modify a Camera. At least one field required. clear_flags is " +
        "Skybox/SolidColor/Depth/Nothing. background_color is {r,g,b,a}. " +
        "Undo-backed. Returns {applied, before, after}.")]
    public static async Task<JsonElement> CameraSet(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        bool? use_main = null,
        float? field_of_view = null,
        float? near_clip_plane = null,
        float? far_clip_plane = null,
        bool? orthographic = null,
        float? orthographic_size = null,
        int? culling_mask = null,
        float? depth = null,
        string? clear_flags = null,
        JsonElement? background_color = null,
        bool? hdr = null,
        int? target_display = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("camera-set", new
    {
        instance_id, gameobject_path, use_main,
        field_of_view, near_clip_plane, far_clip_plane,
        orthographic, orthographic_size, culling_mask, depth,
        clear_flags, background_color, hdr, target_display
    }, ct);

    [McpServerTool(Name = "camera-world-to-screen-point"), Description(
        "Project a world position into screen and viewport space for a " +
        "given camera. Returns {screen_point (z=distance), viewport_point, " +
        "on_screen, behind_camera, pixel_width, pixel_height}. Use to " +
        "check whether a world point is visible before expensive rendering. " +
        "world_position is {x,y,z}.")]
    public static async Task<JsonElement> CameraWorldToScreenPoint(
        UnityClient unity,
        JsonElement world_position,
        int? instance_id = null,
        string? gameobject_path = null,
        bool? use_main = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("camera-world-to-screen-point", new
    {
        instance_id, gameobject_path, use_main, world_position
    }, ct);

    [McpServerTool(Name = "camera-screen-to-world-ray"), Description(
        "Compute the world-space ray from a camera through a screen pixel. " +
        "Returns {origin, direction}. Feed this into physics-raycast for " +
        "click-to-world picking.")]
    public static async Task<JsonElement> CameraScreenToWorldRay(
        UnityClient unity,
        float screen_x,
        float screen_y,
        int? instance_id = null,
        string? gameobject_path = null,
        bool? use_main = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("camera-screen-to-world-ray", new
    {
        instance_id, gameobject_path, use_main, screen_x, screen_y
    }, ct);

    [McpServerTool(Name = "light-create"), Description(
        "Create a Light. light_type ∈ Directional/Point/Spot/Area/Disc/" +
        "Rectangle. shadows ∈ None/Hard/Soft. color is {r,g,b,a}. " +
        "world_position is {x,y,z}. Undo-backed.")]
    public static async Task<JsonElement> LightCreate(
        UnityClient unity,
        string? name = null,
        string? light_type = null,
        string? parent_path = null,
        float? intensity = null,
        float? range = null,
        float? spot_angle = null,
        string? shadows = null,
        JsonElement? color = null,
        JsonElement? world_position = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("light-create", new
    {
        name, light_type, parent_path,
        intensity, range, spot_angle, shadows,
        color, world_position
    }, ct);

    [McpServerTool(Name = "light-set"), Description(
        "Modify a Light. At least one field required. Undo-backed. Warnings " +
        "for disabled, zero intensity, zero range on point/spot, zero spot " +
        "angle.")]
    public static async Task<JsonElement> LightSet(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        string? light_type = null,
        float? intensity = null,
        float? range = null,
        float? spot_angle = null,
        string? shadows = null,
        float? shadow_strength = null,
        JsonElement? color = null,
        int? culling_mask = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("light-set", new
    {
        instance_id, gameobject_path,
        light_type, intensity, range, spot_angle,
        shadows, shadow_strength, color, culling_mask
    }, ct);

    [McpServerTool(Name = "light-inspect"), Description(
        "Read single-Light state: type, mode (bake type), intensity, range, " +
        "spot_angle, shadows, color, culling_mask, render_mode, world " +
        "transform. Complements lighting-diagnostic's scene-wide view.")]
    public static async Task<JsonElement> LightInspect(
        UnityClient unity,
        int? instance_id = null,
        string? gameobject_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("light-inspect", new
    {
        instance_id, gameobject_path
    }, ct);
}
