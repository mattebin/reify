using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using Reify.Editor.Tools;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Reify.Editor
{
    internal sealed class ReifyCommandCenterWindow : EditorWindow
    {
        private const string ReportsRootRelative = "reports/llm-issues";
        private Vector2 _scroll;
        private Vector2 _toolScroll;
        private string _toolSearch = "";
        private string _status = "";
        private int _selectedReportIndex;
        private ReportState _reportState = ReportState.Pending;
        private List<ReportFile> _reports = new List<ReportFile>();
        private double _nextAutoRefresh;

        [MenuItem("Window/Reify/Command Center")]
        [MenuItem("Tools/Reify/Command Center")]
        public static void Open()
        {
            var window = GetWindow<ReifyCommandCenterWindow>("Reify");
            window.minSize = new Vector2(720, 520);
            window.RefreshReports();
            window.Show();
        }

        private void OnEnable()
        {
            RefreshReports();
            _nextAutoRefresh = EditorApplication.timeSinceStartup + 2;
        }

        private void OnGUI()
        {
            if (EditorApplication.timeSinceStartup >= _nextAutoRefresh)
            {
                _nextAutoRefresh = EditorApplication.timeSinceStartup + 2;
                Repaint();
            }

            DrawHeader();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawHealth();
            Space();
            DrawQuickActions();
            Space();
            DrawTools();
            Space();
            DrawReports();
            Space();
            DrawDisciplineGuide();
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            var toolCount = GetToolNames().Length;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Reify Command Center", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Evidence-first Unity MCP. The LLM can act, but the receipts stay visible.");
            EditorGUILayout.BeginHorizontal();
            Badge(IsReady() ? "Bridge ready" : "Bridge busy", IsReady() ? Color.green : Color.yellow);
            Badge($"{toolCount} tools", new Color(0.4f, 0.7f, 1f));
            Badge(Application.unityVersion, new Color(0.8f, 0.8f, 0.8f));
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.Info);
        }

        private void DrawHealth()
        {
            Section("Health");
            var port = ResolveBridgePort();
            var maxBytes = ResolveMaxResponseBytes();
            var state = EditorApplication.isCompiling || EditorApplication.isUpdating
                ? "compiling/importing"
                : EditorApplication.isPlaying
                    ? "play"
                    : "edit_idle";
            var compileSnapshot = CompileErrorsTool.SnapshotCurrent();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Row("State", state);
            Row("Bridge URL", $"http://127.0.0.1:{port}/");
            Row("Response cap", $"{maxBytes:n0} bytes");
            Row("Unity", Application.unityVersion);
            Row("Project", ProjectRoot());
            Row("Console", $"{compileSnapshot.errors.Count} errors, {compileSnapshot.warnings.Count} warnings");
            Row("Dirty", AnyDirtyScene() ? "dirty open scenes exist" : "no dirty open scenes");
            EditorGUILayout.EndVertical();
        }

        private void DrawQuickActions()
        {
            Section("Quick Actions");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Bridge URL")) Copy($"http://127.0.0.1:{ResolveBridgePort()}/");
            if (GUILayout.Button("Copy Generic MCP Config")) Copy(GenericMcpConfig());
            if (GUILayout.Button("Open Docs")) OpenPathOrUrl("https://github.com/mattebin/reify");
            if (GUILayout.Button("Open Package README")) OpenPathOrUrl(Path.Combine(RepoRoot(), "src", "Editor", "README.md"));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run Self Check (read-only)")) RunSelfCheck();
            if (GUILayout.Button("Refresh Reports")) RefreshReports();
            if (GUILayout.Button("Open Reports Folder")) OpenPathOrUrl(ReportsDir(_reportState));
            if (GUILayout.Button("Copy Review Command")) Copy($"cd \"{RepoRoot()}\" && python scripts/review-llm-issues.py");
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTools()
        {
            Section("Tools");
            _toolSearch = EditorGUILayout.TextField("Search", _toolSearch);
            var names = GetToolNames();
            var filtered = string.IsNullOrWhiteSpace(_toolSearch)
                ? names
                : names.Where(n => n.IndexOf(_toolSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();

            var groups = filtered
                .GroupBy(DomainOf)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToArray();

            _toolScroll = EditorGUILayout.BeginScrollView(_toolScroll, GUILayout.Height(170));
            foreach (var group in groups)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{group.Key} ({group.Count()})", EditorStyles.boldLabel);
                if (GUILayout.Button("Copy names", GUILayout.Width(95)))
                    Copy(string.Join("\n", group.OrderBy(n => n, StringComparer.Ordinal).ToArray()));
                EditorGUILayout.EndHorizontal();

                var line = string.Join(", ", group.OrderBy(n => n, StringComparer.Ordinal).Take(18).ToArray());
                if (group.Count() > 18) line += ", ...";
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawReports()
        {
            Section("LLM Reports");
            EditorGUILayout.HelpBox(
                "Reports stay local until a human clicks Send to GitHub. The AI recommendation is advisory; this window is the consent gate.",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (ToggleReportState("Pending", ReportState.Pending)) RefreshReports();
            if (ToggleReportState("Submitted", ReportState.Submitted)) RefreshReports();
            if (ToggleReportState("Dismissed", ReportState.Dismissed)) RefreshReports();
            EditorGUILayout.EndHorizontal();

            if (_reports.Count == 0)
            {
                EditorGUILayout.LabelField($"No {_reportState.ToString().ToLowerInvariant()} reports.");
                return;
            }

            _selectedReportIndex = Mathf.Clamp(_selectedReportIndex, 0, _reports.Count - 1);
            var labels = _reports.Select(r => r.Title).ToArray();
            _selectedReportIndex = EditorGUILayout.Popup("Report", _selectedReportIndex, labels);
            var report = _reports[_selectedReportIndex];

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Row("Title", report.Title);
            Row("Severity", report.Get("severity", "unknown"));
            Row("Effort", report.Get("effort", "unknown"));
            Row("Tool", report.Get("affected_tool", "(not specified)"));
            Row("Reporter", report.Get("model_name", "(unknown)"));
            Row("AI recommendation", report.Get("ai_recommendation", "unsure"));
            var reason = report.Get("ai_reason", "");
            if (!string.IsNullOrWhiteSpace(reason))
                EditorGUILayout.HelpBox(reason, MessageType.None);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Markdown")) OpenPathOrUrl(report.Path);
            if (GUILayout.Button("Copy Body")) Copy(File.ReadAllText(report.Path, Encoding.UTF8));
            GUI.enabled = _reportState == ReportState.Pending;
            if (GUILayout.Button("Dismiss")) DismissReport(report);
            if (GUILayout.Button("Delete")) DeleteReport(report);
            if (GUILayout.Button("Send to GitHub")) SendReportToGitHub(report);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDisciplineGuide()
        {
            Section("Agent Discipline");
            EditorGUILayout.HelpBox(
                "1. Call reify-orient before first write.\n" +
                "2. Snapshot before risky changes.\n" +
                "3. Every write must prove itself with before/after evidence.\n" +
                "4. Spatial claims need anchor-distance or bounds proof.\n" +
                "5. If reify itself breaks, log a local report and let the user decide whether to send it.",
                MessageType.None);
        }

        private bool ToggleReportState(string label, ReportState state)
        {
            var was = _reportState == state;
            var now = GUILayout.Toggle(was, label, "Button");
            if (now && !was)
            {
                _reportState = state;
                _selectedReportIndex = 0;
                return true;
            }
            return false;
        }

        private async void RunSelfCheck()
        {
            _status = "Running read-only self-check...";
            Repaint();
            try
            {
                var result = await SelfCheckTools.Run(new JObject { ["skip_writes"] = true });
                var token = JToken.FromObject(result);
                _status = $"Self-check completed: {token["pass_count"]} pass, {token["fail_count"]} fail.";
            }
            catch (Exception ex)
            {
                _status = $"Self-check failed: {ex.Message}";
            }
            Repaint();
        }

        private void RefreshReports()
        {
            var dir = ReportsDir(_reportState);
            _reports = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.md")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Select(ReportFile.Load)
                    .ToList()
                : new List<ReportFile>();
            _selectedReportIndex = Mathf.Clamp(_selectedReportIndex, 0, Math.Max(0, _reports.Count - 1));
        }

        private void DismissReport(ReportFile report)
        {
            if (!EditorUtility.DisplayDialog("Dismiss report?", report.Title, "Dismiss", "Cancel")) return;
            Directory.CreateDirectory(ReportsDir(ReportState.Dismissed));
            File.Move(report.Path, Path.Combine(ReportsDir(ReportState.Dismissed), Path.GetFileName(report.Path)));
            _status = "Report moved to dismissed.";
            RefreshReports();
        }

        private void DeleteReport(ReportFile report)
        {
            if (!EditorUtility.DisplayDialog("Delete report?", $"Delete {Path.GetFileName(report.Path)}? This cannot be undone.", "Delete", "Cancel")) return;
            File.Delete(report.Path);
            _status = "Report deleted.";
            RefreshReports();
        }

        private void SendReportToGitHub(ReportFile report)
        {
            var repo = DetectRepo();
            if (string.IsNullOrWhiteSpace(repo))
            {
                EditorUtility.DisplayDialog("No GitHub repo", "Could not detect a GitHub origin remote for this reify checkout.", "OK");
                return;
            }

            var user = RunProcess("gh", new[] { "api", "user", "--jq", ".login" }, RepoRoot(), out _, out var userOut, out var userErr)
                ? userOut.Trim()
                : "";
            if (string.IsNullOrWhiteSpace(user))
            {
                EditorUtility.DisplayDialog("GitHub auth required", $"The GitHub CLI is not authenticated or not installed.\n\n{userErr}", "OK");
                return;
            }

            var recommendation = report.Get("ai_recommendation", "unsure");
            var confirm = EditorUtility.DisplayDialog(
                "Send LLM report to GitHub?",
                $"Target: {repo}\nFiling as: {user}\nAI recommendation: {recommendation}\n\nThis creates a GitHub issue from the local markdown report.",
                "Send",
                "Cancel");
            if (!confirm) return;

            var url = FileIssue(report, repo);
            if (string.IsNullOrWhiteSpace(url)) return;

            Directory.CreateDirectory(ReportsDir(ReportState.Submitted));
            var submittedPath = Path.Combine(ReportsDir(ReportState.Submitted), Path.GetFileName(report.Path));
            File.Move(report.Path, submittedPath);
            File.AppendAllText(submittedPath, $"\n\n<!-- filed to GitHub: {url} -->\n", Encoding.UTF8);
            _status = $"Filed GitHub issue: {url}";
            RefreshReports();
        }

        private string FileIssue(ReportFile report, string repo)
        {
            var bodyPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(bodyPath, report.ToGitHubBody(), new UTF8Encoding(false));
                var labels = BuildLabels(report).ToArray();
                EnsureLabels(repo, labels);

                var args = new List<string>
                {
                    "issue", "create",
                    "--repo", repo,
                    "--title", report.Title,
                    "--body-file", bodyPath
                };
                foreach (var label in labels)
                {
                    args.Add("--label");
                    args.Add(label);
                }

                if (!RunProcess("gh", args, RepoRoot(), out var exit, out var stdout, out var stderr))
                {
                    EditorUtility.DisplayDialog("GitHub issue failed", $"gh exited {exit}\n\n{stderr}", "OK");
                    return null;
                }
                return stdout.Trim();
            }
            finally
            {
                try { File.Delete(bodyPath); } catch { /* ignore */ }
            }
        }

        private static IEnumerable<string> BuildLabels(ReportFile report)
        {
            yield return "llm-reported";
            var severity = report.Get("severity", "");
            if (!string.IsNullOrWhiteSpace(severity)) yield return $"severity:{severity}";
            var effort = report.Get("effort", "");
            if (!string.IsNullOrWhiteSpace(effort)) yield return $"effort:{effort}";
            var model = report.Get("model_name", "");
            if (!string.IsNullOrWhiteSpace(model)) yield return $"reporter:{model.Replace(' ', '-')}";
        }

        private static void EnsureLabels(string repo, IEnumerable<string> labels)
        {
            foreach (var label in labels)
            {
                var color = label.StartsWith("severity:critical") ? "B60205"
                    : label.StartsWith("severity:error") ? "D93F0B"
                    : label.StartsWith("severity:warn") ? "FBCA04"
                    : label.StartsWith("severity:info") ? "C5DEF5"
                    : label.StartsWith("effort:") ? "C2E0C6"
                    : label.StartsWith("reporter:") ? "B4A7E5"
                    : "0E8A16";
                RunProcess("gh", new[] { "label", "create", label, "--repo", repo, "--color", color, "--force" }, RepoRoot(), out _, out _, out _);
            }
        }

        private static bool RunProcess(string fileName, IEnumerable<string> args, string workingDir, out int exitCode, out string stdout, out string stderr)
        {
            var psi = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables.Remove("GITHUB_TOKEN");
            psi.EnvironmentVariables.Remove("GH_TOKEN");
            psi.Arguments = string.Join(" ", args.Select(QuoteArg).ToArray());

            try
            {
                using var process = Process.Start(psi);
                stdout = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(30000);
                exitCode = process.ExitCode;
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                exitCode = -1;
                stdout = "";
                stderr = ex.Message;
                return false;
            }
        }

        private static void Section(string title)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void Row(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(140));
            EditorGUILayout.SelectableLabel(value ?? "", GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        private static void Badge(string text, Color color)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label(text, EditorStyles.helpBox, GUILayout.Width(Mathf.Max(95, text.Length * 8)));
            GUI.backgroundColor = old;
        }

        private static void Space() => EditorGUILayout.Space(3);

        private static bool IsReady() =>
            !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorUtility.scriptCompilationFailed;

        private static string[] GetToolNames() =>
            ReifyBridge.GetRegisteredToolNames().OrderBy(n => n, StringComparer.Ordinal).ToArray();

        private static string DomainOf(string name)
        {
            var dash = name.IndexOf('-');
            return dash > 0 ? name.Substring(0, dash) : name;
        }

        private static int ResolveBridgePort()
        {
            var fromEnv = Environment.GetEnvironmentVariable("REIFY_BRIDGE_PORT");
            return int.TryParse(fromEnv, out var parsed) && parsed > 0 ? parsed : 17777;
        }

        private static int ResolveMaxResponseBytes()
        {
            var fromEnv = Environment.GetEnvironmentVariable("REIFY_MAX_RESPONSE_BYTES");
            return int.TryParse(fromEnv, out var parsed) && parsed > 16384 ? parsed : 786432;
        }

        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string RepoRoot()
        {
            try
            {
                var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ReifyCommandCenterWindow).Assembly);
                if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath))
                {
                    var candidate = Path.GetFullPath(Path.Combine(pkg.resolvedPath, "..", ".."));
                    if (File.Exists(Path.Combine(candidate, "CONTRIBUTING.md")) && Directory.Exists(Path.Combine(candidate, "src")))
                        return candidate;
                }
            }
            catch { /* fall through */ }
            return ProjectRoot();
        }

        private static string ReportsDir(ReportState state)
        {
            var leaf = state.ToString().ToLowerInvariant();
            return Path.Combine(RepoRoot(), ReportsRootRelative, leaf);
        }

        private static string DetectRepo()
        {
            if (!RunProcess("git", new[] { "remote", "get-url", "origin" }, RepoRoot(), out _, out var stdout, out _))
                return null;
            var url = stdout.Trim();
            string tail;
            if (url.StartsWith("git@", StringComparison.Ordinal))
                tail = url.Split(':').LastOrDefault() ?? "";
            else
                tail = url.Contains("github.com/") ? url.Substring(url.IndexOf("github.com/", StringComparison.Ordinal) + "github.com/".Length) : url;
            tail = tail.Trim().Trim('/').Replace("\\", "/");
            if (tail.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                tail = tail.Substring(0, tail.Length - 4);
            return tail.Contains("/") ? tail : null;
        }

        private static string GenericMcpConfig()
        {
            var serverDll = Path.Combine(RepoRoot(), "src", "Server", "bin", "Release", "net8.0", "reify-server.dll").Replace("\\", "/");
            return "{\n  \"mcpServers\": {\n    \"reify\": {\n      \"command\": \"dotnet\",\n      \"args\": [\"" + serverDll + "\"]\n    }\n  }\n}";
        }

        private static void Copy(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text ?? "";
            Debug.Log("[Reify] Copied to clipboard.");
        }

        private static string QuoteArg(string arg)
        {
            if (arg == null) return "\"\"";
            if (arg.Length == 0) return "\"\"";
            if (arg.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0) return arg;
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }

        private static void OpenPathOrUrl(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl)) return;
            if (pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                Application.OpenURL(pathOrUrl);
            else if (File.Exists(pathOrUrl))
                EditorUtility.OpenWithDefaultApp(pathOrUrl);
            else if (Directory.Exists(pathOrUrl))
                EditorUtility.RevealInFinder(pathOrUrl);
            else
                EditorUtility.DisplayDialog("Not found", pathOrUrl, "OK");
        }

        private static bool AnyDirtyScene()
        {
            for (var i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++)
                if (UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i).isDirty)
                    return true;
            return false;
        }

        private enum ReportState
        {
            Pending,
            Submitted,
            Dismissed
        }

        private sealed class ReportFile
        {
            public string Path { get; private set; }
            public string Body { get; private set; }
            public Dictionary<string, string> Frontmatter { get; private set; }
            public string Title => Get("issue_title", System.IO.Path.GetFileNameWithoutExtension(Path));

            public string Get(string key, string fallback) =>
                Frontmatter.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

            public static ReportFile Load(string path)
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var body = text;
                if (text.StartsWith("---", StringComparison.Ordinal))
                {
                    var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        var header = text.Substring(3, end - 3);
                        body = text.Substring(end + 4).TrimStart('\r', '\n');
                        foreach (var rawLine in header.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                        {
                            var line = rawLine.Trim();
                            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                            var colon = line.IndexOf(':');
                            if (colon <= 0) continue;
                            var key = line.Substring(0, colon).Trim();
                            var value = line.Substring(colon + 1).Trim().Trim('"').Trim('\'');
                            frontmatter[key] = value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                        }
                    }
                }
                return new ReportFile { Path = path, Body = body, Frontmatter = frontmatter };
            }

            public string ToGitHubBody()
            {
                var keys = new[]
                {
                    "model_name", "effort", "severity", "affected_tool", "ai_recommendation",
                    "ai_reason", "unity_version", "platform", "reify_tool_count",
                    "frame_at_detection", "timestamp_utc", "reify_commit"
                };
                var sb = new StringBuilder();
                sb.AppendLine("## Metadata");
                sb.AppendLine();
                foreach (var key in keys)
                    if (Frontmatter.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                        sb.AppendLine($"- **{key}**: {value}");
                sb.AppendLine();
                sb.Append(Body);
                return sb.ToString();
            }
        }
    }
}
