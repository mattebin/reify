using System;
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
    /// Bucket the live Unity console by message-shape so a 1352-line spam
    /// of the same exception collapses into one row with count=1352. The
    /// shape key is the message stripped of varying numbers and quoted
    /// strings — works for "InvalidOperationException at frame 1234" type
    /// noise while still distinguishing genuinely different messages.
    /// </summary>
    internal static class ConsoleLogSummarizeTool
    {
        // ---------- console-log-summarize ----------
        [ReifyTool("console-log-summarize")]
        public static Task<object> Summarize(JToken args)
        {
            var typeFilter = (args?.Value<string>("type_filter") ?? "all").ToLowerInvariant();
            var maxGroups  = Math.Clamp(args?.Value<int?>("max_groups") ?? 50, 1, 500);
            var stripDigits = args?.Value<bool?>("strip_digits") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var entries = ReadAllEntries();
                var groups  = new Dictionary<string, GroupAccumulator>(StringComparer.Ordinal);
                int totalIn = 0;

                foreach (var e in entries)
                {
                    if (!MatchesType(e.type, typeFilter)) continue;
                    totalIn++;
                    var key = ShapeKey(e.message, stripDigits);
                    if (!groups.TryGetValue(key, out var g))
                        groups[key] = g = new GroupAccumulator { sample = e.message, types = new HashSet<string>() };
                    g.count++;
                    g.types.Add(e.type);
                    if (e.firstSeenUtc == null) g.firstSeenUtc = e.timestampUtc;
                    g.lastSeenUtc = e.timestampUtc;
                }

                var ordered = groups
                    .OrderByDescending(kv => kv.Value.count)
                    .Take(maxGroups)
                    .Select(kv => new
                    {
                        count = kv.Value.count,
                        types = kv.Value.types.ToArray(),
                        sample = kv.Value.sample,
                        first_seen_utc = kv.Value.firstSeenUtc,
                        last_seen_utc  = kv.Value.lastSeenUtc,
                        shape_key = kv.Key
                    })
                    .ToArray();

                return new
                {
                    total_console_entries = entries.Count,
                    total_after_filter    = totalIn,
                    distinct_groups       = groups.Count,
                    returned_groups       = ordered.Length,
                    truncated             = groups.Count > ordered.Length,
                    type_filter           = typeFilter,
                    groups                = ordered,
                    read_at_utc           = DateTime.UtcNow.ToString("o"),
                    frame                 = (long)Time.frameCount
                };
            });
        }

        private static bool MatchesType(string entryType, string filter)
        {
            if (filter == "all") return true;
            var got = entryType.ToLowerInvariant();
            if (filter == "error")   return got == "error" || got == "exception" || got == "assert";
            if (filter == "warning") return got == "warning";
            if (filter == "info")    return got == "log";
            return got == filter;
        }

        private static string ShapeKey(string message, bool stripDigits)
        {
            if (string.IsNullOrEmpty(message)) return "";
            var firstLine = message.Split('\n')[0];
            var sb = new System.Text.StringBuilder(firstLine.Length);
            foreach (var c in firstLine)
            {
                if (stripDigits && char.IsDigit(c)) sb.Append('#');
                else sb.Append(c);
            }
            // Collapse runs of #'s
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "#+", "#");
        }

        private static List<EntryLite> ReadAllEntries()
        {
            var result = new List<EntryLite>();
            var t = Type.GetType("UnityEditor.LogEntries, UnityEditor");
            if (t == null) return result;
            var entryT = Type.GetType("UnityEditor.LogEntry, UnityEditor");
            if (entryT == null) return result;

            var startM = t.GetMethod("StartGettingEntries", BindingFlags.Public | BindingFlags.Static);
            var endM   = t.GetMethod("EndGettingEntries",   BindingFlags.Public | BindingFlags.Static);
            var countM = t.GetMethod("GetCount",             BindingFlags.Public | BindingFlags.Static);
            var getM   = t.GetMethod("GetEntryInternal",     BindingFlags.Public | BindingFlags.Static);
            if (startM == null || endM == null || countM == null || getM == null) return result;

            var entry = Activator.CreateInstance(entryT);
            var msgF  = entryT.GetField("message", BindingFlags.Public | BindingFlags.Instance);
            var modeF = entryT.GetField("mode",    BindingFlags.Public | BindingFlags.Instance);

            try
            {
                startM.Invoke(null, null);
                int count = (int)countM.Invoke(null, null);
                for (int i = 0; i < count; i++)
                {
                    bool ok = (bool)getM.Invoke(null, new object[] { i, entry });
                    if (!ok) continue;
                    var msg = msgF?.GetValue(entry) as string ?? "";
                    int mode = modeF != null ? Convert.ToInt32(modeF.GetValue(entry)) : 0;
                    string typeStr = ((mode & 0x1) != 0) ? "Error"
                                   : ((mode & 0x100) != 0) ? "ScriptingError"
                                   : ((mode & 0x200) != 0) ? "Warning"
                                   : ((mode & 0x2) != 0)   ? "Assert"
                                   : "Log";
                    result.Add(new EntryLite
                    {
                        message = msg,
                        type    = typeStr,
                        timestampUtc = DateTime.UtcNow.ToString("o")
                    });
                }
            }
            finally
            {
                try { endM.Invoke(null, null); } catch { /* ignore */ }
            }
            return result;
        }

        private class EntryLite
        {
            public string message;
            public string type;
            public string timestampUtc;
            public string firstSeenUtc;
        }

        private class GroupAccumulator
        {
            public int     count;
            public string  sample;
            public HashSet<string> types;
            public string  firstSeenUtc;
            public string  lastSeenUtc;
        }
    }
}
