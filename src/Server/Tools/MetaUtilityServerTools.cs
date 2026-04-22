using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server.Tools;

[McpServerToolType]
public static class MetaUtilityServerTools
{
    [McpServerTool(Name = "reify-log-issue"), Description(
        "Write a structured bug / unexpected-behavior report to " +
        "reports/llm-issues/pending/. The user gates GitHub submission " +
        "via `python scripts/review-llm-issues.py` — this tool does NOT " +
        "file issues directly. See reports/llm-issues/README.md for the " +
        "full flow and ADR-003 for when to use it. " +
        "Required: issue_title, model_name, effort (S/M/L), context, " +
        "symptom. Optional: severity (info/warn/error/critical, default " +
        "warn), affected_tool, reproduction_steps[], logs, suggested_fix, " +
        "reify_commit. Auto-captured: unity_version, platform, " +
        "frame_at_detection, timestamp_utc, reify_tool_count.")]
    public static async Task<JsonElement> ReifyLogIssue(
        UnityClient unity,
        string issue_title,
        string model_name,
        string effort,
        string context,
        string symptom,
        string? severity = null,
        string? affected_tool = null,
        string[]? reproduction_steps = null,
        string? logs = null,
        string? suggested_fix = null,
        string? reify_commit = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("reify-log-issue", new
    {
        issue_title, model_name, effort, context, symptom,
        severity, affected_tool, reproduction_steps, logs,
        suggested_fix, reify_commit
    }, ct);

    [McpServerTool(Name = "reify-list-pending-issues"), Description(
        "List LLM-reported issues in reports/llm-issues/pending/ that " +
        "are awaiting the user's review. Read-only.")]
    public static async Task<JsonElement> ReifyListPendingIssues(UnityClient unity, CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("reify-list-pending-issues", null, ct);

    [McpServerTool(Name = "geometry-line-primitive"), Description(
        "Create a Capsule, Cylinder, or Cube stretched along a world-" +
        "space segment from `from` to `to` with given `thickness`. " +
        "Eliminates the rotation-math trap for rail, strut, or arrow " +
        "primitives — the tool computes length, midpoint, and the " +
        "quaternion that orients local +Y along (to - from). Returns " +
        "ADR-002 applied_fields + endpoint_proofs block whose " +
        "gap_top_meters / gap_bottom_meters prove the placement " +
        "without a follow-up anchor-distance call. Use for handrails, " +
        "guy wires, connecting rods, debug lines.")]
    public static async Task<JsonElement> GeometryLinePrimitive(
        UnityClient unity,
        JsonElement from,
        JsonElement to,
        float? thickness = null,
        string? name = null,
        string? primitive = null,
        string? parent_path = null,
        CancellationToken ct = default
    ) => await unity.CallAsync<JsonElement>("geometry-line-primitive", new
    {
        from, to, thickness, name, primitive, parent_path
    }, ct);
}
