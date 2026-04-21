using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class BuildScreenshotServerTools
{
    [McpServerTool(Name = "build-target-get"), Description(
        "Read the active build target + group, list every BuildTarget " +
        "supported by this Unity install, and current dev/debug/profiler " +
        "flags. Cheap read.")]
    public static async Task<JsonElement> BuildTargetGet(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("build-target-get", null, ct);

    [McpServerTool(Name = "build-target-switch"), Description(
        "Switch the active build target. Args: target (e.g. " +
        "StandaloneWindows64, Android, WebGL, iOS, LinuxServer). Rejects " +
        "unsupported targets cleanly. NOTE: switching triggers a full " +
        "asset reimport for the new target — expect a long editor stall; " +
        "poll domain-reload-status for readiness.")]
    public static async Task<JsonElement> BuildTargetSwitch(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("build-target-switch", args, ct);

    [McpServerTool(Name = "build-execute"), Description(
        "Run BuildPipeline.BuildPlayer for the active target. Args: " +
        "output_path (required), scenes[] (optional — defaults to enabled " +
        "scenes from EditorBuildSettings), development (bool), " +
        "auto_run_player (bool). Returns a BuildReport summary: result, " +
        "total_size_bytes, total_time_seconds, error/warning counts.")]
    public static async Task<JsonElement> BuildExecute(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("build-execute", args, ct);

    [McpServerTool(Name = "structured-screenshot"), Description(
        "Philosophy escape hatch. Renders a camera to a PNG under Assets/ " +
        "AND returns the structured scene state for that same frame: the " +
        "camera's config plus every Renderer whose bounds intersect the " +
        "camera frustum (gameobject_path, material, sorting, bounds). " +
        "\n\n" +
        "Args: camera ref (camera_instance_id / camera_gameobject_path, " +
        "defaults to Camera.main), output_path (default " +
        "'Assets/ReifyScreenshots/capture.png'), width (default 1280), " +
        "height (default 720), include_scene_state (default true), " +
        "max_renderers_in_frame (default 200). " +
        "\n\n" +
        "Use sparingly — the structured-state reads (scene-query, " +
        "material-inspect, render-queue-audit, etc.) are faster, cheaper, " +
        "and diff cleanly across frames. Pull this tool when you actually " +
        "need human-level vision, not before.")]
    public static async Task<JsonElement> StructuredScreenshot(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("structured-screenshot", args, ct);
}
