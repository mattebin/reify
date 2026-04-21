using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reify.Shared.Contracts;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class ConsoleLogServerTools
{
    [McpServerTool(Name = "console-log-read"), Description(
        "Read recent Unity Console entries. Each entry: type (Log, Warning, " +
        "Error, Assert, Exception), message, stack_trace, timestamp_utc, " +
        "frame, global_index. Optional filters: type_filter (error/warning/" +
        "info/all), count (default 50, max 500), since_frame, " +
        "contains_substring. Returns the most recent 'count' matches in " +
        "chronological order. Works during Play Mode — captures runtime logs.")]
    public static async Task<JsonElement> ConsoleLogRead(UnityClient unity,
        string? type_filter, int? count, long? since_frame, string? contains_substring, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("console-log-read",
        new ConsoleReadArgs(type_filter, count, since_frame, contains_substring), ct);

    [McpServerTool(Name = "console-log-clear"), Description(
        "Clear the reify log buffer. Set clear_unity_console=true (default) to " +
        "also clear Unity's Console window via reflection into internal " +
        "UnityEditor.LogEntries.Clear; on failure the tool still clears its " +
        "own buffer and reports the error in unity_console_error.")]
    public static async Task<JsonElement> ConsoleLogClear(UnityClient unity,
        bool? clear_unity_console, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("console-log-clear",
        new ConsoleClearArgs(clear_unity_console), ct);

    [McpServerTool(Name = "console-log-subscribe-snapshot"), Description(
        "Incremental log read keyed by session_id (default 'default'). Returns " +
        "entries with global_index > the session's last cursor; advances the " +
        "cursor to the last returned entry. Ideal for agents that poll for new " +
        "logs without re-reading history. Set reset_cursor=true to re-read " +
        "from the start of the buffer. max_entries default 500, cap 2000.")]
    public static async Task<JsonElement> ConsoleLogSubscribeSnapshot(UnityClient unity,
        string? session_id, string? type_filter, int? max_entries, bool? reset_cursor, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("console-log-subscribe-snapshot",
        new ConsoleSubscribeArgs(session_id, type_filter, max_entries, reset_cursor), ct);
}
