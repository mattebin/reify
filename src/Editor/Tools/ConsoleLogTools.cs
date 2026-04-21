using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Captures Unity log output into a ring buffer, exposes read/clear
    /// tools, and supports per-caller incremental reads via
    /// console-log-subscribe-snapshot. Thread-safe — Unity delivers some
    /// logs off the main thread.
    ///
    /// InitializeOnLoad so log capture starts as soon as the editor
    /// assembly loads; we miss pre-load messages from the current Unity
    /// session but catch everything after domain reload and during play.
    /// </summary>
    [InitializeOnLoad]
    internal static class ConsoleLogTools
    {
        private const int Capacity = 2000;

        private static readonly object _lock = new object();
        private static readonly LogEntry[] _buffer = new LogEntry[Capacity];
        private static int _writeIndex = 0;   // next slot to write
        private static int _totalSeen  = 0;   // monotonically increasing, used as global index

        // Session-scoped cursors for console-log-subscribe-snapshot.
        private static readonly ConcurrentDictionary<string, int> _sessionCursors = new ConcurrentDictionary<string, int>();

        private struct LogEntry
        {
            public int    globalIndex;
            public string type;          // "Log", "Warning", "Error", "Assert", "Exception"
            public string message;
            public string stackTrace;
            public string timestampUtc;
            public long   frame;
        }

        static ConsoleLogTools()
        {
            Application.logMessageReceivedThreaded += OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            lock (_lock)
            {
                _buffer[_writeIndex] = new LogEntry
                {
                    globalIndex  = _totalSeen,
                    type         = type.ToString(),
                    message      = message ?? "",
                    stackTrace   = stackTrace ?? "",
                    timestampUtc = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
                _writeIndex = (_writeIndex + 1) % Capacity;
                _totalSeen++;
            }
        }

        // ---------- console-log-read ----------
        public static Task<object> Read(JToken args)
        {
            var typeFilter      = (args?.Value<string>("type_filter") ?? "all").ToLowerInvariant();
            var count           = Math.Min(args?.Value<int?>("count") ?? 50, 500);
            var sinceFrame      = args?["since_frame"]?.Type == JTokenType.Integer
                ? args.Value<long?>("since_frame") : null;
            var containsSubstr  = args?.Value<string>("contains_substring");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var snapshot = Snapshot();
                var filtered = ApplyFilters(snapshot, typeFilter, sinceFrame, containsSubstr);
                // Return the MOST RECENT `count` entries, in chronological order.
                var start = Math.Max(0, filtered.Count - count);
                var slice = filtered.GetRange(start, filtered.Count - start);

                return new
                {
                    returned      = slice.Count,
                    total_in_buffer = snapshot.Count,
                    total_seen    = _totalSeen,
                    truncated     = filtered.Count > count,
                    filter        = new { type_filter = typeFilter, count, since_frame = sinceFrame, contains_substring = containsSubstr },
                    entries       = slice.Select(ToDto).ToArray(),
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        // ---------- console-log-clear ----------
        public static Task<object> Clear(JToken args)
        {
            var alsoClearUnityConsole = args?.Value<bool?>("clear_unity_console") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                int cleared;
                lock (_lock)
                {
                    cleared = CountInBuffer();
                    Array.Clear(_buffer, 0, _buffer.Length);
                    _writeIndex = 0;
                    // Don't reset _totalSeen — keeps globalIndex monotonic
                    // for clients that keep a cursor across clears.
                }
                _sessionCursors.Clear();

                var unityConsoleCleared = false;
                string unityConsoleError = null;
                if (alsoClearUnityConsole)
                {
                    try
                    {
                        // UnityEditor.LogEntries is internal — reached via reflection.
                        var t = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                        var m = t?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public);
                        if (m != null) { m.Invoke(null, null); unityConsoleCleared = true; }
                        else unityConsoleError = "UnityEditor.LogEntries.Clear not found.";
                    }
                    catch (Exception ex) { unityConsoleError = ex.Message; }
                }

                return new
                {
                    cleared_buffer_entries = cleared,
                    unity_console_cleared  = unityConsoleCleared,
                    unity_console_error    = unityConsoleError,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- console-log-subscribe-snapshot ----------
        public static Task<object> SubscribeSnapshot(JToken args)
        {
            var sessionId      = args?.Value<string>("session_id") ?? "default";
            var typeFilter     = (args?.Value<string>("type_filter") ?? "all").ToLowerInvariant();
            var maxEntries     = Math.Min(args?.Value<int?>("max_entries") ?? 500, 2000);
            var resetCursor    = args?.Value<bool?>("reset_cursor") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                if (resetCursor) _sessionCursors.TryRemove(sessionId, out _);
                var lastSeen = _sessionCursors.GetOrAdd(sessionId, _ => 0);

                var snapshot = Snapshot();
                var unseen = new List<LogEntry>();
                foreach (var e in snapshot)
                    if (e.globalIndex > lastSeen) unseen.Add(e);

                // Filter.
                var filtered = ApplyFilters(unseen, typeFilter, null, null);
                var truncated = filtered.Count > maxEntries;
                var slice = truncated ? filtered.GetRange(0, maxEntries) : filtered;

                var newCursor = slice.Count > 0 ? slice[slice.Count - 1].globalIndex : lastSeen;
                _sessionCursors[sessionId] = newCursor;

                return new
                {
                    session_id     = sessionId,
                    previous_cursor = lastSeen,
                    new_cursor     = newCursor,
                    returned       = slice.Count,
                    unseen_total   = filtered.Count,
                    truncated,
                    entries        = slice.Select(ToDto).ToArray(),
                    read_at_utc    = DateTime.UtcNow.ToString("o"),
                    frame          = (long)Time.frameCount
                };
            });
        }

        // ---------- shared ----------
        private static List<LogEntry> Snapshot()
        {
            lock (_lock)
            {
                var result = new List<LogEntry>(Capacity);
                // Walk oldest → newest. If buffer not yet wrapped, _writeIndex == count.
                if (_totalSeen < Capacity)
                {
                    for (var i = 0; i < _writeIndex; i++) result.Add(_buffer[i]);
                }
                else
                {
                    // Buffer is wrapped; oldest is at _writeIndex.
                    for (var i = 0; i < Capacity; i++)
                        result.Add(_buffer[(_writeIndex + i) % Capacity]);
                }
                return result;
            }
        }

        private static int CountInBuffer()
        {
            return _totalSeen < Capacity ? _writeIndex : Capacity;
        }

        private static List<LogEntry> ApplyFilters(
            List<LogEntry> src, string typeFilter, long? sinceFrame, string containsSubstr)
        {
            var result = new List<LogEntry>(src.Count);
            foreach (var e in src)
            {
                if (typeFilter != "all")
                {
                    var want = typeFilter;
                    var got  = e.type.ToLowerInvariant();
                    var match =
                        (want == "error"   && (got == "error" || got == "exception" || got == "assert")) ||
                        (want == "warning" && got == "warning") ||
                        (want == "info"    && got == "log") ||
                        (want == got);
                    if (!match) continue;
                }
                if (sinceFrame.HasValue && e.frame < sinceFrame.Value) continue;
                if (!string.IsNullOrEmpty(containsSubstr)
                    && e.message.IndexOf(containsSubstr, StringComparison.OrdinalIgnoreCase) < 0) continue;
                result.Add(e);
            }
            return result;
        }

        private static object ToDto(LogEntry e) => new
        {
            global_index = e.globalIndex,
            type         = e.type,
            message      = e.message,
            stack_trace  = e.stackTrace,
            timestamp_utc = e.timestampUtc,
            frame        = e.frame
        };
    }
}
