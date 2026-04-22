using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class MemoryProfilerServerTools
{
    [McpServerTool(Name = "memory-snapshot-capture"), Description(
        "Capture a full Unity memory snapshot (.snap file) via " +
        "UnityEngine.Profiling.Memory.Experimental.MemoryProfiler." +
        "TakeSnapshot. Built-in — does NOT require the " +
        "com.unity.memoryprofiler package (that package is only needed " +
        "to OPEN the .snap in the editor UI). " +
        "\n\n" +
        "output_path defaults to ./MemorySnapshots/reify_<timestamp>.snap. " +
        "capture_flags defaults to the full set (ManagedObjects | " +
        "NativeObjects | NativeAllocations | NativeAllocationSites | " +
        "NativeStackTraces); pass a comma-separated CaptureFlags value " +
        "to narrow. Returns {success, output_path, size_bytes, " +
        "capture_flags}. Complements profiler-memory-info (live " +
        "counters) with a durable heap dump for offline analysis.")]
    public static async Task<JsonElement> MemorySnapshotCapture(
        UnityClient unity,
        string? output_path = null,
        string? capture_flags = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("memory-snapshot-capture", new
    {
        output_path, capture_flags
    }, ct);
}
