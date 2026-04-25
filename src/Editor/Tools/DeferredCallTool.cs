using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Queues a tool call to fire after the next domain reload completes.
    /// Solves the "I asked Unity to recompile and reload, my call vanished
    /// in the dead zone" problem. The queue persists in SessionState so it
    /// survives the very domain reload it's waiting on. Results land in a
    /// completion bucket that editor-call-deferred-results drains.
    /// </summary>
    [InitializeOnLoad]
    internal static class DeferredCallTool
    {
        private const string KeyPending  = "Reify.DeferredCalls.Pending";
        private const string KeyResults  = "Reify.DeferredCalls.Results";

        static DeferredCallTool()
        {
            // After every domain reload the static ctor runs. Drain pending
            // calls that were queued before the reload.
            EditorApplication.delayCall += DrainPending;
        }

        // ---------- editor-call-deferred ----------
        [ReifyTool("editor-call-deferred")]
        public static Task<object> Defer(JToken args)
        {
            var tool = args?.Value<string>("tool")
                ?? throw new ArgumentException("tool is required (the kebab-case name to invoke later).");
            var inner = args?["args"] ?? new JObject();
            var fireAfterReloads = Math.Clamp(args?.Value<int?>("fire_after_reloads") ?? 1, 1, 10);

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var pending = LoadPending();
                var item = new DeferredCall
                {
                    Id                   = Guid.NewGuid().ToString("N"),
                    Tool                 = tool,
                    ArgsJson             = inner.ToString(Formatting.None),
                    QueuedUtc            = DateTime.UtcNow.ToString("o"),
                    RemainingReloadCount = fireAfterReloads
                };
                pending.Add(item);
                SavePending(pending);
                return new
                {
                    queued      = true,
                    id          = item.Id,
                    tool        = item.Tool,
                    fires_after_reloads = item.RemainingReloadCount,
                    pending_count = pending.Count,
                    note = "Call editor-call-deferred-results with this id after the targeted reload to fetch the result."
                };
            });
        }

        // ---------- editor-call-deferred-list ----------
        [ReifyTool("editor-call-deferred-list")]
        public static Task<object> List(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var pending = LoadPending();
                var results = LoadResults();
                return new
                {
                    pending_count   = pending.Count,
                    results_count   = results.Count,
                    pending = pending.Select(p => new
                    {
                        id = p.Id, tool = p.Tool, queued_utc = p.QueuedUtc, fires_after_reloads = p.RemainingReloadCount
                    }).ToArray(),
                    results = results.Select(r => new
                    {
                        id = r.Id, tool = r.Tool, status = r.Status, completed_utc = r.CompletedUtc, error = r.Error
                    }).ToArray()
                };
            });
        }

        // ---------- editor-call-deferred-results ----------
        [ReifyTool("editor-call-deferred-results")]
        public static Task<object> Results(JToken args)
        {
            var id = args?.Value<string>("id");
            var consume = args?.Value<bool?>("consume") ?? true;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var results = LoadResults();
                var match = id == null
                    ? results
                    : results.Where(r => r.Id == id).ToList();

                object payload = match.Select(r => new
                {
                    id = r.Id,
                    tool = r.Tool,
                    status = r.Status,
                    completed_utc = r.CompletedUtc,
                    error = r.Error,
                    result_json = r.ResultJson
                }).ToArray();

                if (consume && id != null)
                {
                    results.RemoveAll(r => r.Id == id);
                    SaveResults(results);
                }
                else if (consume && id == null)
                {
                    results.Clear();
                    SaveResults(results);
                }

                return new { returned_count = match.Count, consumed = consume, results = payload };
            });
        }

        // ---------- editor-call-deferred-cancel ----------
        [ReifyTool("editor-call-deferred-cancel")]
        public static Task<object> Cancel(JToken args)
        {
            var id = args?.Value<string>("id") ?? throw new ArgumentException("id is required.");
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var pending = LoadPending();
                int removed = pending.RemoveAll(p => p.Id == id);
                SavePending(pending);
                return new { id, cancelled = removed > 0, remaining_pending = pending.Count };
            });
        }

        private static void DrainPending()
        {
            // Each domain reload ticks down RemainingReloadCount. When it
            // reaches zero, fire the call inline and bucket the result.
            var pending = LoadPending();
            if (pending.Count == 0) return;

            var stillPending = new List<DeferredCall>();
            var results = LoadResults();

            foreach (var p in pending)
            {
                p.RemainingReloadCount--;
                if (p.RemainingReloadCount > 0)
                {
                    stillPending.Add(p);
                    continue;
                }
                // Fire now.
                var record = new DeferredResult
                {
                    Id   = p.Id,
                    Tool = p.Tool,
                    CompletedUtc = DateTime.UtcNow.ToString("o")
                };
                try
                {
                    JToken parsedArgs = JToken.Parse(string.IsNullOrEmpty(p.ArgsJson) ? "{}" : p.ArgsJson);
                    if (Bridge.ReifyBridge.TryGetHandler(p.Tool, out var handler))
                    {
                        var obj = handler(parsedArgs).GetAwaiter().GetResult();
                        record.Status = "ok";
                        record.ResultJson = JsonConvert.SerializeObject(obj);
                    }
                    else
                    {
                        record.Status = "failed";
                        record.Error  = $"Unknown tool '{p.Tool}'.";
                    }
                }
                catch (Exception ex)
                {
                    record.Status = "failed";
                    record.Error  = ex.Message;
                }
                results.Add(record);
                if (results.Count > 100) results.RemoveRange(0, results.Count - 100);
            }

            SavePending(stillPending);
            SaveResults(results);
        }

        private static List<DeferredCall> LoadPending()
        {
            var raw = SessionState.GetString(KeyPending, "");
            if (string.IsNullOrEmpty(raw)) return new List<DeferredCall>();
            try { return JsonConvert.DeserializeObject<List<DeferredCall>>(raw) ?? new(); }
            catch { return new List<DeferredCall>(); }
        }
        private static void SavePending(List<DeferredCall> list)
            => SessionState.SetString(KeyPending, JsonConvert.SerializeObject(list));

        private static List<DeferredResult> LoadResults()
        {
            var raw = SessionState.GetString(KeyResults, "");
            if (string.IsNullOrEmpty(raw)) return new List<DeferredResult>();
            try { return JsonConvert.DeserializeObject<List<DeferredResult>>(raw) ?? new(); }
            catch { return new List<DeferredResult>(); }
        }
        private static void SaveResults(List<DeferredResult> list)
            => SessionState.SetString(KeyResults, JsonConvert.SerializeObject(list));

        private class DeferredCall
        {
            public string Id;
            public string Tool;
            public string ArgsJson;
            public string QueuedUtc;
            public int    RemainingReloadCount;
        }

        private class DeferredResult
        {
            public string Id;
            public string Tool;
            public string Status;
            public string Error;
            public string ResultJson;
            public string CompletedUtc;
        }
    }
}
