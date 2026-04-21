using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Profiler surface: frame rendering stats (via UnityStats), memory
    /// usage summaries (via UnityEngine.Profiling.Profiler), and
    /// recording toggle. Deep profiling / marker recorder capture is a
    /// future batch — that needs ProfilerRecorder and is stateful enough
    /// to deserve its own design pass.
    /// </summary>
    internal static class ProfilerTools
    {
        // ---------- profiler-frame-stats ----------
        [ReifyTool("profiler-frame-stats")]
        public static Task<object> FrameStats(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                // UnityStats is an internal editor class exposing per-frame
                // draw statistics. Public-ish — available in UnityEditor
                // namespace but undocumented. Wrap in try/catch so Unity
                // version drift doesn't crash the tool.
                int triangles = -1, vertices = -1, drawCalls = -1, batchedDrawCalls = -1, dynamicBatchedDrawCalls = -1, staticBatchedDrawCalls = -1, instancedBatchedDrawCalls = -1, setPassCalls = -1, shadowCasters = -1, renderTextureChanges = -1, usedTextureMemorySize = -1, usedTextureCount = -1;
                float frameTime = -1f, renderTime = -1f;
                string error = null;
                try
                {
                    var t = Type.GetType("UnityEditor.UnityStats, UnityEditor");
                    if (t != null)
                    {
                        triangles                 = GetStatic<int>  (t, "triangles");
                        vertices                  = GetStatic<int>  (t, "vertices");
                        drawCalls                 = GetStatic<int>  (t, "drawCalls");
                        batchedDrawCalls          = GetStatic<int>  (t, "batchedDrawCalls");
                        dynamicBatchedDrawCalls   = GetStatic<int>  (t, "dynamicBatchedDrawCalls");
                        staticBatchedDrawCalls    = GetStatic<int>  (t, "staticBatchedDrawCalls");
                        instancedBatchedDrawCalls = GetStatic<int>  (t, "instancedBatchedDrawCalls");
                        setPassCalls              = GetStatic<int>  (t, "setPassCalls");
                        shadowCasters             = GetStatic<int>  (t, "shadowCasters");
                        renderTextureChanges      = GetStatic<int>  (t, "renderTextureChanges");
                        usedTextureMemorySize     = GetStatic<int>  (t, "usedTextureMemorySize");
                        usedTextureCount          = GetStatic<int>  (t, "usedTextureCount");
                        frameTime                 = GetStatic<float>(t, "frameTime");
                        renderTime                = GetStatic<float>(t, "renderTime");
                    }
                    else
                    {
                        error = "UnityEditor.UnityStats not found — Unity API changed.";
                    }
                }
                catch (Exception ex) { error = ex.Message; }

                var warnings = new List<string>();
                if (drawCalls >= 0 && drawCalls > 1000)
                    warnings.Add($"drawCalls = {drawCalls} — high. Consider static/dynamic batching or SRP batcher.");
                if (setPassCalls >= 0 && setPassCalls > 500)
                    warnings.Add($"setPassCalls = {setPassCalls} — high. Usually driven by unique materials; combine where possible.");
                if (frameTime >= 0f && frameTime > 33.33f)
                    warnings.Add($"frameTime = {frameTime:F2} ms — below 30 fps. Investigate per-pass cost.");
                if (!string.IsNullOrEmpty(error))
                    warnings.Add($"Stats read error: {error}");

                return new
                {
                    triangles,
                    vertices,
                    draw_calls                    = drawCalls,
                    batched_draw_calls            = batchedDrawCalls,
                    dynamic_batched_draw_calls    = dynamicBatchedDrawCalls,
                    static_batched_draw_calls     = staticBatchedDrawCalls,
                    instanced_batched_draw_calls  = instancedBatchedDrawCalls,
                    set_pass_calls                = setPassCalls,
                    shadow_casters                = shadowCasters,
                    render_texture_changes        = renderTextureChanges,
                    used_texture_memory_bytes     = usedTextureMemorySize,
                    used_texture_count            = usedTextureCount,
                    frame_time_ms                 = frameTime,
                    render_time_ms                = renderTime,
                    error,
                    warnings                      = warnings.ToArray(),
                    read_at_utc                   = DateTime.UtcNow.ToString("o"),
                    frame                         = (long)Time.frameCount
                };
            });
        }

        // ---------- profiler-memory-info ----------
        [ReifyTool("profiler-memory-info")]
        public static Task<object> MemoryInfo(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var warnings = new List<string>();
                var totalAllocated = Profiler.GetTotalAllocatedMemoryLong();
                var totalReserved  = Profiler.GetTotalReservedMemoryLong();
                var totalUnused    = Profiler.GetTotalUnusedReservedMemoryLong();
                var monoUsed       = Profiler.GetMonoUsedSizeLong();
                var monoHeap       = Profiler.GetMonoHeapSizeLong();
                var tempAllocator  = Profiler.GetTempAllocatorSize();
                var gfxDriver      = Profiler.GetAllocatedMemoryForGraphicsDriver();

                if (monoUsed > 500L * 1024 * 1024)
                    warnings.Add($"Mono heap used is {monoUsed / (1024 * 1024)} MB — large. Consider pooling / references holding onto assets.");
                if (totalReserved > 0 && totalUnused > 0 && totalUnused > totalReserved / 2)
                    warnings.Add("Large unused reserved memory — allocator fragmentation. A domain reload or Resources.UnloadUnusedAssets may help.");

                return new
                {
                    total_allocated_bytes    = totalAllocated,
                    total_reserved_bytes     = totalReserved,
                    total_unused_reserved_bytes = totalUnused,
                    mono_used_bytes          = monoUsed,
                    mono_heap_bytes          = monoHeap,
                    temp_allocator_bytes     = tempAllocator,
                    graphics_driver_bytes    = gfxDriver,
                    // Human-readable MB versions for quick skim
                    total_allocated_mb       = totalAllocated / (1024.0 * 1024.0),
                    total_reserved_mb        = totalReserved  / (1024.0 * 1024.0),
                    mono_used_mb             = monoUsed       / (1024.0 * 1024.0),
                    graphics_driver_mb       = gfxDriver      / (1024.0 * 1024.0),
                    warnings                 = warnings.ToArray(),
                    read_at_utc              = DateTime.UtcNow.ToString("o"),
                    frame                    = (long)Time.frameCount
                };
            });
        }

        // ---------- profiler-recording-status ----------
        [ReifyTool("profiler-recording-status")]
        public static Task<object> RecordingStatus(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var isEnabled = Profiler.enabled;
                var isDeep    = Profiler.supported;  // always true on Editor; real "deep" is ProfilerDriver-internal
                return new
                {
                    is_enabled   = isEnabled,
                    is_supported = isDeep,
                    area_note    = "Profiler.enabled toggles recording; ProfilerDriver (internal) controls deep-profile. Use profiler-set-recording to flip.",
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- profiler-set-recording ----------
        [ReifyTool("profiler-set-recording")]
        public static Task<object> SetRecording(JToken args)
        {
            var enabled = args?.Value<bool?>("enabled") ?? throw new ArgumentException("enabled (bool) is required.");
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var before = Profiler.enabled;
                Profiler.enabled = enabled;
                return new
                {
                    before_enabled = before,
                    after_enabled  = Profiler.enabled,
                    requested      = enabled,
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers ----------
        private static T GetStatic<T>(Type type, string name)
        {
            var p = type.GetProperty(name,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (p != null)
                try { return (T)Convert.ChangeType(p.GetValue(null), typeof(T)); } catch { }
            var f = type.GetField(name,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f != null)
                try { return (T)Convert.ChangeType(f.GetValue(null), typeof(T)); } catch { }
            return default;
        }
    }
}
