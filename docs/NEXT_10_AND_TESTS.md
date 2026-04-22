# Next 10 and test 10

This is the current short list for the most worthwhile next work in `reify`.
It is intentionally mixed: some items add missing Unity surface area, and some
raise the trust/usability floor of what already exists.

## Top 10 next picks

1. Flatten newer MCP wrapper argument shapes.
   Recent/high-value wrappers have now been flattened for `meta`,
   `project pipeline`, `final batch`, and `import settings`. The remaining
   work is to finish flattening older legacy `JsonElement args` wrappers in
   the rest of the server surface.
2. Finish live validation of the newest write/async batch.
   `tests-run/status/results/cancel`, `asmdef-update-or-create/delete`, and
   `project-layer-set` should all have clean live round-trips recorded.
3. Add a Frame Debugger domain.
   This is still a meaningful blind spot for render-order and draw-call
   problems that structured scene state alone cannot fully explain.
4. Add a Memory Profiler domain.
   Reify already covers frame stats and memory info at a lighter level, but a
   stronger memory surface would close a real debugging gap.
5. Add Shader Graph / Sub Graph inspection.
   Read-first coverage would be enough to start and would fit the project's
   evidence-first style well.
6. Add VFX Graph inspection.
   Same logic as Shader Graph: start read-only if authoring depth is too
   expensive initially.
7. Add multi-instance Unity Editor discovery.
   This becomes important once clone workflows or parallel validation editors
   are common.
8. Add MPPM multiplayer play mode support.
   This is niche, but it is still real Unity surface area and fits the "100%
   by API" goal.
9. Unify long-running job patterns beyond tests.
   Builds, heavy imports, bakes, and similar operations should converge on a
   `run/status/results/cancel` style instead of bespoke behavior.
10. Harden write-side evidence further.
    Focus on destructive asset/prefab/package operations: richer provenance,
    clearer before/after state, and stronger read-back guarantees.

## Top 10 validation tests

1. Session bootstrap through real MCP.
   Verify `ping`, `reify-version`, and `reify-tool-list` end-to-end through
   the stdio server, not just the raw bridge.
2. Unity refresh/re-focus regression.
   After editing the local file package, confirm Unity picks up the change only
   after focus/refresh and that stale-state failures are not misclassified as
   code bugs.
3. `batch-execute` schema + runtime smoke.
   Validate both the MCP wrapper argument shape and the direct Unity-side
   execution path.
4. `tests-list` and `tests-run` on a clean scene.
   Save all dirty scenes first, then verify job creation, status polling,
   results pagination, and cancel behavior.
5. `asmdef-update-or-create` full temp round-trip.
   Create a temporary asmdef, inspect it, patch it, inspect again, then delete
   it cleanly.
6. `project-tag-add/remove` reversible smoke.
   Add a temporary tag, confirm it appears in Unity state, then remove it and
   confirm cleanup.
7. `project-layer-set` reversible smoke.
   Use an unused custom slot, verify the rename/clear loop, then restore it to
   the original state.
8. Response-size cap behavior.
   Run an intentionally large query and confirm `RESPONSE_TOO_LARGE` is
   returned instead of a broken transport.
9. Reflection escape hatch validation.
   Confirm `reflection-method-find` works normally and
   `reflection-method-call` stays gated unless the explicit opt-in env var is
   set.
10. Dirty-scene / compile / domain-reload guards.
    Validate that the tools which should refuse unsafe operations do so with
    clear, structured error messages.

## Selection logic

These lists are not "biggest possible feature count" lists. They are ordered
to improve one of three things:

- missing Unity surface area
- client usability
- trustworthiness of evidence

That ordering keeps `reify` on its actual differentiator: Unity by API, with
structured evidence strong enough for an LLM to trust.
