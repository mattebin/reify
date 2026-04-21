using System.Text.Json.Serialization;

namespace Reify.Shared.Contracts;

public sealed record ConsoleReadArgs(
    [property: JsonPropertyName("type_filter")]        string? TypeFilter,
    [property: JsonPropertyName("count")]              int? Count,
    [property: JsonPropertyName("since_frame")]        long? SinceFrame,
    [property: JsonPropertyName("contains_substring")] string? ContainsSubstring);

public sealed record ConsoleClearArgs(
    [property: JsonPropertyName("clear_unity_console")] bool? ClearUnityConsole);

public sealed record ConsoleSubscribeArgs(
    [property: JsonPropertyName("session_id")]   string? SessionId,
    [property: JsonPropertyName("type_filter")]  string? TypeFilter,
    [property: JsonPropertyName("max_entries")]  int? MaxEntries,
    [property: JsonPropertyName("reset_cursor")] bool? ResetCursor);
