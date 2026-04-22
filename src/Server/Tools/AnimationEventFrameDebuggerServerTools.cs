using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class AnimationEventServerTools
{
    [McpServerTool(Name = "animation-clip-events-read"), Description(
        "Read every AnimationEvent on an AnimationClip. Returns {index, " +
        "time, function_name, string/float/int/object parameter, " +
        "send_message_options}.")]
    public static async Task<JsonElement> AnimationClipEventsRead(
        UnityClient unity,
        string asset_path,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("animation-clip-events-read",
        new { asset_path }, ct);

    [McpServerTool(Name = "animation-clip-events-set"), Description(
        "Replace an AnimationClip's full AnimationEvent list. events[] = " +
        "array of { function_name (required), time, string_parameter, " +
        "float_parameter, int_parameter, send_message_options, " +
        "object_parameter: { asset_path } }. Pass [] to clear all events. " +
        "Undo-backed.")]
    public static async Task<JsonElement> AnimationClipEventsSet(
        UnityClient unity,
        string asset_path,
        JsonElement events,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("animation-clip-events-set",
        new { asset_path, events }, ct);
}

[McpServerToolType]
public static class FrameDebuggerServerTools
{
    [McpServerTool(Name = "frame-debugger-status"), Description(
        "Read Frame Debugger state via reflection into " +
        "UnityEditorInternal.FrameDebuggerUtility. Returns " +
        "is_enabled_local / _remote, receiving_remote, total_event_count, " +
        "current_event_limit. Open the window via Window > Analysis > " +
        "Frame Debugger; these tools only read.")]
    public static async Task<JsonElement> FrameDebuggerStatus(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("frame-debugger-status", null, ct);

    [McpServerTool(Name = "frame-debugger-set-enabled"), Description(
        "Toggle the local Frame Debugger. Args: enabled (bool). Returns " +
        "before_enabled / after_enabled / requested. Uses reflection — " +
        "Unity's internal SetEnabled API signature varies across versions.")]
    public static async Task<JsonElement> FrameDebuggerSetEnabled(
        UnityClient unity,
        bool enabled,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("frame-debugger-set-enabled",
        new { enabled }, ct);
}
