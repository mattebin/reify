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
    public static async Task<JsonElement> CameraInspect(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("camera-inspect", args, ct);

    [McpServerTool(Name = "camera-set"), Description(
        "Modify a Camera: field_of_view, near_clip_plane, far_clip_plane, " +
        "orthographic, orthographic_size, culling_mask, depth, clear_flags " +
        "(Skybox/SolidColor/Depth/Nothing), background_color {r,g,b,a}, " +
        "hdr, target_display. At least one required. Undo-backed. Returns " +
        "{applied, before, after}.")]
    public static async Task<JsonElement> CameraSet(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("camera-set", args, ct);

    [McpServerTool(Name = "camera-world-to-screen-point"), Description(
        "Project a world position into screen and viewport space for a " +
        "given camera. Returns {screen_point (z=distance), viewport_point, " +
        "on_screen, behind_camera, pixel_width, pixel_height}. Use to " +
        "check whether a world point is visible before expensive rendering.")]
    public static async Task<JsonElement> CameraWorldToScreenPoint(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("camera-world-to-screen-point", args, ct);

    [McpServerTool(Name = "camera-screen-to-world-ray"), Description(
        "Compute the world-space ray from a camera through a screen pixel. " +
        "Returns {origin, direction}. Feed this into physics-raycast for " +
        "click-to-world picking.")]
    public static async Task<JsonElement> CameraScreenToWorldRay(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("camera-screen-to-world-ray", args, ct);

    [McpServerTool(Name = "light-create"), Description(
        "Create a Light: name, light_type (Directional/Point/Spot/Area/" +
        "Disc/Rectangle), optional parent_path, intensity, range, " +
        "spot_angle, shadows (None/Hard/Soft), color {r,g,b,a}, " +
        "world_position {x,y,z}. Undo-backed.")]
    public static async Task<JsonElement> LightCreate(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("light-create", args, ct);

    [McpServerTool(Name = "light-set"), Description(
        "Modify a Light: light_type, intensity, range, spot_angle, shadows, " +
        "shadow_strength, color, culling_mask. At least one required. " +
        "Undo-backed. Warnings for disabled, zero intensity, zero range " +
        "on point/spot, zero spot angle.")]
    public static async Task<JsonElement> LightSet(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("light-set", args, ct);

    [McpServerTool(Name = "light-inspect"), Description(
        "Read single-Light state: type, mode (bake type), intensity, range, " +
        "spot_angle, shadows, color, culling_mask, render_mode, world " +
        "transform. Complements lighting-diagnostic's scene-wide view.")]
    public static async Task<JsonElement> LightInspect(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("light-inspect", args, ct);
}
