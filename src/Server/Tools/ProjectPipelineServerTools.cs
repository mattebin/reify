using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class ProjectPipelineServerTools
{
    [McpServerTool(Name = "asmdef-list"), Description(
        "List Assembly Definition (.asmdef) assets in the project. Args: " +
        "optional name_filter, include_packages (default false), limit " +
        "(default 200), offset (default 0). Returns evidence like asset path, " +
        "assembly name, SHA-256, references, platform filters, and warnings.")]
    public static async Task<JsonElement> AsmdefList(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asmdef-list", args, ct);

    [McpServerTool(Name = "asmdef-inspect"), Description(
        "Inspect one .asmdef asset. Args: asset_path, optional " +
        "include_raw_text (default false). Returns parsed definition JSON, " +
        "GUID, SHA-256, references, versionDefines, and warnings.")]
    public static async Task<JsonElement> AsmdefInspect(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asmdef-inspect", args, ct);

    [McpServerTool(Name = "asmdef-update-or-create"), Description(
        "Create or patch an Assembly Definition asset. Args: asset_path plus " +
        "either a nested definition object or top-level asmdef fields " +
        "(name, references, includePlatforms, excludePlatforms, " +
        "allowUnsafeCode, overrideReferences, precompiledReferences, " +
        "autoReferenced, defineConstraints, versionDefines, " +
        "noEngineReferences, rootNamespace). Returns before/after readback.")]
    public static async Task<JsonElement> AsmdefUpdateOrCreate(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asmdef-update-or-create", args, ct);

    [McpServerTool(Name = "asmdef-delete"), Description(
        "Delete a .asmdef asset. Args: asset_path, optional use_trash " +
        "(default true). Returns the pre-delete evidence summary.")]
    public static async Task<JsonElement> AsmdefDelete(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("asmdef-delete", args, ct);

    [McpServerTool(Name = "project-tag-add"), Description(
        "Add a new Unity tag to ProjectSettings/TagManager.asset. Args: name. " +
        "Returns the updated tag snapshot. Rejects duplicates cleanly.")]
    public static async Task<JsonElement> ProjectTagAdd(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("project-tag-add", args, ct);

    [McpServerTool(Name = "project-tag-remove"), Description(
        "Remove a Unity tag from ProjectSettings/TagManager.asset. Args: name. " +
        "Built-in tags are protected. Returns the updated tag snapshot.")]
    public static async Task<JsonElement> ProjectTagRemove(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("project-tag-remove", args, ct);

    [McpServerTool(Name = "project-layer-set"), Description(
        "Assign or clear one custom Unity layer slot. Args: index (8-31) and " +
        "name (empty/null clears the slot). Rejects duplicate names on other " +
        "layers and returns the updated layer snapshot.")]
    public static async Task<JsonElement> ProjectLayerSet(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("project-layer-set", args, ct);

    [McpServerTool(Name = "tests-list"), Description(
        "Enumerate Unity tests available to the Test Runner. Args: optional " +
        "mode (EditMode default, PlayMode), assembly_name, namespace_name, " +
        "class_name, method_name, limit, offset. Returns a paginated flat list " +
        "of matching leaf tests.")]
    public static async Task<JsonElement> TestsList(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("tests-list", args, ct);

    [McpServerTool(Name = "tests-run"), Description(
        "Start a Unity test run as an async job. Args: optional mode " +
        "(EditMode default, PlayMode), assembly_name, namespace_name, " +
        "class_name, method_name, include_passing_tests, include_logs, " +
        "include_stack_traces, max_logs. Returns job_id immediately; poll " +
        "tests-status / tests-results / tests-cancel. Rejects dirty scenes " +
        "and active compilation.")]
    public static async Task<JsonElement> TestsRun(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("tests-run", args, ct);

    [McpServerTool(Name = "tests-status"), Description(
        "Read one async Unity test job's status. Args: job_id. Returns mode, " +
        "filters, summary counts, timestamps, stored result/log counts, and " +
        "whether cancellation was requested.")]
    public static async Task<JsonElement> TestsStatus(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("tests-status", args, ct);

    [McpServerTool(Name = "tests-results"), Description(
        "Read paginated results from one async Unity test job. Args: job_id, " +
        "offset, limit, include_logs, log_offset, log_limit. Returns summary " +
        "plus a page of stored results/logs.")]
    public static async Task<JsonElement> TestsResults(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("tests-results", args, ct);

    [McpServerTool(Name = "tests-cancel"), Description(
        "Request cancellation of a running Unity test job. Args: job_id. " +
        "If the local Unity Test Runner API exposes cancellation, reify asks " +
        "it to stop the run and marks the job as cancelling/cancelled.")]
    public static async Task<JsonElement> TestsCancel(UnityClient unity, JsonElement args, CancellationToken ct
    ) => await unity.CallAsync<JsonElement>("tests-cancel", args, ct);
}
