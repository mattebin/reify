using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Async Unity Test Runner surface. test runs return a job id immediately,
    /// status/results are polled separately, and result pages stay bounded.
    /// </summary>
    [InitializeOnLoad]
    internal static class TestRunnerTools
    {
        private const int DefaultResultPageSize = 100;
        private const int DefaultLogPageSize = 100;
        private const int DefaultMaxStoredLogs = 200;
        private static readonly object Sync = new();
        private static readonly Dictionary<string, TestRunJob> Jobs = new(StringComparer.Ordinal);

        private static TestRunnerApi _api;
        private static ReifyTestCallbacks _callbacks;
        private static bool _callbacksRegistered;
        private static string _activeJobId;

        static TestRunnerTools()
        {
            EnsureApi();
        }

        [ReifyTool("tests-list")]
        public static Task<object> List(JToken args)
        {
            var mode = ParseMode(args?.Value<string>("mode"), defaultMode: TestMode.EditMode);
            var assemblyName = NullIfEmpty(args?.Value<string>("assembly_name"));
            var namespaceName = NullIfEmpty(args?.Value<string>("namespace_name"));
            var className = NullIfEmpty(args?.Value<string>("class_name"));
            var methodName = NullIfEmpty(args?.Value<string>("method_name"));
            var limit = Math.Max(1, args?.Value<int?>("limit") ?? 200);
            var offset = Math.Max(0, args?.Value<int?>("offset") ?? 0);

            return MainThreadDispatcher.RunAsync<object>(async () =>
            {
                EnsureApi();
                var leaves = await RetrieveLeafTests(mode);
                var filtered = leaves
                    .Where(t => MatchesFilter(t, assemblyName, namespaceName, className, methodName))
                    .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var page = filtered
                    .Skip(offset)
                    .Take(limit)
                    .Select(t => new
                    {
                        full_name = t.FullName,
                        name = t.Name,
                        class_name = ExtractClassName(t.FullName),
                        namespace_name = ExtractNamespace(t.FullName),
                        assembly_name = ExtractAssemblyName(t.UniqueName),
                        unique_name = t.UniqueName
                    })
                    .ToArray();

                return new
                {
                    mode = mode.ToString(),
                    assembly_name = assemblyName,
                    namespace_name = namespaceName,
                    class_name = className,
                    method_name = methodName,
                    total_matching = filtered.Count,
                    returned = page.Length,
                    limit,
                    offset,
                    truncated = offset + page.Length < filtered.Count,
                    tests = page,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("tests-run")]
        public static Task<object> Run(JToken args)
        {
            var mode = ParseMode(args?.Value<string>("mode"), defaultMode: TestMode.EditMode);
            var assemblyName = NullIfEmpty(args?.Value<string>("assembly_name"));
            var namespaceName = NullIfEmpty(args?.Value<string>("namespace_name"));
            var className = NullIfEmpty(args?.Value<string>("class_name"));
            var methodName = NullIfEmpty(args?.Value<string>("method_name"));
            var includePassingTests = args?.Value<bool?>("include_passing_tests") ?? false;
            var includeLogs = args?.Value<bool?>("include_logs") ?? false;
            var includeStackTraces = args?.Value<bool?>("include_stack_traces") ?? false;
            var maxStoredLogs = Math.Max(0, args?.Value<int?>("max_logs") ?? DefaultMaxStoredLogs);

            return MainThreadDispatcher.RunAsync<object>(async () =>
            {
                EnsureApi();
                ThrowIfTestsUnsafeToStart();

                lock (Sync)
                {
                    if (!string.IsNullOrEmpty(_activeJobId) &&
                        Jobs.TryGetValue(_activeJobId, out var active) &&
                        active.IsTerminal == false)
                    {
                        throw new InvalidOperationException(
                            $"A test run is already active (job_id: {_activeJobId}). Poll tests-status/results or cancel it before starting another.");
                    }
                }

                var leaves = await RetrieveLeafTests(mode);
                var matching = leaves
                    .Where(t => MatchesFilter(t, assemblyName, namespaceName, className, methodName))
                    .ToList();
                if (matching.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No tests matched the requested filters. Use tests-list to inspect what is available.");
                }

                var filter = BuildFilter(mode, assemblyName, namespaceName, className, methodName);
                var options = new TestRunOptions(
                    mode,
                    assemblyName,
                    namespaceName,
                    className,
                    methodName,
                    includePassingTests,
                    includeLogs,
                    includeStackTraces,
                    maxStoredLogs);

                var job = CreateJob(options, matching.Count);
                lock (Sync)
                {
                    _activeJobId = job.JobId;
                }

                try
                {
                    _api.Execute(new ExecutionSettings(filter));
                }
                catch
                {
                    MarkJobFailed(job, "Failed to start Unity Test Runner execution.");
                    lock (Sync)
                    {
                        if (_activeJobId == job.JobId)
                            _activeJobId = null;
                    }
                    throw;
                }

                return BuildJobSnapshot(job, includeResultsSummary: true);
            });
        }

        [ReifyTool("tests-status")]
        public static Task<object> Status(JToken args)
        {
            var jobId = args?.Value<string>("job_id")
                ?? throw new ArgumentException("job_id is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var job = GetJob(jobId);
                return BuildJobSnapshot(job, includeResultsSummary: true);
            });
        }

        [ReifyTool("tests-results")]
        public static Task<object> Results(JToken args)
        {
            var jobId = args?.Value<string>("job_id")
                ?? throw new ArgumentException("job_id is required.");
            var offset = Math.Max(0, args?.Value<int?>("offset") ?? 0);
            var limit = Math.Max(1, args?.Value<int?>("limit") ?? DefaultResultPageSize);
            var includeLogs = args?.Value<bool?>("include_logs") ?? false;
            var logOffset = Math.Max(0, args?.Value<int?>("log_offset") ?? 0);
            var logLimit = Math.Max(1, args?.Value<int?>("log_limit") ?? DefaultLogPageSize);

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var job = GetJob(jobId);
                List<TestCaseRecord> results;
                List<TestLogRecord> logs;
                lock (job.Sync)
                {
                    results = job.Results.ToList();
                    logs = job.Logs.ToList();
                }

                var resultPage = results.Skip(offset).Take(limit).ToArray();
                var logPage = includeLogs
                    ? logs.Skip(logOffset).Take(logLimit).ToArray()
                    : Array.Empty<TestLogRecord>();

                return new
                {
                    job_id = job.JobId,
                    status = job.Status,
                    summary = job.Summary,
                    stored_result_count = results.Count,
                    returned_result_count = resultPage.Length,
                    offset,
                    limit,
                    truncated_results = offset + resultPage.Length < results.Count,
                    results = resultPage,
                    stored_log_count = logs.Count,
                    returned_log_count = logPage.Length,
                    log_offset = includeLogs ? logOffset : 0,
                    log_limit = includeLogs ? logLimit : 0,
                    truncated_logs = includeLogs && logOffset + logPage.Length < logs.Count,
                    logs = logPage,
                    completed_at_utc = job.CompletedAtUtc,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("tests-cancel")]
        public static Task<object> Cancel(JToken args)
        {
            var jobId = args?.Value<string>("job_id")
                ?? throw new ArgumentException("job_id is required.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var job = GetJob(jobId);
                if (job.IsTerminal)
                {
                    return new
                    {
                        job_id = job.JobId,
                        status = job.Status,
                        cancelled = false,
                        already_terminal = true,
                        read_at_utc = DateTime.UtcNow.ToString("o"),
                        frame = (long)Time.frameCount
                    };
                }

                job.CancelRequested = true;
                var cancelled = TryCancelCurrentRun();
                if (cancelled)
                {
                    job.Status = "cancelling";
                    job.Note = "Cancellation requested from Unity Test Runner.";
                }

                return new
                {
                    job_id = job.JobId,
                    status = job.Status,
                    cancelled,
                    note = cancelled
                        ? "Unity Test Runner cancellation was requested."
                        : "This Unity version/TestRunner API does not expose a cancellation method.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)Time.frameCount
                };
            });
        }

        internal static void OnRunStarted(ITestAdaptor testsToRun)
        {
            var job = GetActiveJobOrNull();
            if (job == null)
                return;

            lock (job.Sync)
            {
                job.Status = "running";
                job.StartedAtUtc = DateTime.UtcNow.ToString("o");
                job.Summary.TotalTests = CountLeafTests(testsToRun);
                job.Note = $"Unity Test Runner started {job.Options.Mode}.";
                job.Results.Clear();
                job.Logs.Clear();
            }

            if (job.Options.IncludeLogs)
            {
                Application.logMessageReceivedThreaded -= OnLogMessageReceived;
                Application.logMessageReceivedThreaded += OnLogMessageReceived;
            }
        }

        internal static void OnTestFinished(ITestResultAdaptor result)
        {
            if (result.Test == null || result.Test.IsSuite)
                return;

            var job = GetActiveJobOrNull();
            if (job == null)
                return;

            var status = result.TestStatus.ToString();
            var isPassing = string.Equals(status, "Passed", StringComparison.OrdinalIgnoreCase);
            var record = new TestCaseRecord
            {
                FullName = result.Test.FullName,
                Name = result.Test.Name,
                ClassName = ExtractClassName(result.Test.FullName),
                NamespaceName = ExtractNamespace(result.Test.FullName),
                AssemblyName = ExtractAssemblyName(result.Test.UniqueName),
                Status = status,
                DurationSeconds = result.Duration,
                Message = string.IsNullOrWhiteSpace(result.Message) ? null : result.Message,
                StackTrace = job.Options.IncludeStackTraces && !string.IsNullOrWhiteSpace(result.StackTrace)
                    ? result.StackTrace
                    : null
            };

            lock (job.Sync)
            {
                if (job.Options.IncludePassingTests || !isPassing)
                    job.Results.Add(record);

                if (string.Equals(status, "Passed", StringComparison.OrdinalIgnoreCase))
                    job.Summary.PassedTests++;
                else if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
                    job.Summary.FailedTests++;
                else if (string.Equals(status, "Skipped", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(status, "Inconclusive", StringComparison.OrdinalIgnoreCase))
                    job.Summary.SkippedTests++;
            }
        }

        internal static void OnRunFinished(ITestResultAdaptor result)
        {
            var job = GetActiveJobOrNull();
            if (job == null)
                return;

            Application.logMessageReceivedThreaded -= OnLogMessageReceived;

            lock (job.Sync)
            {
                job.CompletedAtUtc = DateTime.UtcNow.ToString("o");
                job.Summary.DurationSeconds = result.Duration;
                if (job.CancelRequested)
                {
                    job.Status = "cancelled";
                    job.Note = "Unity Test Runner finished after a cancellation request.";
                }
                else if (job.Summary.FailedTests > 0)
                {
                    job.Status = "failed";
                    job.Note = "One or more tests failed.";
                }
                else
                {
                    job.Status = "completed";
                    job.Note = "Unity Test Runner completed successfully.";
                }
            }

            lock (Sync)
            {
                if (_activeJobId == job.JobId)
                    _activeJobId = null;
            }
        }

        internal static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            var job = GetActiveJobOrNull();
            if (job == null || !job.Options.IncludeLogs)
                return;

            var entry = new TestLogRecord
            {
                Type = type.ToString(),
                Message = condition,
                StackTrace = job.Options.IncludeStackTraces && !string.IsNullOrWhiteSpace(stackTrace)
                    ? stackTrace
                    : null,
                TimestampUtc = DateTime.UtcNow.ToString("o")
            };

            lock (job.Sync)
            {
                job.Logs.Add(entry);
                if (job.Logs.Count > job.Options.MaxStoredLogs)
                    job.Logs.RemoveAt(0);
            }
        }

        private static void EnsureApi()
        {
            if (_api == null)
                _api = ScriptableObject.CreateInstance<TestRunnerApi>();

            if (_callbacks == null)
            {
                _callbacks = ScriptableObject.CreateInstance<ReifyTestCallbacks>();
                _callbacks.hideFlags = HideFlags.HideAndDontSave;
            }

            if (!_callbacksRegistered)
            {
                _api.RegisterCallbacks(_callbacks);
                _callbacksRegistered = true;
            }
        }

        private static TestRunJob CreateJob(TestRunOptions options, int matchingTestCount)
        {
            var job = new TestRunJob
            {
                JobId = Guid.NewGuid().ToString("N"),
                Status = "queued",
                RequestedAtUtc = DateTime.UtcNow.ToString("o"),
                Options = options,
                Summary = new TestRunSummary
                {
                    MatchingTests = matchingTestCount,
                    TotalTests = matchingTestCount
                },
                Note = "Queued in Unity Test Runner."
            };

            lock (Sync)
            {
                Jobs[job.JobId] = job;
                TrimCompletedJobs();
            }

            return job;
        }

        private static void TrimCompletedJobs()
        {
            var terminalJobs = Jobs.Values
                .Where(j => j.IsTerminal)
                .OrderByDescending(j => j.CompletedAtUtc, StringComparer.Ordinal)
                .Skip(12)
                .ToArray();

            foreach (var job in terminalJobs)
                Jobs.Remove(job.JobId);
        }

        private static object BuildJobSnapshot(TestRunJob job, bool includeResultsSummary)
        {
            lock (job.Sync)
            {
                return new
                {
                    job_id = job.JobId,
                    status = job.Status,
                    note = job.Note,
                    requested_at_utc = job.RequestedAtUtc,
                    started_at_utc = job.StartedAtUtc,
                    completed_at_utc = job.CompletedAtUtc,
                    mode = job.Options.Mode.ToString(),
                    filters = new
                    {
                        assembly_name = job.Options.AssemblyName,
                        namespace_name = job.Options.NamespaceName,
                        class_name = job.Options.ClassName,
                        method_name = job.Options.MethodName
                    },
                    summary = job.Summary,
                    stored_result_count = includeResultsSummary ? job.Results.Count : 0,
                    stored_log_count = includeResultsSummary ? job.Logs.Count : 0,
                    cancel_requested = job.CancelRequested,
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame = (long)Time.frameCount
                };
            }
        }

        private static TestRunJob GetJob(string jobId)
        {
            lock (Sync)
            {
                if (Jobs.TryGetValue(jobId, out var job))
                    return job;
            }
            throw new InvalidOperationException($"No test job with id '{jobId}'.");
        }

        private static TestRunJob GetActiveJobOrNull()
        {
            lock (Sync)
            {
                if (string.IsNullOrEmpty(_activeJobId))
                    return null;
                return Jobs.TryGetValue(_activeJobId, out var job) ? job : null;
            }
        }

        private static void MarkJobFailed(TestRunJob job, string message)
        {
            lock (job.Sync)
            {
                job.Status = "failed";
                job.CompletedAtUtc = DateTime.UtcNow.ToString("o");
                job.Note = message;
            }
        }

        private static async Task<List<TestLeafInfo>> RetrieveLeafTests(TestMode mode)
        {
            var tcs = new TaskCompletionSource<ITestAdaptor>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _api.RetrieveTestList(mode, root => tcs.TrySetResult(root));

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != tcs.Task)
                throw new TimeoutException("Unity Test Runner did not return the test list within 10 seconds.");

            var root = await tcs.Task;
            var list = new List<TestLeafInfo>();
            if (root != null)
                CollectLeafTests(root, list);
            return list;
        }

        private static void CollectLeafTests(ITestAdaptor node, List<TestLeafInfo> list)
        {
            if (node == null)
                return;

            if (!node.IsSuite)
            {
                list.Add(new TestLeafInfo
                {
                    FullName = node.FullName,
                    Name = node.Name,
                    UniqueName = node.UniqueName
                });
            }

            if (node.HasChildren)
            {
                foreach (var child in node.Children)
                    CollectLeafTests(child, list);
            }
        }

        private static int CountLeafTests(ITestAdaptor node)
        {
            if (node == null) return 0;
            if (!node.IsSuite) return 1;
            var count = 0;
            if (node.HasChildren)
            {
                foreach (var child in node.Children)
                    count += CountLeafTests(child);
            }
            return count;
        }

        private static bool MatchesFilter(
            TestLeafInfo test,
            string assemblyName,
            string namespaceName,
            string className,
            string methodName)
        {
            if (!string.IsNullOrEmpty(assemblyName) &&
                !string.Equals(ExtractAssemblyName(test.UniqueName), assemblyName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(namespaceName))
            {
                var actualNamespace = ExtractNamespace(test.FullName);
                if (!string.Equals(actualNamespace, namespaceName, StringComparison.OrdinalIgnoreCase) &&
                    !(actualNamespace?.StartsWith(namespaceName + ".", StringComparison.OrdinalIgnoreCase) ?? false))
                    return false;
            }

            if (!string.IsNullOrEmpty(className) &&
                !string.Equals(ExtractClassName(test.FullName), className, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(methodName) &&
                !string.Equals(test.FullName, methodName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(test.Name, methodName, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static Filter BuildFilter(
            TestMode mode,
            string assemblyName,
            string namespaceName,
            string className,
            string methodName)
        {
            var filter = new Filter { testMode = mode };
            if (!string.IsNullOrEmpty(assemblyName))
                filter.assemblyNames = new[] { assemblyName };

            var groupNames = new List<string>();
            if (!string.IsNullOrEmpty(namespaceName))
                groupNames.Add($"^{EscapeRegex(namespaceName)}\\.");
            if (!string.IsNullOrEmpty(className))
                groupNames.Add($"^.*\\.{EscapeRegex(className)}\\.[^\\.]+$");
            if (groupNames.Count > 0)
                filter.groupNames = groupNames.ToArray();

            if (!string.IsNullOrEmpty(methodName))
                filter.testNames = new[] { methodName };

            return filter;
        }

        private static string EscapeRegex(string value)
            => System.Text.RegularExpressions.Regex.Escape(value);

        private static TestMode ParseMode(string value, TestMode defaultMode)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultMode;
            if (Enum.TryParse(value, true, out TestMode parsed))
                return parsed;
            throw new ArgumentException($"Unknown test mode '{value}'. Valid values include EditMode and PlayMode.");
        }

        private static string NullIfEmpty(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string ExtractAssemblyName(string uniqueName)
        {
            if (string.IsNullOrWhiteSpace(uniqueName))
                return null;
            var index = uniqueName.IndexOf(".dll", StringComparison.OrdinalIgnoreCase);
            return index > 0 ? uniqueName[..index] : null;
        }

        private static string ExtractNamespace(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;
            var lastDot = fullName.LastIndexOf('.');
            if (lastDot <= 0)
                return null;
            var beforeMethod = fullName[..lastDot];
            var classDot = beforeMethod.LastIndexOf('.');
            return classDot <= 0 ? null : beforeMethod[..classDot];
        }

        private static string ExtractClassName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;
            var lastDot = fullName.LastIndexOf('.');
            if (lastDot <= 0)
                return null;
            var beforeMethod = fullName[..lastDot];
            var classDot = beforeMethod.LastIndexOf('.');
            return classDot < 0 ? beforeMethod : beforeMethod[(classDot + 1)..];
        }

        private static void ThrowIfTestsUnsafeToStart()
        {
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Unity is compiling scripts. Wait for compilation to finish before running tests.");
            if (EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Unity currently has script compilation errors. Fix them before running tests.");

            var dirtyScenes = new List<Scene>();
            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (scene.isDirty)
                    dirtyScenes.Add(scene);
            }
            if (dirtyScenes.Count == 0)
                return;

            var details = string.Join(", ", dirtyScenes.Select(s =>
            {
                var name = string.IsNullOrEmpty(s.name) ? "(untitled)" : s.name;
                var path = string.IsNullOrEmpty(s.path) ? "(unsaved)" : s.path;
                return $"'{name}' ({path})";
            }));
            throw new InvalidOperationException(
                $"Cannot run tests while open scenes are dirty: {details}. Save the scene(s) and retry.");
        }

        private static bool TryCancelCurrentRun()
        {
            var method = typeof(TestRunnerApi).GetMethod(
                "CancelTestRun",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (method == null)
                return false;

            method.Invoke(_api, null);
            return true;
        }
    }

    internal sealed class ReifyTestCallbacks : ScriptableObject, ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) => TestRunnerTools.OnRunStarted(testsToRun);
        public void RunFinished(ITestResultAdaptor result) => TestRunnerTools.OnRunFinished(result);
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) => TestRunnerTools.OnTestFinished(result);
    }

    [Serializable]
    internal sealed class TestRunJob
    {
        public readonly object Sync = new();
        public string JobId;
        public string Status;
        public string RequestedAtUtc;
        public string StartedAtUtc;
        public string CompletedAtUtc;
        public string Note;
        public bool CancelRequested;
        public TestRunOptions Options;
        public TestRunSummary Summary = new();
        public List<TestCaseRecord> Results = new();
        public List<TestLogRecord> Logs = new();
        public bool IsTerminal =>
            string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Status, "cancelled", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    internal sealed class TestRunOptions
    {
        public TestRunOptions(
            TestMode mode,
            string assemblyName,
            string namespaceName,
            string className,
            string methodName,
            bool includePassingTests,
            bool includeLogs,
            bool includeStackTraces,
            int maxStoredLogs)
        {
            Mode = mode;
            AssemblyName = assemblyName;
            NamespaceName = namespaceName;
            ClassName = className;
            MethodName = methodName;
            IncludePassingTests = includePassingTests;
            IncludeLogs = includeLogs;
            IncludeStackTraces = includeStackTraces;
            MaxStoredLogs = maxStoredLogs;
        }

        public TestMode Mode { get; }
        public string AssemblyName { get; }
        public string NamespaceName { get; }
        public string ClassName { get; }
        public string MethodName { get; }
        public bool IncludePassingTests { get; }
        public bool IncludeLogs { get; }
        public bool IncludeStackTraces { get; }
        public int MaxStoredLogs { get; }
    }

    [Serializable]
    internal sealed class TestRunSummary
    {
        public int MatchingTests { get; set; }
        public int TotalTests { get; set; }
        public int PassedTests { get; set; }
        public int FailedTests { get; set; }
        public int SkippedTests { get; set; }
        public double DurationSeconds { get; set; }
    }

    [Serializable]
    internal sealed class TestCaseRecord
    {
        public string FullName { get; set; }
        public string Name { get; set; }
        public string ClassName { get; set; }
        public string NamespaceName { get; set; }
        public string AssemblyName { get; set; }
        public string Status { get; set; }
        public double DurationSeconds { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
    }

    [Serializable]
    internal sealed class TestLogRecord
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public string TimestampUtc { get; set; }
    }

    internal sealed class TestLeafInfo
    {
        public string FullName { get; set; }
        public string Name { get; set; }
        public string UniqueName { get; set; }
    }
}
