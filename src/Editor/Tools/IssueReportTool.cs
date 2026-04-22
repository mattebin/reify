using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Lets an LLM write a structured bug/unexpected-behavior report to
    /// `reports/llm-issues/pending/`. The user gates GitHub submission via
    /// `scripts/review-llm-issues.py`; this tool does NOT hit GitHub
    /// directly. See `reports/llm-issues/README.md` for the full flow.
    /// </summary>
    internal static class IssueReportTool
    {
        private const string PendingDir = "reports/llm-issues/pending";
        private static readonly string[] ValidEffort   = { "S", "M", "L" };
        private static readonly string[] ValidSeverity = { "info", "warn", "error", "critical" };

        [ReifyTool("reify-log-issue")]
        public static Task<object> Log(JToken args)
        {
            // Required fields
            var issueTitle = args?.Value<string>("issue_title")
                ?? throw new ArgumentException("issue_title is required.");
            var modelName  = args?.Value<string>("model_name")
                ?? throw new ArgumentException("model_name is required (e.g. 'claude-sonnet-4.5').");
            var effort     = (args?.Value<string>("effort") ?? "M").Trim().ToUpperInvariant();
            if (!ValidEffort.Contains(effort))
                throw new ArgumentException($"effort must be one of S/M/L, got '{effort}'.");
            var severity   = (args?.Value<string>("severity") ?? "warn").Trim().ToLowerInvariant();
            if (!ValidSeverity.Contains(severity))
                throw new ArgumentException(
                    $"severity must be one of info/warn/error/critical, got '{severity}'.");
            var context    = args?.Value<string>("context")
                ?? throw new ArgumentException("context is required — describe what the LLM was doing.");
            var symptom    = args?.Value<string>("symptom")
                ?? throw new ArgumentException("symptom is required — describe what went wrong.");

            // Optional fields
            var affectedTool      = args?.Value<string>("affected_tool");
            var reproductionSteps = args?["reproduction_steps"] as JArray;
            var logs              = args?.Value<string>("logs");
            var suggestedFix      = args?.Value<string>("suggested_fix");
            var reifyCommit       = args?.Value<string>("reify_commit");

            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                // Resolve reify repo root (NOT the hosting Unity project).
                //   - Dev workflow (file: reference): package.resolvedPath points at
                //     reify/src/Editor — repo root is two levels up. reports/ lives
                //     in the reify repo so scripts/review-llm-issues.py can see it.
                //   - Installed-from-git workflow: package resolvedPath is under
                //     Library/PackageCache; writing reports there is pointless.
                //     Fall back to hosting project root + a note in the response.
                var (pending, source) = ResolveReportsDir();
                Directory.CreateDirectory(pending);

                // Filename: timestamp + slugified title, ensures deterministic ordering + uniqueness.
                var now      = DateTime.UtcNow;
                var slug     = Slugify(issueTitle);
                var filename = $"{now:yyyyMMdd-HHmmss}-{slug}.md";
                var fullPath = Path.Combine(pending, filename);

                var sb = new StringBuilder();
                sb.AppendLine("---");
                sb.AppendLine($"issue_title:       \"{EscapeYaml(issueTitle)}\"");
                sb.AppendLine($"model_name:        \"{EscapeYaml(modelName)}\"");
                sb.AppendLine($"effort:            \"{effort}\"");
                sb.AppendLine($"severity:          \"{severity}\"");
                if (!string.IsNullOrEmpty(affectedTool))
                    sb.AppendLine($"affected_tool:     \"{EscapeYaml(affectedTool)}\"");
                sb.AppendLine($"unity_version:     \"{Application.unityVersion}\"");
                sb.AppendLine($"platform:          \"{Application.platform}\"");
                sb.AppendLine($"reify_tool_count:  {CountReifyTools()}");
                sb.AppendLine($"frame_at_detection: {Time.frameCount}");
                sb.AppendLine($"timestamp_utc:     \"{now:o}\"");
                if (!string.IsNullOrEmpty(reifyCommit))
                    sb.AppendLine($"reify_commit:      \"{EscapeYaml(reifyCommit)}\"");
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("## Context");
                sb.AppendLine();
                sb.AppendLine(context.Trim());
                sb.AppendLine();
                sb.AppendLine("## Symptom");
                sb.AppendLine();
                sb.AppendLine(symptom.Trim());
                sb.AppendLine();
                if (reproductionSteps != null && reproductionSteps.Count > 0)
                {
                    sb.AppendLine("## Reproduction");
                    sb.AppendLine();
                    var i = 1;
                    foreach (var step in reproductionSteps)
                    {
                        var text = step?.Value<string>() ?? step?.ToString();
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        sb.AppendLine($"{i}. {text.Trim()}");
                        i++;
                    }
                    sb.AppendLine();
                }
                if (!string.IsNullOrWhiteSpace(logs))
                {
                    sb.AppendLine("## Logs");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(logs.Trim());
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
                if (!string.IsNullOrWhiteSpace(suggestedFix))
                {
                    sb.AppendLine("## Suggested fix");
                    sb.AppendLine();
                    sb.AppendLine(suggestedFix.Trim());
                    sb.AppendLine();
                }

                // Write UTF-8 WITHOUT BOM so downstream tools (Python,
                // git diff, GitHub) don't see a stray \ufeff at the top.
                File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(false));

                return new
                {
                    status        = "logged_pending_review",
                    report_path   = fullPath.Replace('\\', '/'),
                    report_root_source = source,    // "reify_repo" | "unity_project_fallback"
                    filename      = filename,
                    submission_flow = "User runs `python scripts/review-llm-issues.py` to review + submit to GitHub.",
                    // ADR-002 style receipt — before/after is "no report existed" → "report at path"
                    applied_fields = new object[]
                    {
                        new { field = "report_exists", path = fullPath,
                              before = (object)false, after = (object)true }
                    },
                    applied_count = 1,
                    model_name    = modelName,
                    issue_title   = issueTitle,
                    effort        = effort,
                    severity      = severity,
                    read_at_utc   = now.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        [ReifyTool("reify-list-pending-issues")]
        public static Task<object> ListPending(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                var (pending, source) = ResolveReportsDir();
                var entries = new List<object>();
                if (Directory.Exists(pending))
                {
                    foreach (var f in Directory.GetFiles(pending, "*.md").OrderBy(p => p))
                    {
                        var fi = new FileInfo(f);
                        entries.Add(new
                        {
                            filename       = fi.Name,
                            path           = f.Replace('\\', '/'),
                            size_bytes     = fi.Length,
                            last_write_utc = fi.LastWriteTimeUtc.ToString("o")
                        });
                    }
                }
                return new
                {
                    pending_count = entries.Count,
                    pending       = entries.ToArray(),
                    pending_dir   = pending.Replace('\\', '/'),
                    report_root_source = source,
                    review_command = "python scripts/review-llm-issues.py",
                    read_at_utc   = DateTime.UtcNow.ToString("o"),
                    frame         = (long)Time.frameCount
                };
            });
        }

        /// <summary>
        /// Locate `<repo>/reports/llm-issues/pending/` on disk. Prefers the
        /// reify repo root (so `scripts/review-llm-issues.py` can see the
        /// files); falls back to the hosting Unity project root when reify
        /// was installed from git-cache and there is no accessible repo.
        /// </summary>
        private static (string pending, string source) ResolveReportsDir()
        {
            try
            {
                var pkg = PackageInfo.FindForAssembly(typeof(IssueReportTool).Assembly);
                if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath)
                    && pkg.source == PackageSource.Local
                    // resolvedPath for a file: dep points at the package folder,
                    // which for reify is "<repo>/src/Editor". Repo root is two up.
                    && Directory.Exists(Path.GetFullPath(Path.Combine(pkg.resolvedPath, "..", ".."))))
                {
                    var repoRoot = Path.GetFullPath(Path.Combine(pkg.resolvedPath, "..", ".."));
                    // Heuristic: the reify repo has CONTRIBUTING.md + src/ at its root.
                    if (File.Exists(Path.Combine(repoRoot, "CONTRIBUTING.md"))
                        && Directory.Exists(Path.Combine(repoRoot, "src")))
                    {
                        return (Path.Combine(repoRoot, PendingDir), "reify_repo");
                    }
                }
            }
            catch { /* fall through */ }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return (Path.Combine(projectRoot, PendingDir), "unity_project_fallback");
        }

        // ---------- helpers ----------
        private static string Slugify(string input)
        {
            if (string.IsNullOrEmpty(input)) return "report";
            var lower = input.ToLowerInvariant();
            var chars = new char[Math.Min(lower.Length, 60)];
            var j = 0;
            var lastDash = false;
            foreach (var c in lower)
            {
                if (j >= chars.Length) break;
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    chars[j++] = c; lastDash = false;
                }
                else if (!lastDash && j > 0)
                {
                    chars[j++] = '-'; lastDash = true;
                }
            }
            var s = new string(chars, 0, j).Trim('-');
            return string.IsNullOrEmpty(s) ? "report" : s;
        }

        private static string EscapeYaml(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static int CountReifyTools()
        {
            // Best-effort count — consistent with reify-self-check's tool_registry scan.
            var count = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    System.Reflection.MethodInfo[] methods;
                    try
                    {
                        methods = t.GetMethods(System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.Instance);
                    }
                    catch { continue; }
                    foreach (var m in methods)
                    {
                        if (m.GetCustomAttributes(typeof(ReifyToolAttribute), false).Length > 0) count++;
                    }
                }
            }
            return count;
        }
    }
}
