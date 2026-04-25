using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class IterationLoopServerTools
{
    [McpServerTool(Name = "asset-refresh-job"), Description("Queue AssetDatabase.Refresh as a background reify job.")]
    public static async Task<JsonElement> AssetRefreshJob(UnityClient unity, string? path = null, bool? force_update = null, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("asset-refresh-job", new { path, force_update }, ct);

    [McpServerTool(Name = "build-execute-job"), Description("Queue a Unity player build as a background job.")]
    public static async Task<JsonElement> BuildExecuteJob(
        UnityClient unity,
        string output_path,
        string[]? scenes = null,
        bool? development = null,
        bool? auto_run_player = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("build-execute-job", new
    {
        output_path, scenes, development, auto_run_player
    }, ct);

    [McpServerTool(Name = "compile-errors-structured"), Description("Read current Unity compile errors and warnings as structured data.")]
    public static async Task<JsonElement> CompileErrorsStructured(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("compile-errors-structured", null, ct);

    [McpServerTool(Name = "compile-errors-snapshot"), Description("Store the current compile error set as a baseline.")]
    public static async Task<JsonElement> CompileErrorsSnapshot(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("compile-errors-snapshot", null, ct);

    [McpServerTool(Name = "compile-errors-diff"), Description("Compare current compile errors against the last snapshot baseline.")]
    public static async Task<JsonElement> CompileErrorsDiff(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("compile-errors-diff", null, ct);

    [McpServerTool(Name = "console-log-summarize"), Description("Collapse repeated Unity console entries by message shape.")]
    public static async Task<JsonElement> ConsoleLogSummarize(
        UnityClient unity,
        string? type_filter = null,
        int? max_groups = null,
        bool? strip_digits = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("console-log-summarize", new { type_filter, max_groups, strip_digits }, ct);

    [McpServerTool(Name = "editor-await-compile"), Description("Bounded wait until Unity finishes compiling.")]
    public static async Task<JsonElement> EditorAwaitCompile(
        UnityClient unity,
        double? timeout_seconds = null,
        int? poll_ms = null,
        bool? request_first = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-await-compile", new { timeout_seconds, poll_ms, request_first }, ct);

    [McpServerTool(Name = "editor-await-event"), Description("Long-poll for compile_done, reload_done, or play_mode_changed.")]
    public static async Task<JsonElement> EditorAwaitEvent(
        UnityClient unity,
        string[]? events = null,
        double? timeout_seconds = null,
        int? poll_ms = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-await-event", new { events, timeout_seconds, poll_ms }, ct);

    [McpServerTool(Name = "editor-call-deferred"), Description("Queue a reify tool call to fire after one or more domain reloads.")]
    public static async Task<JsonElement> EditorCallDeferred(
        UnityClient unity,
        string tool,
        JsonElement? args = null,
        int? fire_after_reloads = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-call-deferred", new { tool, args, fire_after_reloads }, ct);

    [McpServerTool(Name = "editor-call-deferred-list"), Description("List queued deferred calls and stored deferred-call results.")]
    public static async Task<JsonElement> EditorCallDeferredList(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-call-deferred-list", null, ct);

    [McpServerTool(Name = "editor-call-deferred-results"), Description("Read deferred-call results.")]
    public static async Task<JsonElement> EditorCallDeferredResults(
        UnityClient unity,
        string? id = null,
        bool? consume = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-call-deferred-results", new { id, consume }, ct);

    [McpServerTool(Name = "editor-call-deferred-cancel"), Description("Cancel one queued deferred call by id.")]
    public static async Task<JsonElement> EditorCallDeferredCancel(UnityClient unity, string id, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-call-deferred-cancel", new { id }, ct);

    [McpServerTool(Name = "editor-log-tail"), Description("Tail Unity Editor.log from disk.")]
    public static async Task<JsonElement> EditorLogTail(
        UnityClient unity,
        int? max_lines = null,
        int? max_bytes = null,
        string? contains_substring = null,
        string? path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-log-tail", new { max_lines, max_bytes, contains_substring, path }, ct);

    [McpServerTool(Name = "editor-request-script-compilation"), Description("Request Unity script compilation and domain reload.")]
    public static async Task<JsonElement> EditorRequestScriptCompilation(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-request-script-compilation", null, ct);

    [McpServerTool(Name = "editor-prefs-get"), Description("Read one Unity EditorPrefs key.")]
    public static async Task<JsonElement> EditorPrefsGet(UnityClient unity, string key, string? type = null, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-prefs-get", new { key, type }, ct);

    [McpServerTool(Name = "editor-prefs-set"), Description("Write one Unity EditorPrefs key.")]
    public static async Task<JsonElement> EditorPrefsSet(UnityClient unity, string key, JsonElement value, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-prefs-set", new { key, value }, ct);

    [McpServerTool(Name = "editor-prefs-delete"), Description("Delete one Unity EditorPrefs key.")]
    public static async Task<JsonElement> EditorPrefsDelete(UnityClient unity, string key, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-prefs-delete", new { key }, ct);

    [McpServerTool(Name = "editor-prefs-list"), Description("List known Unity EditorPrefs keys where supported.")]
    public static async Task<JsonElement> EditorPrefsList(UnityClient unity, string? prefix = null, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("editor-prefs-list", new { prefix }, ct);

    [McpServerTool(Name = "player-prefs-get"), Description("Read one Unity PlayerPrefs key.")]
    public static async Task<JsonElement> PlayerPrefsGet(UnityClient unity, string key, string? type = null, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("player-prefs-get", new { key, type }, ct);

    [McpServerTool(Name = "player-prefs-set"), Description("Write one Unity PlayerPrefs key.")]
    public static async Task<JsonElement> PlayerPrefsSet(UnityClient unity, string key, JsonElement value, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("player-prefs-set", new { key, value }, ct);

    [McpServerTool(Name = "player-prefs-delete"), Description("Delete one Unity PlayerPrefs key.")]
    public static async Task<JsonElement> PlayerPrefsDelete(UnityClient unity, string key, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("player-prefs-delete", new { key }, ct);

    [McpServerTool(Name = "job-status"), Description("Read one reify background job by job_id.")]
    public static async Task<JsonElement> JobStatus(UnityClient unity, string job_id, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("job-status", new { job_id }, ct);

    [McpServerTool(Name = "job-result"), Description("Read terminal result for one reify job.")]
    public static async Task<JsonElement> JobResult(UnityClient unity, string job_id, bool? include_events = null, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("job-result", new { job_id, include_events }, ct);

    [McpServerTool(Name = "job-list"), Description("List reify jobs.")]
    public static async Task<JsonElement> JobList(
        UnityClient unity,
        string? kind = null,
        string? state = null,
        int? limit = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("job-list", new { kind, state, limit }, ct);

    [McpServerTool(Name = "job-cancel"), Description("Request cooperative cancellation for one reify job.")]
    public static async Task<JsonElement> JobCancel(UnityClient unity, string job_id, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("job-cancel", new { job_id }, ct);

    [McpServerTool(Name = "primitive-defaults"), Description("Return Unity primitive intrinsic dimensions.")]
    public static async Task<JsonElement> PrimitiveDefaults(UnityClient unity, string? primitive = null, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("primitive-defaults", new { primitive }, ct);

    [McpServerTool(Name = "project-settings-asset-read"), Description("Read a serialized ProjectSettings/*.asset property or top-level property list.")]
    public static async Task<JsonElement> ProjectSettingsAssetRead(
        UnityClient unity,
        string asset,
        string? property_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("project-settings-asset-read", new { asset, property_path }, ct);

    [McpServerTool(Name = "project-settings-asset-write"), Description("Write one serialized ProjectSettings/*.asset property.")]
    public static async Task<JsonElement> ProjectSettingsAssetWrite(
        UnityClient unity,
        string asset,
        string property_path,
        JsonElement value,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("project-settings-asset-write", new { asset, property_path, value }, ct);

    [McpServerTool(Name = "recipe-compile-and-report"), Description("Composite recipe: compile request, wait, then report structured compile state.")]
    public static async Task<JsonElement> RecipeCompileAndReport(UnityClient unity, double? timeout_seconds = null, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("recipe-compile-and-report", new { timeout_seconds }, ct);

    [McpServerTool(Name = "recipe-enter-play-and-snapshot"), Description("Composite recipe: enter play mode, settle frames, then return scene evidence.")]
    public static async Task<JsonElement> RecipeEnterPlayAndSnapshot(
        UnityClient unity,
        string? name_contains = null,
        int? settle_frames = null,
        int? snapshot_limit = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("recipe-enter-play-and-snapshot", new { name_contains, settle_frames, snapshot_limit }, ct);

    [McpServerTool(Name = "recipe-compile-then-test"), Description("Composite recipe: compile, then start a Unity test run.")]
    public static async Task<JsonElement> RecipeCompileThenTest(
        UnityClient unity,
        double? compile_timeout_seconds = null,
        string? assembly_name = null,
        string? mode = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("recipe-compile-then-test", new { compile_timeout_seconds, assembly_name, mode }, ct);

    [McpServerTool(Name = "reify-health"), Description("Combined Reify/Unity bridge health endpoint.")]
    public static async Task<JsonElement> ReifyHealth(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("reify-health", null, ct);

    [McpServerTool(Name = "reify-self-check"), Description("Run Reify's built-in self-check suite.")]
    public static async Task<JsonElement> ReifySelfCheck(UnityClient unity, bool? skip_writes = null, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("reify-self-check", new { skip_writes }, ct);

    [McpServerTool(Name = "tests-coverage-map"), Description("Map assemblies to test assemblies and coverage gaps.")]
    public static async Task<JsonElement> TestsCoverageMap(UnityClient unity, bool? include_builtin = null, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("tests-coverage-map", new { include_builtin }, ct);
}
