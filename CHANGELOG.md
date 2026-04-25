# Changelog

All notable changes to reify are documented here. Ordering follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versioning follows
[Semantic Versioning](https://semver.org/) once the public API stabilises.

## [Unreleased]

## [0.3.0] - 2026-04-25

### Added
- `physics2d-*` query domain (raycast, raycast-all, overlap-circle,
  overlap-box, settings) - 2D mirror of the 3D physics surface.
- `ui-toolkit-*` domain - UIDocument inspect, VisualElement tree walk,
  UXML + USS asset inspection.
- `animation-clip-events-read` / `animation-clip-events-set` - AnimationEvent
  CRUD that complements the existing clip tools.
- `frame-debugger-status` / `frame-debugger-set-enabled` - Frame Debugger
  state via reflection into `UnityEditorInternal.FrameDebuggerUtility`.
- `tmp-text-inspect` / `tmp-text-set` / `tmp-font-asset-inspect` -
  TextMesh Pro surface via reflection, package-gated on
  `com.unity.textmeshpro`.
- `shader-inspect` - Shader declaration side: ShaderUtil property dump,
  keyword enumeration, is_supported, pass/subshader counts.
- `shader-graph-inspect` - `.shadergraph` / `.shadersubgraph` importer
  inspection, package-gated on `com.unity.shadergraph`.
- `visual-effect-inspect` / `visual-effect-asset-inspect` - VFX Graph
  reflection, package-gated on `com.unity.visualeffectgraph`.
- `scene-snapshot` / `scene-diff` - first-class structural scene diff.
  Prove exactly which GameObjects, components, and transform fields a
  write touched, without a screenshot.
- `asset-snapshot` / `asset-diff` - first-class asset-database diff.
  Added/removed/moved (by GUID stability) / modified (by length +
  last-write-utc delta) receipts for imports / moves / refactors.
- `memory-snapshot-capture` - built-in Unity memory snapshot (.snap
  producer) via `UnityEngine.Profiling.Memory.Experimental.MemoryProfiler
  .TakeSnapshot`. Produces the artifact without requiring
  `com.unity.memoryprofiler` (which is only needed to open the file).
- Iteration-loop tool batch: `editor-await-compile`,
  `editor-await-event`, `compile-errors-structured`,
  `compile-errors-snapshot`, `compile-errors-diff`,
  `tests-coverage-map`, `console-log-summarize`,
  `editor-log-tail`, `reify-health`, `editor-call-deferred`,
  `editor-deferred-list`, `editor-deferred-result`,
  `editor-deferred-cancel`, and the `recipe-*` helpers.
- Project settings and preferences surfaces:
  `project-settings-asset-read`, `project-settings-asset-write`,
  `editor-prefs-*`, and `player-prefs-*`.
- MCP/server parity wrappers for the remaining editor tools so the
  live editor registry and stdio server registry now expose the same
  258-tool surface.
- `scene-snapshot` pagination via `cursor` + `page_size`, with
  explicit `is_complete_snapshot`, `returned_count`, and `next_cursor`
  metadata for large real-world scenes.
- CI smoke test that boots the real MCP stdio server, initializes it,
  and verifies `tools/list` exposes the expected public tool surface.

### Changed
- GUID provenance retrofitted onto `asset-delete`, `asset-copy`, and
  `prefab-create` - write-side receipts now include `guids_touched[]`
  and before/after `*_provenance` blocks.
- Flattened the eight remaining legacy `JsonElement args` server
  wrappers (build-target-switch, build-execute, structured-screenshot,
  terrain-*, constraint-*, lod-group-*, tilemap-*, particle-*, camera-*,
  light-*, animator-parameter-set / crossfade / play, physics-*). MCP
  clients now see named, typed parameters with schema, not an opaque
  `args` blob.
- `scene-snapshot` now auto-compacts repeated component type names into
  a component type table for large scenes. A 1000-GameObject scene that
  previously exceeded the default response cap now fits comfortably
  under `REIFY_MAX_RESPONSE_BYTES`.
- `scene-diff` now refuses paginated/partial snapshots by default
  instead of pretending a page is a complete scene. Pass
  `allow_partial=true` when intentionally diffing only included paths.
- `reify-health` now reports bridge URL/port, configured response cap,
  process name, readiness, console counts, and tool count in one place.
- README and GitHub repo metadata now describe compatibility as any
  MCP-capable LLM client, with Claude/Cursor/VS Code/Windsurf configs
  kept as examples rather than the boundary of support.

### Fixed
- `scene-query` accepts `name_contains` as an alias for the documented
  name filter.
- `scene-diff` no longer reports transform changes when both before and
  after transform data are absent.
- Large scene evidence workflows no longer dead-end on
  `RESPONSE_TOO_LARGE`; agents can use compact snapshots, pagination,
  or both.

### Packaging
- `CHANGELOG.md` added (referenced by `package.json` but missing in earlier
  builds).
- `.github/workflows/release.yml` - tag-driven build + publish of a
  prebuilt `reify-server` binary.
- `.github/ISSUE_TEMPLATE/` - bug + feature request templates.
- `CONTRIBUTING.md` with the tool-addition contract (ADR-001 evidence
  discipline, scratch build flow, validation requirements).

## [0.2.1] - 2026-04-23

### Changed
- `package.json` minimum Unity bumped `2021.3` to `6000.0`. The declared
  minimum now matches what we actually validate against (Unity 6000.4.3f1).
  Earlier Unity versions may still work but are not tested and would be
  a dishonest compatibility claim per reify's own "no unverified
  assertions" stance.

### Notes
- No tool surface changes. This release exists purely to correct the
  Unity compatibility declaration in the UPM manifest so OpenUPM and
  Unity Package Manager both report accurate requirements.

## [0.2.0] - 2026-04-22

### Added
- Project-pipeline batch: `asmdef-list/inspect/update-or-create/delete`,
  `project-tag-add/remove`, `project-layer-set`, `tests-*`
  (`list/run/status/results/cancel` async job pattern).
- MCP resources (`reify://about`, `reify://philosophy/structured-state`,
  `reify://tools/catalog`, `reify://tools/{name}`) + prompts
  (`reify-structured-diagnosis`, `reify-safe-change-loop`,
  `reify-capability-escalation`).
- `batch-execute` for multi-call single round-trips.
- `reflection-method-find` + `reflection-method-call` (opt-in via
  `REIFY_ALLOW_REFLECTION_CALL=1`) for escape-hatch discovery and
  invocation.
- `reify-tool-list`, `reify-version` meta tools.
- Root `AGENTS.md` contract + per-client playbooks under
  `docs/AGENT_PLAYBOOKS.md`.
- Client configs for Cursor, Windsurf, VS Code MCP in `client-config/`.

### Changed
- Flattened MCP wrappers in `meta`, `project pipeline`, `final batch`,
  `import settings` so agents pass named params instead of a nested
  `args` envelope. Older wrappers still use the nested shape.
- Bridge adds `REIFY_MAX_RESPONSE_BYTES` cap with a structured
  `RESPONSE_TOO_LARGE` error instead of breaking stdio transport.
- `MainThreadDispatcher` gains a `RunAsync(Func<Task<T>>)` overload for
  async main-thread work.
- `project-quality-settings` wraps its per-level probe in try/finally so
  the editor always restores the prior quality level on failure.

### Fixed
- `batch-execute` / `reify-tool-list` / `reify-version` /
  `reflection-method-find` were reading `Time.frameCount` from the HTTP
  worker thread; now routed through `MainThreadDispatcher`.

## [0.1.0] - 2026-04-21

Initial feature batch leading up to the 150-tool milestone.

### Added
- Core domains: scene, gameobject, component, asset, prefab, play-mode,
  console-log, editor ops, project info, packages, scripts, physics,
  animator, audio, navigation, UI, camera + light, particles, tilemap,
  constraint + LOD, terrain, import settings, builds, scriptable objects,
  animation clips, input system.
- 11 philosophy tools: mesh-native-bounds, material-inspect, scene-query,
  project-render-pipeline-state, animator-state, render-queue-audit,
  asset-dependents, lighting-diagnostic, domain-reload-status,
  persistence-status, structured-screenshot.
- Identity hardening: scene-qualified paths (`SceneName::Path`), path
  ambiguity rejection, duplicate-component rejection.
- Bridge auto-registration: `[ReifyTool("name")]` attribute scan at
  static cctor time.
