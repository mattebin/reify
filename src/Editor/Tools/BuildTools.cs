using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Build pipeline surface: active target read, switch target, execute
    /// a build and summarise the BuildReport.
    /// </summary>
    internal static class BuildTools
    {
        // ---------- build-target-get ----------
        [ReifyTool("build-target-get")]
        public static Task<object> TargetGet(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var active = EditorUserBuildSettings.activeBuildTarget;
                var group  = BuildPipeline.GetBuildTargetGroup(active);

                var supported = new List<string>();
                foreach (BuildTarget t in Enum.GetValues(typeof(BuildTarget)))
                {
                    if ((int)t < 0) continue;
                    if (BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(t), t))
                        supported.Add(t.ToString());
                }

                return new
                {
                    active_target          = active.ToString(),
                    active_target_group    = group.ToString(),
                    supported_targets      = supported.ToArray(),
                    development_build      = EditorUserBuildSettings.development,
                    allow_debugging        = EditorUserBuildSettings.allowDebugging,
                    connect_profiler       = EditorUserBuildSettings.connectProfiler,
                    read_at_utc            = DateTime.UtcNow.ToString("o"),
                    frame                  = (long)Time.frameCount
                };
            });
        }

        // ---------- build-target-switch ----------
        [ReifyTool("build-target-switch")]
        public static Task<object> TargetSwitch(JToken args)
        {
            var targetStr = args?.Value<string>("target")
                ?? throw new ArgumentException("target is required (e.g. StandaloneWindows64, Android, WebGL, iOS).");
            if (!Enum.TryParse<BuildTarget>(targetStr, true, out var target))
                throw new ArgumentException($"target '{targetStr}' is not a valid BuildTarget.");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var group = BuildPipeline.GetBuildTargetGroup(target);
                if (!BuildPipeline.IsBuildTargetSupported(group, target))
                    throw new InvalidOperationException(
                        $"BuildTarget '{target}' is not supported on this Unity install. " +
                        "Install the target module via Unity Hub.");

                var before = EditorUserBuildSettings.activeBuildTarget;
                if (before == target)
                {
                    return new
                    {
                        already_active  = true,
                        active_target   = target.ToString(),
                        read_at_utc     = DateTime.UtcNow.ToString("o"),
                        frame           = (long)Time.frameCount
                    };
                }

                var ok = EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
                return new
                {
                    requested       = target.ToString(),
                    before_target   = before.ToString(),
                    after_target    = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    switch_returned = ok,
                    note            = "Switching triggers a full asset reimport for the new target. Expect a long stall — poll domain-reload-status to know when the editor is ready again.",
                    read_at_utc     = DateTime.UtcNow.ToString("o"),
                    frame           = (long)Time.frameCount
                };
            });
        }

        // ---------- build-execute-job ----------
        // Non-blocking variant: returns a job_id immediately, runs the
        // build on the next editor frame, and lets the caller poll
        // job-status / job-result. Use this for anything non-trivial —
        // a Standalone build is minutes of work and will time out MCP
        // clients on sync.
        [ReifyTool("build-execute-job")]
        public static Task<object> ExecuteJob(JToken args)
        {
            var outputPath = args?.Value<string>("output_path")
                ?? throw new ArgumentException("output_path is required.");
            var scenesArr  = args?["scenes"] as JArray;
            var development = args?.Value<bool?>("development") ?? false;
            var autoRun     = args?.Value<bool?>("auto_run_player") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var job = ReifyJobs.Start("build");
                ReifyJobs.SetRunning(job, "build queued", 0f);

                // Capture enabled scenes + args on the request thread so
                // the delay-called lambda doesn't race against subsequent
                // EditorBuildSettings mutations.
                string[] scenes;
                if (scenesArr != null && scenesArr.Count > 0)
                {
                    var list = new List<string>();
                    foreach (var s in scenesArr) list.Add(s.Value<string>());
                    scenes = list.ToArray();
                }
                else
                {
                    var all = EditorBuildSettings.scenes;
                    var enabled = new List<string>();
                    foreach (var s in all) if (s.enabled) enabled.Add(s.path);
                    if (enabled.Count == 0)
                    {
                        ReifyJobs.Fail(job,
                            "No scenes provided and no enabled scenes in EditorBuildSettings.");
                        return new { job = ReifyJobs.Serialize(job) };
                    }
                    scenes = enabled.ToArray();
                }

                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        ReifyJobs.SetRunning(job, "BuildPipeline.BuildPlayer running", -1f);
                        var target = EditorUserBuildSettings.activeBuildTarget;
                        var options = BuildOptions.None;
                        if (development) options |= BuildOptions.Development;
                        if (autoRun)     options |= BuildOptions.AutoRunPlayer;

                        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                        {
                            scenes = scenes, locationPathName = outputPath,
                            target = target, options = options
                        });
                        var s = report.summary;
                        var errorCount = 0; var warningCount = 0;
                        foreach (var step in report.steps)
                            foreach (var msg in step.messages)
                            {
                                if (msg.type == LogType.Error || msg.type == LogType.Exception) errorCount++;
                                else if (msg.type == LogType.Warning) warningCount++;
                            }

                        ReifyJobs.Succeed(job, new
                        {
                            target              = target.ToString(),
                            output_path         = outputPath,
                            scenes              = scenes,
                            development,
                            auto_run_player     = autoRun,
                            result              = s.result.ToString(),
                            platform            = s.platform.ToString(),
                            total_size_bytes    = (long)s.totalSize,
                            total_time_seconds  = s.totalTime.TotalSeconds,
                            build_started_utc   = s.buildStartedAt.ToUniversalTime().ToString("o"),
                            build_ended_utc     = s.buildEndedAt.ToUniversalTime().ToString("o"),
                            total_errors        = s.totalErrors,
                            total_warnings      = s.totalWarnings,
                            step_error_count    = errorCount,
                            step_warning_count  = warningCount,
                            step_count          = report.steps?.Length ?? 0
                        });
                    }
                    catch (Exception ex)
                    {
                        ReifyJobs.Fail(job, $"{ex.GetType().Name}: {ex.Message}");
                    }
                };

                return new
                {
                    job          = ReifyJobs.Serialize(job, includeResult: false, includeEvents: false),
                    note         = "Build queued. Poll job-status / job-result with the job_id.",
                    read_at_utc  = DateTime.UtcNow.ToString("o"),
                    frame        = (long)Time.frameCount
                };
            });
        }

        // ---------- build-execute ----------
        [ReifyTool("build-execute")]
        public static Task<object> Execute(JToken args)
        {
            var outputPath = args?.Value<string>("output_path")
                ?? throw new ArgumentException("output_path is required.");
            var scenesArr  = args?["scenes"] as JArray;
            var development = args?.Value<bool?>("development") ?? false;
            var autoRun     = args?.Value<bool?>("auto_run_player") ?? false;

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var target = EditorUserBuildSettings.activeBuildTarget;

                // Resolve scene list — explicit arg overrides EditorBuildSettings.
                string[] scenes;
                if (scenesArr != null && scenesArr.Count > 0)
                {
                    var list = new List<string>();
                    foreach (var s in scenesArr) list.Add(s.Value<string>());
                    scenes = list.ToArray();
                }
                else
                {
                    var all = EditorBuildSettings.scenes;
                    var enabled = new List<string>();
                    foreach (var s in all) if (s.enabled) enabled.Add(s.path);
                    if (enabled.Count == 0)
                        throw new InvalidOperationException(
                            "No scenes provided and no enabled scenes in EditorBuildSettings. " +
                            "Add scenes to Build Settings or pass them via the 'scenes' arg.");
                    scenes = enabled.ToArray();
                }

                var options = BuildOptions.None;
                if (development) options |= BuildOptions.Development;
                if (autoRun)     options |= BuildOptions.AutoRunPlayer;

                var bpo = new BuildPlayerOptions
                {
                    scenes           = scenes,
                    locationPathName = outputPath,
                    target           = target,
                    options          = options
                };

                var report = BuildPipeline.BuildPlayer(bpo);
                var summary = report.summary;

                // Collect step + error summary without dumping everything.
                var errorCount = 0;
                var warningCount = 0;
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception) errorCount++;
                        else if (msg.type == LogType.Warning) warningCount++;
                    }
                }

                return new
                {
                    target              = target.ToString(),
                    output_path         = outputPath,
                    scenes              = scenes,
                    development,
                    auto_run_player     = autoRun,
                    result              = summary.result.ToString(),
                    platform            = summary.platform.ToString(),
                    total_size_bytes    = (long)summary.totalSize,
                    total_time_seconds  = summary.totalTime.TotalSeconds,
                    build_started_utc   = summary.buildStartedAt.ToUniversalTime().ToString("o"),
                    build_ended_utc     = summary.buildEndedAt.ToUniversalTime().ToString("o"),
                    total_errors        = summary.totalErrors,
                    total_warnings      = summary.totalWarnings,
                    step_error_count    = errorCount,
                    step_warning_count  = warningCount,
                    step_count          = report.steps?.Length ?? 0,
                    read_at_utc         = DateTime.UtcNow.ToString("o"),
                    frame               = (long)Time.frameCount
                };
            });
        }
    }
}
