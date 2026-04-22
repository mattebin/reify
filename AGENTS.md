# reify agent instructions

If you are an LLM operating in this repo, treat `reify` as a structured-state
Unity backend, not as a screenshot-first Unity assistant.

> **Before your first write call.** `reify` is an **evidence + guides**
> discipline, not a list of tools. The tools without the discipline look
> like they work and are quietly wrong — and every failure mode has a
> one-line fix in [`docs/AGENT_TRAPS.md`](docs/AGENT_TRAPS.md) that a
> minute of reading would have prevented. If you are tempted to skip to
> "let's make a game now", run `reify-orient` or pull the
> `reify://orient/the-evidence-guides-loop` MCP resource first. It takes
> less time than the first debug cycle you will spend if you don't.

## Core operating loop

1. Start by orienting:
   - call `reify-orient` (one MCP call; returns the shortlist + the loop)
   - read [README.md](README.md), [docs/PHILOSOPHY.md](docs/PHILOSOPHY.md),
     and [docs/AGENT_TRAPS.md](docs/AGENT_TRAPS.md) — all short
   - skim [docs/decisions/](docs/decisions/): ADR-001 (naming),
     ADR-002 (write receipts), ADR-003 (spatial claims) — these are
     normative, reviewers reject PRs that violate them
   - read [docs/AGENT_PLAYBOOKS.md](docs/AGENT_PLAYBOOKS.md) if you want
     client-specific guidance
2. Confirm the stack is alive before deeper work:
   - `ping` (returns an `orient_hint` field; obey it the first time)
   - `reify-version`
   - `reify-tool-list`
   - `reify-self-check` (expect `fail_count: 0`)
3. Prefer structured reads before writes.
4. Prefer `batch-execute` for related evidence collection.
5. Preserve `read_at_utc`, `frame`, and stable identifiers from tool output.
6. Reject ambiguous identity instead of guessing.
7. After code/package edits, assume Unity may be stale until it regains focus
   or `Assets/Refresh` runs. Do not chase ghost errors before a refresh.
8. Use `structured-screenshot` only if structured-state cannot answer the
   question.
9. Use `reflection-method-call` only as an explicit opt-in escape hatch.
10. After mutations, verify by reading back through the same Unity code path.
11. For spatial/layout claims, use geometry evidence before concluding
    something "looks right":
   - `primitive-defaults`
   - `spatial-primitive-evidence`
   - `spatial-anchor-distance`
   - See [ADR-003](docs/decisions/ADR-003-spatial-claims.md) — spatial
     claims are normatively required to carry anchor-based proof.
   - See [docs/AGENT_TRAPS.md](docs/AGENT_TRAPS.md) for the observed
     failure modes (anchor convention, rotation asymmetry, etc.) and
     one-line heuristics for each.
12. If a tool behaves unexpectedly or you suspect a reify bug, use
    `reify-log-issue` to write a structured report under
    `reports/llm-issues/`. The user reviews pending reports and chooses
    which to file as GitHub issues via `python scripts/review-llm-issues.py`.
    Do not open GitHub issues directly.

## Evidence rules

- Prefer tool output over assumptions.
- Prefer scene-qualified and instance-id-based identity when available.
- Treat dirty scenes, compile state, and domain reload state as first-class
  guards, not incidental noise.
- If a write tool succeeds but post-state is not yet trustworthy, say so.
- If you claim two shapes connect, align, overlap, or reach a target height,
  prove it from anchors/bounds rather than from rough placement intuition.

## High-value discovery tools

- `reify-orient` — start-here entry point; returns the shortlist + loop
- `reify-tool-list`, `reify-version`, `reify-self-check`
- `batch-execute` — many reads, one round trip
- `domain-reload-status`, `persistence-status`
- `scene-snapshot` + `scene-diff` — prove what changed
- `spatial-primitive-evidence`, `spatial-anchor-distance`, `primitive-defaults`
- `geometry-line-primitive` — A-to-B primitive without rotation math
- `tests-list`, `tests-status`
- `reify-log-issue` — structured bug reporting (user-gated GitHub submission)

## Current validation nuance

This repo is often edited live through a local Unity file package reference.
If a just-edited tool still behaves like old code, refresh/re-focus Unity
before concluding the patch failed.
