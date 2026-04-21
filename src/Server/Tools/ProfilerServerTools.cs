using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class ProfilerServerTools
{
    [McpServerTool(Name = "profiler-frame-stats"), Description(
        "Current-frame rendering stats: triangles, vertices, draw_calls " +
        "(plus batched/dynamic/static/instanced splits), set_pass_calls, " +
        "shadow_casters, render_texture_changes, used_texture_memory/count, " +
        "frame_time_ms, render_time_ms. Warnings for high draw/setpass " +
        "counts and sub-30-fps frame time. Read via UnityEditor.UnityStats " +
        "reflectively — may drift across Unity versions; error field set " +
        "when it does.")]
    public static async Task<JsonElement> ProfilerFrameStats(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("profiler-frame-stats", null, ct);

    [McpServerTool(Name = "profiler-memory-info"), Description(
        "Current memory totals via UnityEngine.Profiling.Profiler: total " +
        "allocated/reserved/unused, Mono used/heap, temp allocator, graphics " +
        "driver. MB versions included for skim. Warnings for large Mono " +
        "heap and high fragmentation.")]
    public static async Task<JsonElement> ProfilerMemoryInfo(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("profiler-memory-info", null, ct);

    [McpServerTool(Name = "profiler-recording-status"), Description(
        "Check whether Profiler.enabled is on. Paired with profiler-set-" +
        "recording to toggle. Note: deep profiling (via ProfilerDriver) is " +
        "internal and not exposed here.")]
    public static async Task<JsonElement> ProfilerRecordingStatus(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("profiler-recording-status", null, ct);

    [McpServerTool(Name = "profiler-set-recording"), Description(
        "Toggle Profiler.enabled. Args: enabled (bool, required). Returns " +
        "before/after for read-back.")]
    public static async Task<JsonElement> ProfilerSetRecording(UnityClient unity, bool enabled, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("profiler-set-recording",
        new { enabled }, ct);
}
