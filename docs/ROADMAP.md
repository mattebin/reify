# Roadmap

Four phases, rough ordering. Scope estimates are hand-wavy — assume they're
40-60% low unless you've just shipped a similar thing.

---

## Phase B/C Coverage Targets (Unity Subsystem Checklist)

Reference derived from AnkleBreaker-Studio/unity-mcp-server's category list
(study only, no code copied). These are Unity subsystems a comprehensive MCP
plugin should eventually cover. reify will implement each from scratch
against Unity's API in its own structured-state-first philosophy.

### Core (Phase B)
- [x] Ping / scene-list (Phase A)
- [ ] Scene management (open, save, create, hierarchy tree with pagination)
- [x] GameObjects (create, delete, duplicate, reparent, activate, transform) *(create/find/destroy/modify shipped 2026-04-21; duplicate deferred)*
- [x] Components (add, remove, get/set serialized properties, wire references) *(add/get/modify/remove/set-property shipped 2026-04-21)*
- [x] Assets (list, import, delete, search, prefabs, materials) *(find/create/delete/get/rename/move shipped 2026-04-21; prefab tooling in its own batch)*
- [ ] Scripts (create, read, update C#)
- [x] Play Mode control *(enter/exit/pause/resume/step/status shipped 2026-04-21)*
- [x] Editor operations (execute menu item, undo/redo, editor state) *(menu-execute/undo/redo/undo-history/selection-get/selection-set shipped 2026-04-21)*
- [x] Project info (packages, render pipeline, build settings) *(project-info/packages/build-settings/layers-tags/render-pipeline-state shipped 2026-04-21)*
- [x] Console log read/clear *(read/clear/subscribe-snapshot shipped 2026-04-21)*
- [ ] Tags & Layers
- [ ] Selection
- [x] Prefab mode (open, close, overrides, apply/revert) *(create/instantiate/open/close/get-overrides/apply-overrides/revert-overrides shipped 2026-04-21)*

### Advanced (Phase C — Philosophy Tools + Expanded Coverage)
- [~] Animation (clips, controllers, parameters, play) — AND philosophy tool: animator-state introspection *(animator-state shipped 2026-04-21; direct clip/controller CRUD deferred)*
- [x] Physics (raycasts, sphere/box casts, overlap, settings) *(raycast/raycast-all/spherecast/overlap-sphere/overlap-box/settings shipped 2026-04-21)*
- [x] Lighting (lights, environment, skybox, lightmap baking, probes) — AND philosophy tool: urp-pipeline-state diagnostic *(project-render-pipeline-state shipped 2026-04-21; direct lighting CRUD deferred)*
- [ ] Audio (AudioSources, AudioListeners, AudioMixers)
- [ ] Terrain (create, modify, paint, layers, trees, details)
- [ ] Navigation (NavMesh baking, agents, obstacles, off-mesh links)
- [ ] Particles (creation, inspection, module editing)
- [ ] UI (Canvas, UI elements, layout groups, event system)
- [ ] Input Actions (Input System package)
- [ ] Assembly Definitions (.asmdef files)
- [ ] ScriptableObjects
- [ ] Constraints (position, rotation, scale, aim, parent)
- [ ] LOD Groups
- [ ] Profiler (session control, stats, deep profiles)
- [ ] Frame Debugger
- [ ] Memory Profiler
- [ ] Shader Graph / Sub Graphs
- [ ] VFX Graph
- [ ] MPPM Multiplayer Playmode
- [ ] Multi-Instance Unity Editor discovery
- [ ] Builds (Windows, macOS, Linux, Android, iOS, WebGL)

### reify-specific Philosophy Tools (Phase C — the differentiator)
- [x] mesh-native-bounds — report mesh native dimensions BEFORE placement to eliminate scale-guessing *(shipped 2026-04-21, Phase B)*
- [x] material-inspect — distinguish asset-backed materials vs MaterialPropertyBlocks, report override source *(shipped 2026-04-21, second Phase C philosophy tool)*
- [x] urp-pipeline-state — inspect URP asset config, diagnose why skybox/features not rendering *(shipped as project-render-pipeline-state 2026-04-21)*
- [x] render-queue-audit — report render queue, sorting layer, depth conflicts across scene *(shipped 2026-04-21)*
- [x] animator-state/graph — current state, parameters, transitions, blend tree values as JSON *(animator-state shipped 2026-04-21; blend-tree-specific detail deferred)*
- [x] scene-query — grep-like structured query over scene hierarchy and component properties *(shipped 2026-04-21 with scene-hierarchy + scene-stats)*
- [x] lighting-diagnostic — report baked vs realtime, light probe coverage, skybox config, ambient state *(shipped 2026-04-21)*
- [x] asset-dependents — what references this asset, in what scenes, what components *(shipped 2026-04-21; component-level ref locations deferred)*
- [x] domain-reload-status — is Unity mid-compile, is domain reload in progress, ready-to-operate flag *(shipped 2026-04-21, 9th philosophy tool)*
- [x] persistence-status — dirty scenes + assets, any_dirty gate flag *(shipped 2026-04-21, 10th philosophy tool — all tractable gaps closed)*
- [ ] structured-screenshot — only when LLM truly needs vision: returns screenshot + accompanying scene-state JSON for same frame

---

## Phase A — Foundations

Goal: end Phase A with a plugin skeleton that compiles, installs into a
Unity project, and runs a single trivial MCP tool (`ping`, `scene-list`) end
to end. No domain tools yet. This is about picking an architecture and
committing to it.

1. **Pick a name** (see [`NAMING.md`](NAMING.md)). Update `README.md`, the
   package folder name, and the namespace.
2. **Decide server language.** C# (match Murzak) vs Python (match Coplay)
   vs split. The structured-state philosophy doesn't depend on this;
   operational considerations do. Recommendation: C# for a single-toolchain
   build, stable domain-reload handling, and a single unit-test story.
3. **Decide scope: editor-only or editor + runtime.** Murzak's runtime
   support is genuinely useful, but doubles the API surface. Recommendation:
   editor-only for now, runtime in Phase D.
4. **Pick the transport.** Streamable HTTP + stdio both. Websocket optional.
5. **Lay out the plugin.** `Packages/<name>.unity.mcp/` with `Editor/` and
   (later) `Runtime/`.
6. **Lay out the server.** `Server/` as a C# / .NET project (or Python
   package if you flip the decision).
7. **Pick a tool attribute convention.** Likely `[McpTool("kebab-id",
   Description = "...")]` on methods inside `public static partial class
   Tool_<Domain>`, matching Murzak's file-per-action split.
8. **Ship `ping` and `scene-list-opened`.** Prove the end-to-end path.
9. **Ship the Claude Code configurator.** Enough to `/init` from a client.
   Other clients deferred.
10. **Decide on the attribution header template** for ported files and write
    one. (Header draft is already in [`NOTICE`](../NOTICE).)

Exit condition: Unity installs the package, runs the MCP server, Claude Code
connects, one tool responds with structured JSON.

---

## Phase B — Core domain tools (ported from upstream)

Goal: port the *widely useful* domain tools from Coplay and Murzak onto the
new scaffold, using Murzak's partial-class per-action layout. Every tool
ships with structured-state return shapes — no bundled multi-action tools.

**Porting order (rough — by impact for everyday Unity work):**

1. **Scene + GameObject** — `scene-open`, `scene-save`, `scene-create`,
   `scene-get-data`, `gameobject-create`, `gameobject-find`, `gameobject-modify`,
   `gameobject-destroy`, `gameobject-duplicate`, `gameobject-set-parent`.
   Base source: Murzak (already split), cherry-pick Coplay's resolver helpers.
2. **Component** — `component-add`, `component-destroy`, `component-get`,
   `component-modify`, `component-list-all`. Base source: Murzak.
3. **Asset + prefab** — `assets-find`, `assets-get-data`, `assets-copy`,
   `assets-move`, `assets-delete`, `assets-refresh`, `prefab-create`,
   `prefab-open`, `prefab-instantiate`, `prefab-save`, `prefab-close`. Base
   source: Murzak; take Coplay's sanitization utilities (`AssetPathUtility`).
4. **Script** — `script-read`, `script-update-or-create`, `script-delete`.
   Base source: Murzak. Gate `script-execute` behind an explicit opt-in
   config flag — powerful but a security-auditing headache.
5. **Material + shader** — **redesign this domain, don't port it.** This
   is where structured-state differentiation starts. Write
   `material-inspect` (new, see Gap 3 in ARCHITECTURE_ANALYSIS),
   `shader-get-data`, `shader-list-all`. Keep Coplay's `set_material_color`
   and friends as writers; verify after write.
6. **Editor + selection + console** — `editor-application-get-state`,
   `editor-application-set-state`, `editor-selection-get/set`,
   `console-get-logs`, `console-clear-logs`. Base source: Murzak.
7. **Animator** — port Coplay's `AnimatorRead.cs` almost wholesale; add the
   transitions + graph dump from Gap 6.
8. **Packages** — `package-add/list/remove/search`. Base source: Murzak.
9. **Refresh + menu items + batch** — `assets-refresh` (Murzak),
   `menu-execute` (Coplay's `ExecuteMenuItem`), `batch-execute`
   (Coplay's `BatchExecute`).
10. **Tests** — port Coplay's `RunTests.cs` + `GetTestJob.cs` (async job +
    poll pattern worth keeping).

Exit condition: 25-30 core tools working. Any reasonable "set up a scene,
add components, wire up scripts" workflow completable without
`script-execute` or screenshots.

---

## Phase C — Philosophy features (the differentiator)

Goal: ship the tools that neither upstream has, grouped around the
structured-state thesis. These are why the fork exists.

1. **`mesh-native-bounds`** (Gap 1) — prefab/mesh-asset bounds in meters,
   before placement. Flat JSON `{center, size_meters, min, max}`.
2. **`prefab-placement-preview`** — for a prefab + target transform,
   return the resulting world-space bounds. Read-only.
3. **`material-inspect`** (Gap 3) — full property table with override
   source tracking (asset | instance | MPB) and shader keyword state.
4. **`urp-pipeline-state`** (Gap 2) — structured summary of the active URP
   asset. `urp-volume-stack-at` for position-based volume introspection.
5. **`render-queue-audit`** (Gap 5) — scene-wide report bucketed by queue /
   sorting layer / sorting order with tie flags.
6. **`animator-state`** + **`animator-graph`** (Gap 6 extensions).
7. **`scene-query`** (Gap 7) — structured selector returning
   GameObject paths + field values.
8. **`lighting-diagnostic`** (Gap 8) — per-object lightmap state.
9. **`asset-dependents`** + **`asset-dependencies`** (Gap 9).
10. **`persistence-status`** (Gap 10) — edit vs play mode + override info.
11. **`domain-reload-status`** + request queuing (Gap 11 — architectural).

Each of these ships with documentation in the same file as its
implementation, including a "when to use this instead of a screenshot"
example.

Exit condition: the philosophy pitch in `PHILOSOPHY.md` is demonstrable —
you can point at 10 things neither upstream does and show that this plugin
does them.

---

## Phase D — Polish

1. **Runtime-mode tools.** Port a minimal read surface into a Runtime
   assembly (no editor-only APIs). Goal: LLM can interrogate state in a
   compiled build.
2. **Reflection escape hatch.** Port Murzak's `reflection-method-find/call`,
   but opt-in, with a clear auditing story.
3. **Client configurators beyond Claude Code.** Cursor, VS Code Copilot,
   Windsurf. Steal Coplay's per-client files.
4. **Docs site.** Static-gen docs from tool descriptions. The tool
   metadata should be authoritative.
5. **Tests.** Coplay has a pattern worth stealing: Python tests for the
   server + Unity test framework integration tests for the plugin. Pick
   equivalents if the server is C#.
6. **Packaging.** OpenUPM path, `.unitypackage` installer, Docker image
   for the server. Follow the path Coplay/Murzak have already trodden.
7. **Telemetry (opt-in).** Only if useful for finding regressions.
   Not for marketing.
8. **Prompts as first-class MCP resources.** Murzak's `API/Prompt/*`
   pattern — pre-made prompt templates for common Unity debugging flows
   that embed the structured-state tools.

Exit condition: the project is something another person could install and
use without direct support.

---

## Non-goals (explicit)

- No generic "do everything with MCP" ambitions. The surface is Unity-shaped.
- No human-first visual tooling (that's the Unity Editor's job).
- No screenshot-centric workflows as the *default* path. Screenshots stay
  available as an opt-in escape hatch.
- No attempt to stay source-compatible with either upstream's tool names
  or response shapes. We're opinionated — names and shapes are ours.

---

## Open questions (answer when you get back)

- C# server or Python server? (See Phase A step 2.)
- Runtime support in Phase A/B or Phase D?
- Is the scope "my personal Unity projects" or "public eventual OSS release"?
  The polish bar is very different.
- Which MCP clients matter for you personally? Claude Code + Cursor likely;
  anything else?
- Is `script-execute` on-by-default or off-by-default?
- Do you want upstream-commit-linked commits (where every ported file's
  commit message includes the upstream SHA), or periodic rebases of their
  code into `/src/`?
