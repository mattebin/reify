using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace Reify.Server;

[McpServerPromptType]
public static class ReifyPrompts
{
    [McpServerPrompt(Name = "reify-structured-diagnosis")]
    [Description(
        "Guide an LLM through evidence-first diagnosis in Unity. Args: goal, " +
        "optional symptoms. Emphasizes batch reads, identity checks, and " +
        "structured-state tools before screenshots.")]
    public static string StructuredDiagnosis(string goal, string? symptoms = null)
    {
        var domains = string.Join(
            ", ",
            ReifyServerCatalog.GetToolDocs()
                .Select(t => t.Domain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        var sb = new StringBuilder();
        sb.AppendLine("Diagnose this Unity problem with reify's structured-state workflow.");
        sb.AppendLine();
        sb.AppendLine($"Goal: {goal}");
        if (!string.IsNullOrWhiteSpace(symptoms))
        {
            sb.AppendLine($"Symptoms: {symptoms}");
        }

        sb.AppendLine();
        sb.AppendLine("Operating rules:");
        sb.AppendLine("- Start with read tools, not writes.");
        sb.AppendLine("- Prefer one `batch-execute` call for related evidence collection.");
        sb.AppendLine("- Preserve `read_at_utc`, `frame`, and stable identifiers from every response.");
        sb.AppendLine("- Reject ambiguous identity; qualify by scene/object/component when needed.");
        sb.AppendLine("- Use `structured-screenshot` only if structured-state cannot answer the question.");
        sb.AppendLine("- Use `reflection-method-call` or `script-execute` only as opt-in escape hatches after native tools fail.");
        sb.AppendLine();
        sb.AppendLine($"Available high-level domains: {domains}.");
        sb.AppendLine();
        sb.AppendLine("Output shape:");
        sb.AppendLine("1. State the likely evidence domains to inspect.");
        sb.AppendLine("2. Propose the smallest useful batch of read calls.");
        sb.AppendLine("3. Explain what would confirm or falsify each hypothesis.");
        sb.AppendLine("4. Only then suggest the minimal write/verification loop, if a fix is needed.");
        return sb.ToString().TrimEnd();
    }

    [McpServerPrompt(Name = "reify-safe-change-loop")]
    [Description(
        "Generate a safe read-modify-verify plan for a Unity change. Args: " +
        "change, optional target, optional constraints. Emphasizes ambiguity " +
        "rejection, post-state verification, and rollback awareness.")]
    public static string SafeChangeLoop(string change, string? target = null, string? constraints = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Apply this Unity change with a reify-safe loop.");
        sb.AppendLine();
        sb.AppendLine($"Requested change: {change}");
        if (!string.IsNullOrWhiteSpace(target))
        {
            sb.AppendLine($"Target: {target}");
        }
        if (!string.IsNullOrWhiteSpace(constraints))
        {
            sb.AppendLine($"Constraints: {constraints}");
        }

        sb.AppendLine();
        sb.AppendLine("Required flow:");
        sb.AppendLine("1. Read the current state first and capture stable identifiers.");
        sb.AppendLine("2. If identity is ambiguous, resolve that before any mutation.");
        sb.AppendLine("3. Perform the smallest write that can achieve the change.");
        sb.AppendLine("4. Read back the exact fields or objects that should have changed.");
        sb.AppendLine("5. Compare expected vs actual result using returned evidence, not screenshots.");
        sb.AppendLine("6. If native tools cannot express the last missing step, consider reflection or script execution only if explicitly enabled.");
        sb.AppendLine();
        sb.AppendLine("When producing the answer, separate:");
        sb.AppendLine("- preflight reads");
        sb.AppendLine("- write call(s)");
        sb.AppendLine("- verification reads");
        sb.AppendLine("- failure/rollback conditions");
        return sb.ToString().TrimEnd();
    }

    [McpServerPrompt(Name = "reify-capability-escalation")]
    [Description(
        "Choose the right escalation path for a task when native coverage is " +
        "uncertain. Args: task, optional missing_capability. Orders the " +
        "fallbacks from native tools to batch reads to screenshots to " +
        "reflection/script execution, with philosophy-aware guardrails.")]
    public static string CapabilityEscalation(string task, string? missingCapability = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Plan the least-risky capability escalation for this Unity task.");
        sb.AppendLine();
        sb.AppendLine($"Task: {task}");
        if (!string.IsNullOrWhiteSpace(missingCapability))
        {
            sb.AppendLine($"Suspected gap: {missingCapability}");
        }

        sb.AppendLine();
        sb.AppendLine("Escalation order:");
        sb.AppendLine("1. Native reify read/write tools.");
        sb.AppendLine("2. `batch-execute` to compose multiple native reads/writes efficiently.");
        sb.AppendLine("3. `structured-screenshot` only if the answer is truly visual.");
        sb.AppendLine("4. `reflection-method-find` to discover a narrow reflection escape hatch.");
        sb.AppendLine("5. `reflection-method-call` or `script-execute` only if enabled and only for the missing edge.");
        sb.AppendLine();
        sb.AppendLine("For the answer:");
        sb.AppendLine("- explain why each earlier level is sufficient or insufficient");
        sb.AppendLine("- call out the exact trust/safety tradeoff introduced by escalation");
        sb.AppendLine("- prefer adding a native tool later if reflection becomes repeat work");
        return sb.ToString().TrimEnd();
    }
}
