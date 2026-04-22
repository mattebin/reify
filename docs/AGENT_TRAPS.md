# Agent traps — live-session failure modes worth memorising

Every trap in this file has been observed in a live reify session against a
running Unity. The fix is a one-line heuristic an agent can apply on first
encounter; the worked example shows the exact shape of the symptom. This
document is normative: it is referenced from [ADR-003](decisions/ADR-003-spatial-claims.md)
and from [AGENTS.md](../AGENTS.md). Edits that remove a trap without
documenting why are reverted on review.

## Trap 1 — Multiple similar joints show the same non-zero gap

### Symptom

```
[GAP] Step1 top_back ↔ Step2 bottom_front  dist=0.60000m
[GAP] Step2 top_back ↔ Step3 bottom_front  dist=0.60000m
[GAP] Step3 top_back ↔ Step4 bottom_front  dist=0.60000m
[GAP] Step4 top_back ↔ Step5 bottom_front  dist=0.60000m
```

Four joints, same magnitude. If the build were geometrically wrong you'd
expect the gaps to vary — placement errors compound, they don't replicate.

### Heuristic

**Same-magnitude gaps across structurally similar checks = convention or
axis-sign error, not geometry.** Re-examine the anchor naming or
coordinate system BEFORE rebuilding.

### Worked example

In the session above, the tool uses `front=+Z`, `back=-Z`. The paired
anchors should have been `top_front ↔ bottom_back`, not `top_back ↔
bottom_front`. Swapping the anchor names (zero rebuild) turned all four
gaps to `0.00000m`.

## Trap 2 — `within_tolerance: false` on a joint that looks aligned

### Symptom

```
[GAP] L.leg top meets torso bottom  dist=0.1500m  gap=(x=0.150 y=0.000 z=0.000)
```

`within_tolerance` is false, but the distance is pure-X. The Y gap is 0.

### Heuristic

**Read `axis_gap_meters` before concluding the geometry is wrong.** If a
single axis accounts for the entire distance, the "gap" may be
deliberate spatial separation (a biped's legs sit to either side of the
torso centreline), not a joint failure. The correct check is
axis-specific:

```python
# Not: within_tolerance
# But: does the Y anchor I care about actually match?
assert abs(leg_top_y - torso_bottom_y) < 0.005
```

### Worked example

Stickman v2 legs: two legs at `x=±0.15`, torso at `x=0`. "Torso bottom"
as a point anchor lives at `x=0`. The `dist=0.15` between leg-top and
torso-bottom is the horizontal offset of the leg from the body midline —
physically correct. The vertical connection (`Δy=0.000`) is the real
claim to verify, and it passed.

## Trap 3 — Rotation makes "left" and "right" anchors asymmetric

### Symptom

Two identical capsules, same `rot = (0, 0, 90)` on both, positioned
symmetrically at `x = -0.45` and `x = +0.45`. The `bottom` anchor on
each:

```
LeftArm  bottom -> world (-0.15, 1.55, 0)   # inner tip (torso-ward)
RightArm bottom -> world (+0.75, 1.55, 0)   # outer tip (away from torso)
```

Same local anchor, same rotation, opposite world meanings.

### Heuristic

**A single rotation applied to mirrored positions does not produce
mirrored geometry.** To truly mirror a rotated pair, mirror the
rotation too (e.g. `(0, 0, 90)` on the left, `(0, 0, -90)` on the
right), OR accept the asymmetry and use different anchor names on each
side (`bottom` on one, `top` on the other).

### Worked example

Stickman v2 arms. `rot(0,0,90)` maps local `-Y` to world `+X`. On the
LEFT arm (centre at x=-0.45), `+X` points AT the torso — so `bottom` =
inner tip. On the RIGHT arm (centre at x=+0.45), `+X` points AWAY from
the torso — so `bottom` = outer tip. The tool is consistent; the human
intuition "bottom should mean inner tip on both sides" is not.

Verification fix: read `transform.up` / `transform.right` /
`transform.forward` from `spatial-primitive-evidence` BEFORE
interpreting an anchor name. These tell you exactly which world
direction each local axis points for this specific instance.

## Trap 4 — Memorised primitive dimensions

### Symptom

Agent assumes "Unity Capsule is 2m tall with 0.5m radius" and multiplies
out placements. Works until Unity changes a default mesh, or until
someone replaces `Capsule.fbx`, or until a custom primitive is used.

### Heuristic

**Read primitive dimensions from the scene before computing placements.**
Either:

- `primitive-defaults` tool call, or
- the `primitive_defaults` + `mesh_bounds` fields in the response of
  `gameobject-create` when a primitive was created, or
- `spatial-primitive-evidence` on an existing instance.

### Worked example

The staircase + handrail build pulled `rise = 1.16m` and `run = 2.5m`
from `spatial-primitive-evidence` on the live Step1 and Landing. The
quaternion math for the handrail used those numbers, not a remembered
spec. When the landing thickness was later adjusted, the handrail
regeneration would have picked up the new geometry automatically — no
code edits required.

## Trap 5 — Build-in-loop without batch-execute

### Symptom

Seven `gameobject-create` calls = seven HTTP round trips. The editor
becomes the bottleneck for a build that could be one call.

### Heuristic

**When creating N related GameObjects or doing N reads that feed a
single decision, use `batch-execute`.** One HTTP call, one consistent
frame, one combined receipt.

### Worked example

The staircase build used a Python loop. Seven requests, seven responses
to parse. Equivalent as a batch:

```python
call('batch-execute', {'calls': [
    {'tool': 'gameobject-create', 'args': {...}},   # Step1
    {'tool': 'gameobject-create', 'args': {...}},   # Step2
    ... etc
]})
```

Batch output is a single array of receipts, which is also the right
shape for passing to an ADR-002 reviewer.

## How to add a trap

When you find a new failure mode during a live session:

1. Add a section with the three fields: **Symptom** (verbatim output),
   **Heuristic** (one line), **Worked example** (a concrete case from
   the session).
2. Do not remove existing traps when adding new ones.
3. If a trap becomes obsolete because a tool fix eliminates the failure
   mode at the source, mark it obsolete with a pointer to the commit
   that fixed it, rather than deleting it. Future drift may bring it
   back.

## See also

- [ADR-003: spatial claims require anchor-based proof](decisions/ADR-003-spatial-claims.md)
- [AGENTS.md](../AGENTS.md) — the operating loop that references this document
- [AGENT_PLAYBOOKS.md](AGENT_PLAYBOOKS.md) — client-specific setup
