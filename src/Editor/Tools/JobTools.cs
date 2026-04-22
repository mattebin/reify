using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Query and manage long-running jobs started by any reify tool that
    /// uses the shared ReifyJobs infrastructure. Pairs with build-execute
    /// (soon), asset-reimport (soon), lighting-bake (soon), etc. by
    /// returning a job_id the caller can poll.
    /// </summary>
    internal static class JobTools
    {
        // ---------- job-status ----------
        [ReifyTool("job-status")]
        public static Task<object> Status(JToken args)
        {
            var id = args?.Value<string>("job_id")
                ?? throw new ArgumentException("job_id is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var j = ReifyJobs.Get(id)
                    ?? throw new InvalidOperationException($"No job with id '{id}'.");
                return new
                {
                    job          = ReifyJobs.Serialize(j, includeResult: false, includeEvents: false),
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- job-result ----------
        [ReifyTool("job-result")]
        public static Task<object> Result(JToken args)
        {
            var id = args?.Value<string>("job_id")
                ?? throw new ArgumentException("job_id is required.");
            var includeEvents = args?.Value<bool?>("include_events") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var j = ReifyJobs.Get(id)
                    ?? throw new InvalidOperationException($"No job with id '{id}'.");
                if (j.state != ReifyJobs.State.Succeeded && j.state != ReifyJobs.State.Failed
                    && j.state != ReifyJobs.State.Cancelled)
                    throw new InvalidOperationException(
                        $"Job '{id}' is still {j.state}. Poll job-status until terminal.");

                return new
                {
                    job          = ReifyJobs.Serialize(j, includeResult: true, includeEvents: includeEvents),
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- job-list ----------
        [ReifyTool("job-list")]
        public static Task<object> List(JToken args)
        {
            var kind      = args?.Value<string>("kind");
            var stateArg  = args?.Value<string>("state");
            var limit     = args?.Value<int?>("limit") ?? 50;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var q = ReifyJobs.List();
                if (!string.IsNullOrEmpty(kind))
                    q = q.Where(j => string.Equals(j.kind, kind, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(stateArg))
                {
                    if (!Enum.TryParse<ReifyJobs.State>(stateArg, true, out var s))
                        throw new ArgumentException(
                            $"state '{stateArg}' is not valid (Pending|Running|Succeeded|Failed|Cancelled).");
                    q = q.Where(j => j.state == s);
                }
                var arr = q.OrderByDescending(j => j.started_utc).Take(limit)
                    .Select(j => ReifyJobs.Serialize(j, includeResult: false, includeEvents: false))
                    .ToArray();
                return new
                {
                    job_count   = arr.Length,
                    jobs        = arr,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }

        // ---------- job-cancel ----------
        [ReifyTool("job-cancel")]
        public static Task<object> Cancel(JToken args)
        {
            var id = args?.Value<string>("job_id")
                ?? throw new ArgumentException("job_id is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var j = ReifyJobs.Get(id)
                    ?? throw new InvalidOperationException($"No job with id '{id}'.");
                var beforeState = j.state.ToString();
                ReifyJobs.Cancel(j);
                return new
                {
                    job = ReifyJobs.Serialize(j, includeResult: false, includeEvents: false),
                    applied_fields = new object[]
                    {
                        new { field = "state", before = beforeState, after = j.state.ToString() }
                    },
                    applied_count = 1,
                    note = "Cooperative cancellation — the running tool must check job.state " +
                           "to actually stop. Some backends (Unity BuildPipeline, bake) can't " +
                           "be pre-empted and will finish anyway.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }
    }
}
