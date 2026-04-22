using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reify.Editor.Bridge;
using UnityEngine;

namespace Reify.Editor.Tools
{
    /// <summary>
    /// Orientation tool. An LLM that just discovered reify via MCP and is about
    /// to "start making a game" is expected to hit this first. The response is
    /// deliberately compact (so it's cheap) and points at the source-of-truth
    /// docs (so it doesn't duplicate them).
    ///
    /// The philosophy: reify is not a list of tools, it's an evidence +
    /// guides discipline. Tools without the discipline are worse than no
    /// tools because they look like they work.
    /// </summary>
    internal static class OrientTool
    {
        [ReifyTool("reify-orient")]
        public static Task<object> Orient(JToken _)
        {
            return MainThreadDispatcher.RunAsync<object>(() =>
            {
                return new
                {
                    thesis = "reify is structured state + anchor-proven claims for Unity. " +
                             "Tools without the discipline (evidence + guides) are worse " +
                             "than no tools because they look like they work.",
                    the_loop = new[]
                    {
                        "1. Read before write. Snapshot the scene / asset / GO state.",
                        "2. Compute from live evidence, not memorised dimensions or assumptions.",
                        "3. Make the smallest mutation that expresses the change.",
                        "4. Read back through the same code path (writes return before/after).",
                        "5. For spatial/geometric claims, anchor-prove it (ADR-003).",
                    },
                    before_you_build = new[]
                    {
                        "Call ping to confirm Unity is alive.",
                        "Call reify-self-check. Expect fail_count=0.",
                        "Scan reify-tool-list for the live tool inventory — do not memorise from outside sources.",
                        "Read AGENTS.md, docs/PHILOSOPHY.md, and docs/AGENT_TRAPS.md. They are short.",
                        "If you are about to make a spatial claim, also read docs/decisions/ADR-003-spatial-claims.md.",
                    },
                    // Concrete doc pointers — not prescriptive, but findable.
                    read_these = new[]
                    {
                        new { path = "AGENTS.md",
                              why = "the operating loop. 11 numbered rules, all actionable." },
                        new { path = "docs/PHILOSOPHY.md",
                              why = "why evidence, not screenshots. The thesis, long form." },
                        new { path = "docs/AGENT_TRAPS.md",
                              why = "five observed ways LLMs misuse reify. Each has a one-line heuristic." },
                        new { path = "docs/decisions/ADR-001-tool-naming.md",
                              why = "tool naming convention. Affects how you will recognise tools in transcripts." },
                        new { path = "docs/decisions/ADR-002-write-receipts.md",
                              why = "every write returns before/after. Reviewers reject writes that don't." },
                        new { path = "docs/decisions/ADR-003-spatial-claims.md",
                              why = "connection/alignment/height claims require anchor-based proof." },
                        new { path = "docs/AGENT_PLAYBOOKS.md",
                              why = "client-specific setup (Claude Code, Cursor, Windsurf, VS Code MCP)." },
                    },
                    high_leverage_tools = new[]
                    {
                        new { name = "batch-execute",
                              use_for = "collect many reads in one round trip" },
                        new { name = "scene-snapshot + scene-diff",
                              use_for = "prove what changed between two points in time" },
                        new { name = "spatial-primitive-evidence",
                              use_for = "read world bounds, anchors, and transform basis of a GO" },
                        new { name = "spatial-anchor-distance",
                              use_for = "prove two shapes touch / align / are apart by X metres" },
                        new { name = "primitive-defaults",
                              use_for = "intrinsic dimensions of Cube/Sphere/Capsule/Cylinder/Plane/Quad" },
                        new { name = "geometry-line-primitive",
                              use_for = "create a Capsule/Cylinder/Cube from world-point A to B (no rotation math)" },
                        new { name = "reify-log-issue",
                              use_for = "report a bug or unexpected behavior for the user to triage" },
                        new { name = "reify-self-check",
                              use_for = "9-point install verification; run after any domain reload" },
                    },
                    also_see = new
                    {
                        mcp_resources_you_can_pull = new[]
                        {
                            "reify://about",
                            "reify://philosophy/structured-state",
                            "reify://orient/the-evidence-guides-loop",
                            "reify://orient/agent-reading-list",
                        },
                        mcp_prompts_you_can_invoke = new[]
                        {
                            "reify-structured-diagnosis",
                            "reify-safe-change-loop",
                        },
                    },
                    honesty_clause = "If you start building without orienting, you will trip one of the traps " +
                                     "in AGENT_TRAPS.md within a dozen calls. The traps are not hypothetical — " +
                                     "they are from live sessions. Reading them first is a one-minute investment " +
                                     "that pays back in the first spatial claim.",
                    read_at_utc = DateTime.UtcNow.ToString("o"),
                    frame       = (long)Time.frameCount
                };
            });
        }
    }
}
