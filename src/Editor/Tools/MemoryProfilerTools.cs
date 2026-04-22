using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling.Memory.Experimental;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Built-in memory snapshot surface. Uses
    /// UnityEngine.Profiling.Memory.Experimental.MemoryProfiler.TakeSnapshot
    /// which ships with Unity — no com.unity.memoryprofiler package needed
    /// to produce the .snap file. The package is needed to OPEN the file
    /// in the UI, but this tool just writes it to disk + returns the path
    /// so the asset is durable.
    ///
    /// Complements the existing profiler-memory-info snapshot (which
    /// returns summary counters) with the full heap dump for later
    /// offline analysis.
    /// </summary>
    internal static class MemoryProfilerTools
    {
        // ---------- memory-snapshot-capture ----------
        [ReifyTool("memory-snapshot-capture")]
        public static Task<object> Capture(JToken args)
        {
            var outputPath = args?.Value<string>("output_path");
            var captureFlagsStr = args?.Value<string>("capture_flags");

            return MainThreadDispatcher.RunAsync<object>(async () =>
            {
                // Default to a timestamped file under the project root.
                if (string.IsNullOrEmpty(outputPath))
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
                    outputPath = Path.GetFullPath(Path.Combine(".", "MemorySnapshots", $"reify_{stamp}.snap"));
                }
                else
                {
                    outputPath = Path.GetFullPath(outputPath);
                }

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                Unity.Profiling.Memory.CaptureFlags flags = Unity.Profiling.Memory.CaptureFlags.ManagedObjects | Unity.Profiling.Memory.CaptureFlags.NativeObjects
                    | Unity.Profiling.Memory.CaptureFlags.NativeAllocations | Unity.Profiling.Memory.CaptureFlags.NativeAllocationSites
                    | Unity.Profiling.Memory.CaptureFlags.NativeStackTraces;
                if (!string.IsNullOrEmpty(captureFlagsStr))
                {
                    if (!Enum.TryParse(captureFlagsStr, true, out flags))
                        throw new ArgumentException(
                            $"capture_flags '{captureFlagsStr}' is not a valid CaptureFlags value. " +
                            "Use names from UnityEngine.Profiling.Memory.Experimental.CaptureFlags " +
                            "separated by commas, or omit for the default full capture.");
                }

                var tcs = new System.Threading.Tasks.TaskCompletionSource<(string path, bool ok)>(
                    System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

                Unity.Profiling.Memory.MemoryProfiler.TakeSnapshot(outputPath, (path, ok) => tcs.TrySetResult((path, ok)), flags);

                // Callbacks fire on main thread during the next editor update;
                // awaiting here yields control so the dispatcher can keep
                // draining and the TakeSnapshot internals can run.
                var result = await tcs.Task;

                long sizeBytes = 0;
                if (!string.IsNullOrEmpty(result.path) && File.Exists(result.path))
                    sizeBytes = new FileInfo(result.path).Length;

                return (object)new
                {
                    success       = result.ok,
                    output_path   = result.path ?? outputPath,
                    size_bytes    = sizeBytes,
                    capture_flags = flags.ToString(),
                    note          = "To open this .snap, install com.unity.memoryprofiler and " +
                                    "use Window > Analysis > Memory Profiler. The file is a " +
                                    "durable artifact — reify produced it without needing the " +
                                    "package installed.",
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }
    }
}
