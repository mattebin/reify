using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class ProjectInfoServerTools
{
    [McpServerTool(Name = "project-info"), Description(
        "Read project-level identity and configuration: Unity version, " +
        "project path + name, product/company name, render pipeline " +
        "(Built-in/URP/HDRP + asset), scripting backend (Mono/IL2CPP), API " +
        "compatibility level, active build target, color space, graphics APIs. " +
        "Cheap one-call project snapshot.")]
    public static async Task<JsonElement> ProjectInfo(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("project-info", null, ct);

    [McpServerTool(Name = "project-packages"), Description(
        "Structured list of every installed UPM package — name, display_name, " +
        "version, source (Registry/Git/Local/Embedded/BuiltIn), is_direct_dep, " +
        "resolved_path, and dependency list. One snapshot of Packages/" +
        "manifest.json + the PackageManager resolved state.")]
    public static async Task<JsonElement> ProjectPackages(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("project-packages", null, ct);

    [McpServerTool(Name = "project-build-settings"), Description(
        "Return scenes in build (path, enabled, index, guid), active build " +
        "target, development_build flag, allow_debugging, connect_profiler. " +
        "Read-only snapshot of File > Build Settings as JSON.")]
    public static async Task<JsonElement> ProjectBuildSettings(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("project-build-settings", null, ct);

    [McpServerTool(Name = "project-layers-tags"), Description(
        "Enumerate all 32 layers (name + index + is_builtin + is_unused) and " +
        "all tags. Warns on duplicate tags. Handy before any gameobject-modify " +
        "layer/tag call so the caller knows what values are valid.")]
    public static async Task<JsonElement> ProjectLayersTags(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("project-layers-tags", null, ct);

    [McpServerTool(Name = "project-active-scene"), Description(
        "Return the currently active Scene's metadata: name, asset path, " +
        "build_index, is_loaded, is_dirty, root_count + root_gameobjects, " +
        "total gameobject_count (recursive). Cheap — use when you need the " +
        "active-scene identity without walking the full hierarchy.")]
    public static async Task<JsonElement> ProjectActiveScene(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("project-active-scene", null, ct);

    [McpServerTool(Name = "project-quality-settings"), Description(
        "Enumerate every Quality Level with its shadow distance, shadow " +
        "resolution + cascades, VSync count, anti-aliasing, pixel light " +
        "count, texture quality (mipmap limit), anisotropic filtering, " +
        "soft particles flag, LOD bias. Identifies current_level_index. " +
        "Per-platform overrides deferred to a future tool.")]
    public static async Task<JsonElement> ProjectQualitySettings(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("project-quality-settings", null, ct);

    [McpServerTool(Name = "project-render-pipeline-state"), Description(
        "Philosophy tool. Full structured diagnostic of the active render " +
        "pipeline. For URP: MSAA sample count, HDR, render scale, shadow " +
        "distance, shadow cascades, soft shadows, opaque/transparent layer " +
        "masks. Warnings flag common misconfigurations: MSAA off, HDR off " +
        "(emission/bloom clipping), render scale != 1.0, zero shadow distance " +
        "(shadows effectively off), cascades wasted on tiny distance, layer " +
        "masks that zero out opaque or transparent passes. This is the " +
        "tool that would have solved the skybox-not-rendering bug in one " +
        "call. URP asset read via reflection — no package dependency.")]
    public static async Task<JsonElement> ProjectRenderPipelineState(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("project-render-pipeline-state", null, ct);
}
