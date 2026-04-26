# ADR-002 - Write-tool receipt shape

## Context

Live validation on 2026-04-22 found that reify's write tools were lying
by omission. `gameobject-modify` would return `OK` without actually
mutating the GameObject when an unknown field was sent. `gameobject-create`
returned the DTO but omitted the intrinsic mesh dimensions, forcing a
caller to search for "Unity default Capsule height". The answer lived on
reify's own disk but did not make it to the receipt.

The primitive win from this period was `component-set-property`, whose
response always includes `{before, after, applied: true}`. That shape is
self-proving: a reviewer reading the transcript does not have to trust
reify, they can verify the mutation from the receipt alone.

This ADR locks the shape as a standard.

## Decision

**Every write tool MUST return a self-proving receipt.** At minimum:

```json
{
  "applied_fields": [
    { "field": "m_LocalPosition.y", "before": 1.80, "after": 1.88 }
  ],
  "applied_count": 1,
  "read_at_utc": "...",
  "frame": 563
}
```

Plus, depending on the write's domain:

- **GameObject / Component writes**: also include the post-write DTO
  (transform, component list).
- **Asset writes** (create/copy/delete/move): also include
  `guids_touched[]` and the `AssetProvenance.Summarize` block for every
  path touched.
- **Scene writes**: pair with `scene-snapshot` + `scene-diff` for a
  structural receipt across the whole scene, not just the one object.
- **Primitive creates**: additionally surface `mesh_bounds` (local and
  world) + `primitive_defaults` so the caller knows the intrinsic
  dimensions without needing Unity docs. `gameobject-create` enforces this
  as of 2026-04-22; the standalone `primitive-defaults` tool exposes the
  same data without side effects.

## What counts as a "write"

Anything that mutates a GameObject, Component, asset, scene, project
setting, or package. Read-only inspection tools are exempt; they have their
own evidence contract: `read_at_utc` + `frame` + stable identifiers, per
ADR-001.

## Anti-patterns this forbids

1. **Silent no-op**: write tool receives unrecognised args, does nothing,
   returns `OK`. Mitigation: reject unknown keys at the envelope level.
   `gameobject-modify` enforces this.
2. **Rubber-stamp receipt**: response contains only `{ok: true}` or
   re-emits the input args. A reviewer cannot distinguish a successful write
   from a silent drop.
3. **Asymmetric receipt**: response shows the new state but not the old.
   Prevents verifying delta, only end-state. `before/after` pairs are
   required.
4. **Externalised proof**: response says "reload the scene to see the
   change". The receipt must be sufficient on its own.

## Enforcement

New writes: reviewers reject PRs that ship a write tool without the receipt
shape above. `CONTRIBUTING.md` "Write-tool contract" points to this ADR.

Existing writes: retrofitted incrementally. Priority order (high-traffic
first):

| Tool | Status | Notes |
|---|---|---|
| `component-set-property` | done: canonical `before/after` | the prior art |
| `component-modify` | done: `applied[]` + `failed[]` | already has it |
| `gameobject-modify` | done: `applied_fields[]` | fixed 2026-04-22 |
| `gameobject-create` | done: `primitive_defaults` + `mesh_bounds` | fixed 2026-04-22 |
| `asset-delete` | done: `deleted_provenance` + `guids_touched` | batch 6 |
| `asset-copy` | done: `source_provenance` + `destination_provenance` | batch 6 |
| `prefab-create` | done: `prefab_provenance` + `guids_touched` | batch 6 |
| `component-add` | done: component-type and GameObject component-count before/after | includes post-write DTO |
| `component-remove` | done: component-type and GameObject component-count before/after | includes removed snapshot |
| `asset-create` | done: `applied_fields` + created provenance + `guids_touched` | proves absent -> present |
| `asset-move` / `asset-rename` | done: path/GUID before/after + source/destination provenance | proves GUID preservation |
| `scene-create` / `scene-save` | done: `applied_fields` + provenance or disk dirty/write evidence | create/save receipts |
| `animator-parameter-set` | done: `{before, after, type}` | already has it |
| `animator-crossfade` / `animator-play` | done: state/transition before/after evidence | runtime-state receipt |

## Why this matters for "reliable + verifiable"

The philosophy since day one was "structured evidence, not screenshots."
That breaks down for writes if the receipt does not prove the write
happened. A reviewer reading the transcript has to trust either the tool's
word that it succeeded or re-execute a separate read tool to verify. Both
undermine the single-call trust that reads already have via `read_at_utc`
and stable IDs.

With the shape locked: one call, self-proving. That is the "verifiable" half
of the claim.
