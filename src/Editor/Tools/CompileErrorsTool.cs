using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Structured access to current compile errors and a snapshot/diff
    /// surface so an agent can ask "what's new since I last looked?"
    /// instead of re-reading the whole console buffer and diffing strings
    /// in its head. Source: UnityEditor.LogEntries (internal) + parsing
    /// the Roslyn "Path(line,col): error CS####: message" line format.
    /// </summary>
    internal static class CompileErrorsTool
    {
        private const string KeySnapshot = "Reify.CompileErrorsSnapshotJson";

        // Format: "Assets\Foo\Bar.cs(12,34): error CS0102: ..."
        // Drive  : "C:\path\to\file.cs(12,34): warning CS0612: ..."
        private static readonly Regex CompileLineRx = new Regex(
            @"^(?<path>[^()]+)\((?<line>\d+),(?<col>\d+)\):\s+(?<sev>error|warning)\s+(?<code>[A-Z]+\d+):\s*(?<msg>.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ---------- compile-errors-structured ----------
        [ReifyTool("compile-errors-structured")]
        public static Task<object> Structured(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var snap = SnapshotCurrent();
                return new
                {
                    error_count           = snap.errors.Count,
                    warning_count         = snap.warnings.Count,
                    errors                = snap.errors,
                    warnings              = snap.warnings,
                    script_compilation_failed = EditorUtility.scriptCompilationFailed,
                    is_compiling          = EditorApplication.isCompiling,
                    read_at_utc           = DateTime.UtcNow.ToString("o"),
                    frame                 = (long)Time.frameCount
                };
            });
        }

        // ---------- compile-errors-snapshot ----------
        // Stores the current set of errors as the reference snapshot so
        // a later compile-errors-diff can answer "what changed since I
        // started this fix?".
        [ReifyTool("compile-errors-snapshot")]
        public static Task<object> Snapshot(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var snap = SnapshotCurrent();
                var json = JsonConvert.SerializeObject(snap);
                SessionState.SetString(KeySnapshot, json);
                return new
                {
                    snapshotted_error_count   = snap.errors.Count,
                    snapshotted_warning_count = snap.warnings.Count,
                    snapshot_taken_utc        = DateTime.UtcNow.ToString("o"),
                    note = "Use compile-errors-diff to compare future state against this snapshot."
                };
            });
        }

        // ---------- compile-errors-diff ----------
        [ReifyTool("compile-errors-diff")]
        public static Task<object> Diff(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var current = SnapshotCurrent();
                CompileErrorSnapshot baseline = null;
                var raw = SessionState.GetString(KeySnapshot, null);
                if (!string.IsNullOrEmpty(raw))
                {
                    try { baseline = JsonConvert.DeserializeObject<CompileErrorSnapshot>(raw); }
                    catch { baseline = null; }
                }
                if (baseline == null)
                    baseline = new CompileErrorSnapshot();

                var added     = new List<CompileError>();
                var resolved  = new List<CompileError>();
                var persisting = new List<CompileError>();

                var baseKeys    = new HashSet<string>();
                foreach (var e in baseline.errors)   baseKeys.Add(KeyOf(e));
                foreach (var w in baseline.warnings) baseKeys.Add(KeyOf(w));

                var currentKeys = new HashSet<string>();
                foreach (var e in current.errors)   currentKeys.Add(KeyOf(e));
                foreach (var w in current.warnings) currentKeys.Add(KeyOf(w));

                foreach (var e in current.errors)
                {
                    if (baseKeys.Contains(KeyOf(e))) persisting.Add(e);
                    else                              added.Add(e);
                }
                foreach (var e in baseline.errors)
                {
                    if (!currentKeys.Contains(KeyOf(e))) resolved.Add(e);
                }

                return new
                {
                    has_baseline          = !string.IsNullOrEmpty(raw),
                    baseline_error_count  = baseline.errors.Count,
                    current_error_count   = current.errors.Count,
                    added_count           = added.Count,
                    resolved_count        = resolved.Count,
                    persisting_count      = persisting.Count,
                    added,
                    resolved,
                    persisting,
                    read_at_utc           = DateTime.UtcNow.ToString("o"),
                    frame                 = (long)Time.frameCount
                };
            });
        }

        // ---------- helpers (also used by editor-await-compile) ----------
        public static CompileErrorSnapshot SnapshotCurrent()
        {
            var snap = new CompileErrorSnapshot();

            // UnityEditor.LogEntries is internal — reach via reflection.
            // The shape is: Start(), then GetCount(), then GetEntryInternal(i, entry).
            var t = Type.GetType("UnityEditor.LogEntries, UnityEditor");
            if (t == null) return snap;

            var entryT = Type.GetType("UnityEditor.LogEntry, UnityEditor");
            if (entryT == null) return snap;

            var startM = t.GetMethod("StartGettingEntries", BindingFlags.Public | BindingFlags.Static);
            var endM   = t.GetMethod("EndGettingEntries",   BindingFlags.Public | BindingFlags.Static);
            var countM = t.GetMethod("GetCount",             BindingFlags.Public | BindingFlags.Static);
            var getM   = t.GetMethod("GetEntryInternal",     BindingFlags.Public | BindingFlags.Static);
            if (startM == null || endM == null || countM == null || getM == null) return snap;

            var entry = Activator.CreateInstance(entryT);
            var msgF   = entryT.GetField("message", BindingFlags.Public | BindingFlags.Instance);
            var modeF  = entryT.GetField("mode",    BindingFlags.Public | BindingFlags.Instance);

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
                    bool isError = (mode & (int)LogModeFlags.Error) != 0
                                || (mode & (int)LogModeFlags.Fatal) != 0
                                || (mode & (int)LogModeFlags.AssetImportError) != 0
                                || (mode & (int)LogModeFlags.ScriptingError) != 0
                                || (mode & (int)LogModeFlags.ScriptCompileError) != 0
                                || (mode & (int)LogModeFlags.ScriptingException) != 0;
                    bool isWarning = (mode & (int)LogModeFlags.AssetImportWarning) != 0
                                || (mode & (int)LogModeFlags.ScriptingWarning) != 0
                                || (mode & (int)LogModeFlags.ScriptCompileWarning) != 0;

                    var firstLine = msg.Split('\n')[0];
                    var m = CompileLineRx.Match(firstLine);
                    if (!m.Success && !isError && !isWarning) continue;

                    var err = new CompileError
                    {
                        file     = m.Success ? m.Groups["path"].Value : null,
                        line     = m.Success ? int.Parse(m.Groups["line"].Value) : 0,
                        column   = m.Success ? int.Parse(m.Groups["col"].Value)  : 0,
                        code     = m.Success ? m.Groups["code"].Value : null,
                        severity = m.Success ? m.Groups["sev"].Value.ToLowerInvariant() : (isError ? "error" : "warning"),
                        message  = m.Success ? m.Groups["msg"].Value : firstLine,
                        raw      = firstLine
                    };

                    if (err.severity == "error") snap.errors.Add(err);
                    else                          snap.warnings.Add(err);
                }
            }
            finally
            {
                try { endM.Invoke(null, null); } catch { /* ignore */ }
            }
            return snap;
        }

        private static string KeyOf(CompileError e)
            => $"{e.file}|{e.line}|{e.column}|{e.code}|{e.message}";

        // Mode flags: from UnityEditor source. Subset that matters for compile errors.
        [Flags]
        private enum LogModeFlags
        {
            Error              = 1 << 0,
            Assert             = 1 << 1,
            Log                = 1 << 2,
            Fatal              = 1 << 4,
            AssetImportError   = 1 << 6,
            AssetImportWarning = 1 << 7,
            ScriptingError     = 1 << 8,
            ScriptingWarning   = 1 << 9,
            ScriptingLog       = 1 << 10,
            ScriptCompileError = 1 << 11,
            ScriptCompileWarning = 1 << 12,
            ScriptingException = 1 << 13
        }

        public sealed class CompileError
        {
            public string file;
            public int    line;
            public int    column;
            public string code;
            public string severity;
            public string message;
            public string raw;
        }

        public sealed class CompileErrorSnapshot
        {
            public List<CompileError> errors = new();
            public List<CompileError> warnings = new();
        }
    }
}
