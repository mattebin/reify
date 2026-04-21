# ADR-001 — Tool naming and response shape convention

**Status:** Accepted, 2026-04-21.
**Supersedes:** none.

## Context

Every tool in reify exists as a pair: an MCP-exposed method on the server
(`src/Server/Tools/`) and an Editor-side handler (`src/Editor/Tools/`),
joined by a string key registered in `ReifyBridge`. With ~25 Phase B tools
queued and another ~25 in Phase C, conventions chosen carelessly now will
cost real time in porting churn later. This ADR locks the rules once so
future PRs don't re-litigate them.

## Decisions

### 1. Tool names

- **kebab-case**, lowercase ASCII, no abbreviations.
- **Noun-first for reads, verb-first for writes.** `mesh-native-bounds`,
  `scene-list`, `material-inspect` are reads. `scene-open`, `scene-save`,
  `gameobject-create` are writes. A read tool does not say "get-". A write
  tool does not say "do-".
- **Domain prefix.** `scene-*`, `gameobject-*`, `component-*`,
  `material-*`, `asset-*`, `script-*`, `animator-*`, `editor-*`,
  `console-*`. One domain per name; no `scene-and-gameobject-*`.
- **Never pluralise the domain.** `scene-list`, not `scenes-list`.
  Plurality lives in the return shape, not the name.
- **Reserved verbs for writes:** `create`, `open`, `save`, `close`,
  `destroy`, `duplicate`, `modify`, `set-*` (for single-property writes),
  `add`, `remove`, `refresh`.
- **Reserved verbs for reads:** none — read tools are nouns. Not
  `get-scene-list`; just `scene-list`. Not `inspect-material`; just
  `material-inspect`.

### 2. File organisation

- One tool per file on both sides:
  - `src/Server/Tools/SceneOpenTool.cs` (PascalCase, suffix `Tool`).
  - `src/Editor/Tools/SceneOpenTool.cs` (same name; sibling by convention).
- Once a domain has three or more tools, flip to Murzak-style partial
  classes:
  - `src/Server/Tools/Tool_Scene.Open.cs`, `Tool_Scene.Save.cs`,
    `Tool_Scene.Create.cs`, all `public static partial class Tool_Scene`.
  - Same on the Editor side.
- The partial-class flip is mechanical — rename files, add `partial`,
  move the static class declaration to a root `Tool_Scene.cs`. Do it
  when crossing the threshold, not before.

### 3. Response shape

Every tool response is a flat-ish JSON object with:

- **Domain data at the top.** No `{"result": {...}}` wrapper. The envelope
  lives at the bridge layer, not the tool layer.
- **Mandatory metadata fields** on every read and every write:
  - `read_at_utc` — ISO-8601 UTC timestamp of when the handler sampled
    Unity state.
  - `frame` — `Time.frameCount` at sample time (long).
- **Unit suffixes in key names** when the unit is ambiguous:
  - Linear distances: `_meters`. Angles: `_degrees` or `_radians`
    (pick one; prefer degrees for UI-facing, radians for math-facing —
    name it so the caller knows).
  - Colors: `_rgba` (0–1 floats) or `_srgb_rgba` (0–255 ints); state
    which explicitly.
- **Code identifiers over display names.** Return component type FQNs,
  asset GUIDs, layer indices, render queue numbers. If you also want a
  display name, emit both (`display_name` + `type_fqn`).
- **Stable array ordering.** Return elements in a documented order
  (hierarchy depth-first, asset-path alphabetical, etc.). Non-determinism
  in ordering means caller diffs break pointlessly.

### 4. Arguments

- Arg objects use `snake_case` keys on the wire, mapped to C# properties
  via `[JsonPropertyName(...)]`. Same convention as responses.
- Required args are non-nullable; optional args are nullable with
  documented defaults.
- Positional-only signatures (one string, one int) are permitted for
  tools with one obvious argument — but the MCP tool description must
  state the argument's semantics precisely.

### 5. Writes verify by reading back

- Any write that can be observed (set a transform, change a material
  colour) MUST read the mutated state back through Unity's own code path
  and return it as the response's `data`. Do not echo the input arguments
  as "success".
- `read_at_utc` on the response reflects the *read-back* time, not the
  write time.
- Writes that have no observable return (e.g., `scene-save` to disk) still
  return `read_at_utc` + `frame` + the outcome Unity reports
  (`is_saved_to_disk: true`), not a generic `ok: true`.

### 6. Error handling

- Thrown exceptions on the Editor side become bridge errors with code
  `TOOL_EXCEPTION`. The handler should not catch-and-return — let it throw.
- Domain-level failures that are *not* exceptions (scene file not found,
  asset path malformed) return normally with a documented error field.
  Reserve exceptions for "Unity itself is in a bad state."
- Error codes live in `UPPER_SNAKE_CASE`: `SCENE_NOT_FOUND`,
  `ASSET_PATH_INVALID`, `UNITY_BUSY`. Document new codes in this ADR.

### 7. Mutations go through Unity's Undo system

Per Phase D pattern but adopted now — every write tool that mutates
scene or asset state calls the appropriate `Undo.*` method before the
mutation. Tool descriptions should mention the undo label.

Example: `gameobject-create` → `Undo.RegisterCreatedObjectUndo(go, "Reify: create GameObject")`.

## Consequences

- Every new tool PR gets reviewed against §§1–7. Reviewer's checklist is
  short because the rules are short.
- Renaming a tool after it ships is a breaking change — clients that
  hardcode the old name break. Plan names carefully on first ship.
- The "writes verify by reading back" rule makes simple tools slightly
  slower and more code. Accepted — this is the entire point of the
  structured-state philosophy.

## How to apply to an existing upstream port

1. Read the upstream implementation.
2. Rewrite the tool name per §1.
3. Strip any `{"result": ...}` wrapper per §3.
4. Add `read_at_utc` + `frame`.
5. Convert display names to code identifiers.
6. Wrap mutations in `Undo.*` calls per §7.
7. Preserve the upstream attribution header from `NOTICE` policy.

## Open questions deferred

- Pagination shape for large responses (§Phase D polish pattern on
  response size). Defer until we have a response that exceeds ~256 KB.
- Streaming tool responses (MCP supports it). Not needed for Phase A or
  B; revisit when a tool produces genuinely large output (e.g.,
  `scene-hierarchy` on a 10k-node scene).
