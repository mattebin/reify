# Agent playbooks

These instructions are written to be read by the model itself. The goal is to
make `reify` feel predictable across the main clients that are realistically
useful for this repo.

## Shared rule for every client

Always operate `reify` as a structured-state Unity backend:

- read before write
- batch related reads
- preserve identifiers, `read_at_utc`, and `frame`
- reject ambiguous identity instead of guessing
- verify mutations by reading back
- for geometry/spatial claims, use `primitive-defaults`,
  `spatial-primitive-evidence`, and `spatial-anchor-distance`
- if package code just changed, refresh/re-focus Unity before trusting a
  failure

Use [AGENTS.md](../AGENTS.md) as the shortest universal contract.

## Claude Code

Claude Code should use `reify` as its primary Unity evidence source, not the
 local filesystem as a proxy for Unity state.

- Start with `ping`, `reify-version`, and `reify-tool-list`.
- Read MCP resources early if you need orientation:
  - `reify://about`
  - `reify://philosophy/structured-state`
  - `reify://tools/catalog`
- Prefer `batch-execute` whenever investigating a bug or scene state.
- For object placement, "looks connected" is not enough. Read anchors/bounds
  and prove the distance is near zero.
- When a tool fails right after a package change, assume stale Unity state
  first, not broken code.
- Use `tests-*`, `asmdef-*`, and `project-tag/layer` tools instead of editing
  Unity config files manually.
- Use `structured-screenshot` only after the structured-state path bottoms out.

## Codex Desktop

Codex shares the same machine and shell as the repo, so it should split the
 work cleanly:

- use the shell for repo inspection, build verification, and docs
- use `reify` MCP for Unity truth
- do not infer Unity runtime/editor state from files when a tool exists
- when creating or laying out geometry, verify contact/alignment numerically
  before reporting success
- after patching the Unity package, re-focus or refresh Unity before judging
  the result
- use direct bridge calls only for debugging transport problems; otherwise use
  the MCP server path
- keep final reports explicit about what was live-validated vs scratch-built

## Cursor

Cursor should keep interactions short, tool-led, and explicit.

- Use `reify-tool-list` first instead of assuming tool names from memory.
- Prefer small `batch-execute` bundles over long chains of ad hoc calls.
- Ask `reify` for scene/component/project evidence before proposing code
  changes.
- Use `spatial-anchor-distance` for "touching", "aligned", "connected", and
  "2m tall" style claims.
- For write loops, use:
  1. read current state
  2. mutate once
  3. read back
  4. stop if identity becomes ambiguous
- If a response shape looks odd, trust the schema actually returned, not the
  one you expected.

## Windsurf

Windsurf should optimize for short, high-signal loops.

- Start from the narrowest tool that can answer the question.
- Prefer domain-specific reads over large generic dumps.
- Use `scene-query`, `component-get`, `material-inspect`,
  `domain-reload-status`, `persistence-status`, and spatial-evidence tools as
  diagnostic anchors.
- Treat reflection as last resort.
- If Unity was just edited, perform a refresh/re-focus step before re-running
  the same failing tool.
- Report blockers as either:
  - stale Unity state
  - dirty scene / compile guard
  - real tool failure

## VS Code MCP

VS Code MCP clients should be conservative and evidence-heavy.

- Begin with `ping` and `reify-version` on session start.
- Prefer resources/prompts for orientation when the user is vague.
- Use `tests-list` before `tests-run`.
- For spatial reasoning, prefer anchor/bounds reads over prose descriptions of
  object placement.
- Prefer the schema actually advertised by MCP over memory. Recent/high-value
  wrappers were flattened, but some older legacy wrappers may still have
  awkward shapes until the rest of the server surface is normalized.
- Keep user-visible summaries grounded in actual tool output, especially for
  project settings, package state, and scene mutations.

## Good defaults for any future client

If a new MCP-capable client is added later, it should inherit this order:

1. `ping`
2. `reify-version`
3. `reify-tool-list`
4. `batch-execute` for discovery/diagnosis
5. domain-specific read tools
6. one write at a time
7. read-back verification
8. screenshot or reflection only if the structured path fails
