using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class PlayModeServerTools
{
    [McpServerTool(Name = "play-mode-enter"), Description(
        "Enter Play Mode. Asynchronous — handler returns 'enter_queued' " +
        "immediately; poll play-mode-status to observe state transition. " +
        "Safe when already playing (returns 'already_playing'). " +
        "\n\nBridge survives the domain reload that Play Mode triggers — " +
        "subsequent tool calls will work once recompilation completes.")]
    public static async Task<JsonElement> PlayModeEnter(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("play-mode-enter", null, ct);

    [McpServerTool(Name = "play-mode-exit"), Description(
        "Exit Play Mode. Asynchronous. Safe when already stopped.")]
    public static async Task<JsonElement> PlayModeExit(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("play-mode-exit", null, ct);

    [McpServerTool(Name = "play-mode-pause"), Description(
        "Pause Play Mode. Fails if not currently playing.")]
    public static async Task<JsonElement> PlayModePause(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("play-mode-pause", null, ct);

    [McpServerTool(Name = "play-mode-resume"), Description(
        "Resume from Pause. Fails if not currently playing.")]
    public static async Task<JsonElement> PlayModeResume(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("play-mode-resume", null, ct);

    [McpServerTool(Name = "play-mode-step"), Description(
        "Advance one frame while paused. Fails if not playing or not paused.")]
    public static async Task<JsonElement> PlayModeStep(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("play-mode-step", null, ct);

    [McpServerTool(Name = "play-mode-status"), Description(
        "Read current play-mode state: edit | transitioning | playing | paused. " +
        "Includes is_playing, is_paused, is_compiling, entered_at_utc, " +
        "seconds_since_entered, frame, and realtime_since_startup. Cheap — " +
        "poll freely to observe transitions.")]
    public static async Task<JsonElement> PlayModeStatus(UnityClient unity, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("play-mode-status", null, ct);
}
