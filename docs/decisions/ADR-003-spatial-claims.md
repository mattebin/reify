# ADR-003 — Spatial claims require anchor-based proof

## Context

Three live builds (stickman v1, stickman v2, staircase + handrail + pillars)
surfaced a pattern: even with the full evidence surface in place, agents would
default to visual intuition for spatial correctness. Stickman v1 was built by
eye and shipped with a 1.92m total height and non-connecting limbs. Stickman
v2, the staircase, and the handrail all used `spatial-primitive-evidence` +
`spatial-anchor-distance` and shipped with every joint proven to `dist=0.000m`.

The failure mode in v1 wasn't a missing tool. It was an unwritten norm. A
reviewer reading the transcript had no way to reject "these pieces connect"
as an unsupported claim. Visual agreement between the writer and reviewer —
or silence from the reviewer — was passing as proof.

ADR-002 locked the same shape for write receipts: every mutation returns
`applied_fields` with `before/after`, so a write cannot rubber-stamp itself.
This ADR extends that norm to geometry.

## Decision

**Any claim about spatial relationships MUST be backed by anchor- or
bounds-derived evidence in the same transcript.** Specifically:

1. Statements of the form "X touches Y", "X sits on Y", "X is parallel to
   Y", "X is inside Y", "total height is Nm" MUST be paired with output
   from one of:
   - `spatial-anchor-distance` with `within_tolerance: true`, or
   - `spatial-primitive-evidence` with anchor values that satisfy the
     claim by direct comparison, or
   - `mesh-native-bounds` / `renderer.bounds` numeric comparison.
2. Tolerance MUST be explicit. Default is 5mm (`0.005`) for construction
   work; tighter tolerances require explicit justification.
3. "Within tolerance" is not the only datum. When `within_tolerance: false`,
   the `axis_gap_meters` decomposition MUST be examined before concluding
   the geometry is wrong. A same-magnitude gap across multiple similar
   joints usually means anchor-convention or axis-sign error, not a build
   error.
4. Primitive dimensions MUST come from `primitive-defaults` or
   `spatial-primitive-evidence`, not agent memory.
5. For rotated primitives, the `transform.right/up/forward` fields of
   `spatial-primitive-evidence` MUST be consulted before reasoning about
   which world direction a local anchor points. The asymmetric-arm trap
   from stickman v2 is the canonical example: same mesh, same Z=90°
   rotation, but local `-Y` lands on opposite world X sides depending on
   whether the object sits at +X or -X.

## What this forbids

- **Visual agreement as proof.** "Looks fine" / "I can see they line up" is
  not a claim anyone can reject, so it cannot be a claim anyone accepts.
- **Memorised dimensions.** "Unity's Capsule is 2m tall, so scale.y = 0.5
  gives height 1m" skips the `primitive-defaults` call that proves it for
  THIS version of Unity. Values drift across versions.
- **Unqualified connection claims.** "The head connects to the torso" with
  no `spatial-anchor-distance` result in the transcript.

## What this requires of tools

- `gameobject-create` with `primitive` set already returns
  `primitive_defaults` + `mesh_bounds` + `world_height` in its receipt
  (per ADR-002 + the primitive-defaults retrofit). This ADR makes that
  receipt load-bearing: reviewers read it rather than trust the caller.
- `spatial-anchor-distance` always returns `axis_gap_meters` per-axis
  alongside the scalar `distance_meters`. This is the datum that
  disambiguates "geometric error" from "naming error".
- `spatial-primitive-evidence` always returns the full `transform` basis
  (`right`, `up`, `forward` in world coords) for rotated objects.

## Enforcement

- `CONTRIBUTING.md § Spatial proof` points to this ADR.
- `docs/AGENT_TRAPS.md` documents the anchor-convention + rotation
  asymmetry traps with worked examples from live sessions.
- `tests/integration/test_reify_contract.py` already asserts
  `spatial-primitive-evidence` shape; extend with a case asserting
  `axis_gap_meters` presence on any non-zero `spatial-anchor-distance`
  result.

## Relationship to ADR-001, ADR-002

- ADR-001 locks tool naming.
- ADR-002 locks write receipts.
- ADR-003 locks spatial claims.

Same pattern each time: the primitive (name / receipt / anchor proof)
exists; the ADR converts it from "available" to "required". A reviewer
can cite the ADR number when rejecting a PR, which is the point — norms
that can be rewritten by the next person to touch the file aren't norms.

## Anti-rewrite clause

If this ADR is later weakened or removed, the removing change MUST cite
a live build failure caused by the ADR's constraints being too strict.
"We don't have time for proofs" is not a valid reason. The proofs take
milliseconds; omitting them cost an entire rebuild of stickman v1.
