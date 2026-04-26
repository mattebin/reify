using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reify.Server;

[McpServerResourceType]
public static class ReifyResources
{
    [McpServerResource(
        UriTemplate = "reify://about",
        Name = "reify-about",
        Title = "Reify About",
        MimeType = "application/json")]
    [Description(
        "High-level summary of the local reify server: thesis, scope, " +
        "tool/prompt/resource counts, supported clients, and bridge host/port. " +
        "Useful when orienting a client before any Unity calls.")]
    public static string About()
        => JsonSerializer.Serialize(ReifyServerCatalog.BuildSummary(), ReifyServerCatalog.Json);

    [McpServerResource(
        UriTemplate = "reify://philosophy/structured-state",
        Name = "reify-philosophy",
        Title = "Reify Philosophy",
        MimeType = "text/markdown")]
    [Description(
        "The short version of reify's operating philosophy: structured-state " +
        "first, screenshots last, read-before-write, verify-after-write, and " +
        "prefer ambiguity rejection over silent fallbacks.")]
    public static string StructuredStatePhilosophy()
        => """
           # Reify: structured-state first

           Reify is built for API agents that reason from code-shaped evidence.

           Core rules:
           - Prefer structured reads over screenshots.
           - Use stable identifiers, timestamps, and frame context.
           - Reject ambiguity instead of mutating the first match.
           - Read before write when identity or state is uncertain.
           - Verify writes by reading back the resulting state.
           - Use `batch-execute` to collect related evidence in one round trip.
           - Treat `structured-screenshot` as an opt-in escape hatch, not the default.
           - Treat `reflection-method-call` and `script-execute` as last resorts and only when explicitly enabled.

           The goal is not "make Unity clickable through MCP". The goal is "make Unity inspectable and operable through evidence that an LLM can actually reason about."
           """;

    [McpServerResource(
        UriTemplate = "reify://tools/catalog",
        Name = "reify-tool-catalog",
        Title = "Reify Tool Catalog",
        MimeType = "application/json")]
    [Description(
        "Full server-side catalog of reify MCP tools, grouped by domain with " +
        "their descriptions and backing method names. Works even when Unity is " +
        "not currently reachable.")]
    public static string ToolCatalog()
        => JsonSerializer.Serialize(ReifyServerCatalog.BuildToolCatalog(), ReifyServerCatalog.Json);

    [McpServerResource(
        UriTemplate = "reify://tools/{name}",
        Name = "reify-tool-doc",
        Title = "Reify Tool Doc",
        MimeType = "application/json")]
    [Description(
        "Look up one reify MCP tool by name and return its description, domain, " +
        "container type, and backing method name.")]
    public static string ToolDoc(string name)
        => JsonSerializer.Serialize(
            ReifyServerCatalog.GetToolDocOrThrow(name),
            ReifyServerCatalog.Json);

    [McpServerResource(
        UriTemplate = "reify://orient/the-evidence-guides-loop",
        Name = "reify-evidence-guides-loop",
        Title = "Reify: evidence + guides loop",
        MimeType = "text/markdown")]
    [Description(
        "The core operating discipline an LLM is expected to follow when " +
        "using reify. Short. Read before making the first call that actually " +
        "changes anything. Complements the `reify-orient` tool.")]
    public static string EvidenceGuidesLoop()
        => """
           # reify: the evidence + guides loop

           reify is not a toolbox. It is a discipline for using Unity through
           code-shaped evidence. Without the discipline, the tools will look
           like they work and quietly be wrong.

           ## The loop

           1. **Read before write.** Snapshot the scene (`scene-snapshot`),
              the asset inventory (`asset-snapshot`), or the object state
              (`spatial-primitive-evidence`, `component-get`) BEFORE you
              mutate. You cannot diff against nothing.
           2. **Compute from live evidence.** Primitive dimensions come from
              `primitive-defaults` or the `primitive_defaults` block on a
              create response. Never from memory or training.
           3. **Smallest mutation.** Do one thing per tool call. Undo-able.
              The reviewer should be able to revert by reading the transcript
              bottom-up.
           4. **Read back through the same path.** Every write tool returns
              `applied_fields` with `{field, before, after}` per ADR-002.
              That is the receipt. The tool's word on its own is not.
           5. **Anchor-prove spatial claims.** "These connect", "this is
              2m tall", "the arm reaches the torso" — each needs
              `spatial-anchor-distance` or `spatial-primitive-evidence`
              in the transcript per ADR-003. `within_tolerance: true` is
              the receipt; visual agreement is not.

           ## The guides

           Guides steer intent. Evidence grounds claims. Both are required.
           Either alone fails:

           - **Tools without guides.** An LLM fires 30 component-set-property
             calls and claims "done". No diff, no read-back. Reviewer has no
             way to check without rebuilding.
           - **Guides without tools.** An LLM reads AGENTS.md, says "I
             understand the philosophy", then eyeballs a scene. Same result
             as no reify at all.

           The `reify-orient` tool is the entry point. AGENTS.md + the three
           ADRs under docs/decisions/ + docs/AGENT_TRAPS.md are the canonical
           docs. They are short on purpose.

           ## When to file an issue

           If a reify tool behaves unexpectedly, call `reify-log-issue` with
           {issue_title, model_name, effort, context, symptom}. It writes a
           structured report to `reports/llm-issues/pending/`. The user
           reviews and decides whether to file to GitHub. Do NOT open GitHub
           issues directly.

           ## When to break the discipline

           There is one visual escape hatch (`structured-screenshot`) and
           two arbitrary-execution opt-ins (`reflection-method-call` and
           `script-execute`). They are for cases the evidence surface
           genuinely does not cover yet. "I'm in a hurry" is not one of
           those cases.
           """;

    [McpServerResource(
        UriTemplate = "reify://orient/agent-reading-list",
        Name = "reify-agent-reading-list",
        Title = "Reify: agent reading list",
        MimeType = "text/markdown")]
    [Description(
        "Compact pointer list to every normative doc in the reify repo, " +
        "ordered by reading priority for a fresh LLM. Complements the " +
        "`reify-orient` tool — pull this resource if you are a client that " +
        "prefers resources over tools.")]
    public static string AgentReadingList()
        => """
           # reify: agent reading list (ordered)

           If you just got reify connected to your MCP client, read these
           in order before you make the first write call. They are short.

           1. **[AGENTS.md](../../AGENTS.md)** — the operating loop. Twelve
              numbered rules, each actionable. Start here.
           2. **[docs/PHILOSOPHY.md](../../docs/PHILOSOPHY.md)** — the thesis.
              Why structured state, not screenshots. Why ambiguity rejection,
              not best-guess.
           3. **[docs/AGENT_TRAPS.md](../../docs/AGENT_TRAPS.md)** — five
              observed ways LLMs misuse reify, with worked examples from
              live sessions and one-line heuristics each. Read this to find
              out where you are going to trip.
           4. **[docs/decisions/ADR-001-tool-naming.md](../../docs/decisions/ADR-001-tool-naming.md)**
              — how tools are named. Affects how you will recognise calls
              in a transcript.
           5. **[docs/decisions/ADR-002-write-receipts.md](../../docs/decisions/ADR-002-write-receipts.md)**
              — every write must return before/after. Calls that do not
              return a self-proving receipt are rejected in review.
           6. **[docs/decisions/ADR-003-spatial-claims.md](../../docs/decisions/ADR-003-spatial-claims.md)**
              — spatial/geometric claims must carry anchor-based proof.
              "Looks aligned" is not a claim anyone accepts.
           7. **[docs/AGENT_PLAYBOOKS.md](../../docs/AGENT_PLAYBOOKS.md)**
              — per-client setup (Claude Code, Cursor, Windsurf, VS Code
              MCP). Skip if you are already connected.
           8. **[docs/GETTING_STARTED.md](../../docs/GETTING_STARTED.md)**
              — install + substitute `<PATH_TO_REIFY>`. For the human
              setting this up.
           9. **[CONTRIBUTING.md](../../CONTRIBUTING.md)** — the add-a-tool
              contract. For humans or agents proposing new tools.

           Total reading time: ~10 minutes. Every one of them exists because
           a live session produced a failure mode the doc was written to
           prevent.
           """;
}
