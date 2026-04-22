using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Shared long-running-job infrastructure. Builds, reimports, bakes,
    /// memory snapshots, etc. take seconds to minutes — longer than the
    /// MCP request timeout a caller typically allows. Rather than blocking
    /// the request until done, the tool registers a Job and returns a
    /// job_id immediately. Callers poll `job-status` / `job-result` until
    /// finished.
    ///
    /// Mirrors the pattern that `tests-run` / `tests-status` / `tests-results`
    /// already use, but generalised so every domain can share it.
    /// </summary>
    internal static class ReifyJobs
    {
        public enum State { Pending, Running, Succeeded, Failed, Cancelled }

        public sealed class Job
        {
            public string   id;
            public string   kind;                // "build", "import", "bake", "memory-snapshot", ...
            public State    state;
            public string   message;             // last progress line
            public float    progress01;          // 0..1 or -1 = indeterminate
            public DateTime started_utc;
            public DateTime? finished_utc;
            public object   result;              // final payload on success
            public string   error;               // message on failure
            public List<object> events;          // optional progress event log
        }

        private static readonly ConcurrentDictionary<string, Job> Jobs = new();

        public static Job Start(string kind)
        {
            var job = new Job
            {
                id          = Guid.NewGuid().ToString("N").Substring(0, 12),
                kind        = kind,
                state       = State.Pending,
                progress01  = -1f,
                started_utc = DateTime.UtcNow,
                events      = new List<object>()
            };
            Jobs[job.id] = job;
            return job;
        }

        public static Job Get(string id)
        {
            return Jobs.TryGetValue(id, out var j) ? j : null;
        }

        public static IEnumerable<Job> List() => Jobs.Values;

        public static void SetRunning(Job j, string message = null, float progress01 = -1f)
        {
            j.state = State.Running;
            if (message != null) j.message = message;
            j.progress01 = progress01;
            j.events.Add(new
            {
                at_utc    = DateTime.UtcNow.ToString("o"),
                state     = j.state.ToString(),
                message,
                progress01
            });
        }

        public static void Succeed(Job j, object result)
        {
            j.state = State.Succeeded;
            j.result = result;
            j.finished_utc = DateTime.UtcNow;
            j.progress01 = 1f;
            j.events.Add(new
            {
                at_utc = DateTime.UtcNow.ToString("o"),
                state  = j.state.ToString(),
                message = "succeeded"
            });
        }

        public static void Fail(Job j, string error)
        {
            j.state = State.Failed;
            j.error = error;
            j.finished_utc = DateTime.UtcNow;
            j.events.Add(new
            {
                at_utc = DateTime.UtcNow.ToString("o"),
                state  = j.state.ToString(),
                message = error
            });
        }

        public static void Cancel(Job j)
        {
            if (j.state == State.Succeeded || j.state == State.Failed) return;
            j.state = State.Cancelled;
            j.finished_utc = DateTime.UtcNow;
            j.events.Add(new
            {
                at_utc = DateTime.UtcNow.ToString("o"),
                state  = j.state.ToString()
            });
        }

        public static object Serialize(Job j, bool includeResult = true, bool includeEvents = false) => new
        {
            job_id        = j.id,
            kind          = j.kind,
            state         = j.state.ToString(),
            progress01    = j.progress01,
            message       = j.message,
            started_utc   = j.started_utc.ToString("o"),
            finished_utc  = j.finished_utc?.ToString("o"),
            duration_ms   = j.finished_utc.HasValue
                              ? (long)(j.finished_utc.Value - j.started_utc).TotalMilliseconds
                              : (long)(DateTime.UtcNow - j.started_utc).TotalMilliseconds,
            result        = includeResult ? j.result : null,
            error         = j.error,
            event_count   = j.events.Count,
            events        = includeEvents ? j.events.ToArray() : null
        };
    }
}
