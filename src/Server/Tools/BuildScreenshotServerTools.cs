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
    public static async Task<JsonElement> BuildTargetGet(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("build-target-get", null, ct);

    [McpServerTool(Name = "build-target-switch"), Description(
        "Switch the active build target. target is e.g. StandaloneWindows64, " +
        "Android, WebGL, iOS, LinuxServer. Rejects unsupported targets " +
        "cleanly. NOTE: switching triggers a full asset reimport for the new " +
        "target — expect a long editor stall; poll domain-reload-status for " +
        "readiness.")]
    public static async Task<JsonElement> BuildTargetSwitch(
        UnityClient unity,
        string target,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("build-target-switch", new { target }, ct);

    [McpServerTool(Name = "build-execute"), Description(
        "Run BuildPipeline.BuildPlayer for the active target. " +
        "output_path is required. scenes[] defaults to enabled scenes from " +
        "EditorBuildSettings when null/empty. Returns a BuildReport summary: " +
        "result, total_size_bytes, total_time_seconds, error/warning counts.")]
    public static async Task<JsonElement> BuildExecute(
        UnityClient unity,
        string output_path,
        string[]? scenes = null,
        bool? development = null,
        bool? auto_run_player = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("build-execute", new
    {
        output_path, scenes, development, auto_run_player
    }, ct);

    [McpServerTool(Name = "structured-screenshot"), Description(
        "Philosophy escape hatch. Renders a camera to a PNG under Assets/ " +
        "AND returns the structured scene state for that same frame: the " +
        "camera's config plus every Renderer whose bounds intersect the " +
        "camera frustum (gameobject_path, material, sorting, bounds). " +
        "\n\n" +
        "Camera defaults to Camera.main when both camera_instance_id and " +
        "camera_gameobject_path are null. output_path defaults to " +
        "'Assets/ReifyScreenshots/capture.png'. " +
        "\n\n" +
        "Use sparingly — the structured-state reads (scene-query, " +
        "material-inspect, render-queue-audit, etc.) are faster, cheaper, " +
        "and diff cleanly across frames. Pull this tool when you actually " +
        "need human-level vision, not before.")]
    public static async Task<JsonElement> StructuredScreenshot(
        UnityClient unity,
        int? camera_instance_id = null,
        string? camera_gameobject_path = null,
        string? output_path = null,
        int? width = null,
        int? height = null,
        bool? include_scene_state = null,
        int? max_renderers_in_frame = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("structured-screenshot", new
    {
        camera_instance_id, camera_gameobject_path,
        output_path, width, height,
        include_scene_state, max_renderers_in_frame
    }, ct);
}
