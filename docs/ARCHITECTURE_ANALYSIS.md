# Architecture Analysis: CoplayDev/unity-mcp + IvanMurzak/Unity-MCP

Side-by-side analysis of the two upstream projects this fork will pull from,
with a view toward the structured-state-first philosophy in
[`PHILOSOPHY.md`](PHILOSOPHY.md).

Reference commits (fetched with `--depth=1`):

- `coplay/main` — CoplayDev/unity-mcp, latest tag `v9.6.6`
- `murzak/main` — IvanMurzak/Unity-MCP, package version `0.66.0`

Both remotes are configured locally. Refresh with
[`scripts/sync-upstream.sh`](../scripts/sync-upstream.sh).

---

## 1. CoplayDev/unity-mcp

### Repo structure (top-level)

```
.claude/                    Claude-specific skill + prompt bundles
.github/                    CI (Python + Unity test workflows)
CLAUDE.md                   Per-repo Claude Code guidance
CustomTools/                Optional Roslyn runtime-compile tools
MCPForUnity/                THE Unity package (Editor + Runtime C# code)
  └─ Editor/
       Clients/             Per-client config writers (Claude, Cursor, VSCode, etc.)
       Dependencies/        DependencyManager — installs Roslyn, etc.
       Helpers/             PortManager, ObjectResolver, MaterialOps, etc.
       Resources/           Active tool state + editor UI assets
       Services/            ServerManagementService, ToolDiscovery, Transport
       Tools/               THE tool implementations (C# static classes)
Server/                     Python MCP server
  └─ src/
       cli/commands/        CLI front-end (one file per tool group)
       core/                Config + telemetry
       services/            tool registration, custom-tool service, resources
       transport/           WebSocket + legacy stdio bridges
TestProjects/               Unity integration test project
docs/                       User-facing documentation
manifest.json               Unity package entry
mcp_source.py / tools/      Dev tooling
scripts/                    Shell scripts (release / test)
unity-mcp-skill/            Standalone skill directory
```

**Layout style:** two cleanly separated halves — a Unity package
(`MCPForUnity/`) for the editor-side and a Python server (`Server/`) for the
MCP side. They talk via a WebSocket (primary) or stdio (fallback). A `TransportManager`
inside Unity picks one. Tool registration lives in Unity; the Python side is a
thin dispatcher that exposes Unity tools over MCP.

### Tools

Count: **98 C# files** under `MCPForUnity/Editor/Tools/` (excluding `.meta`).
Concrete tool groups:

| Category         | Files (notable)                                                                    |
|------------------|-------------------------------------------------------------------------------------|
| Scene            | `ManageScene.cs`                                                                    |
| GameObject       | `ManageGameObject.cs`, `FindGameObjects.cs`, `GameObjects/*`                        |
| Component        | `ManageComponents.cs`, `GameObjects/ComponentResolver.cs`                           |
| Asset            | `ManageAsset.cs`, `ManageScriptableObject.cs`                                       |
| Script           | `ManageScript.cs`                                                                   |
| Material/Shader  | `ManageMaterial.cs`, `ManageShader.cs`, `ManageTexture.cs`                          |
| Graphics/URP     | `Graphics/*` (volumes, post-processing, renderer features, rendering stats)         |
| Physics          | `Physics/*` (3D + 2D joints, queries, materials, force application, 21 actions)     |
| Animation        | `Animation/AnimatorRead.cs`, `AnimatorControl.cs`, `ClipCreate.cs`, controllers      |
| Camera/Cinemachine | `Cameras/CameraConfigure.cs`, `CameraControl.cs`, `ManageCamera.cs`                |
| UI               | `ManageUI.cs`                                                                       |
| Build            | `Build/BuildRunner.cs`, `BuildJob.cs`, `BuildSettingsHelper.cs`                     |
| Packages         | `ManagePackages.cs`                                                                 |
| Editor/Console   | `ManageEditor.cs`, `ReadConsole.cs`, `ExecuteMenuItem.cs`, `RefreshUnity.cs`         |
| Profiler         | `Profiler/*` (memory profiler, frame debugger, 14 actions)                          |
| ProBuilder       | `ProBuilder/ManageProBuilder.cs`, `ProBuilderMeshUtils.cs`, `ProBuilderSmoothing.cs` |
| Code exec        | `ExecuteCode.cs` (Roslyn), `CustomTools/RoslynRuntimeCompilation/*`                 |
| VFX              | `Vfx/*`                                                                             |
| Reflection       | `UnityReflect.cs` (API surface introspection)                                       |
| Batching         | `BatchExecute.cs`                                                                   |

**Tool design convention:** each tool is a `static class` exposing a
`HandleCommand(JObject @params)` that switches on `action`. Tools are
registered via the `[McpForUnityTool(name)]` attribute and discovered by
`ToolDiscoveryService` at editor-start. Responses are POCOs returned as JSON.

**Python CLI mirror:** every tool has a matching Python file in
`Server/src/cli/commands/` (animation, asset, audio, batch, build, camera, code,
component, docs, editor, gameobject, graphics, instance, lighting, material,
packages, physics, prefab, probuilder, profiler, reflect, scene, script, shader,
texture, tool, ui, vfx). The CLI layer wraps the MCP tool call and also exists
as a standalone command runner.

### Transport layer

- **Primary:** WebSocket (`WebSocketTransportClient` inside Unity, matching
  `starlette` server in Python). Used for HTTP-like streaming / client ↔ Unity
  routing.
- **Fallback:** stdio (`StdioTransportClient` ↔ `transport/legacy/`).
- Coordinator: `MCPForUnity/Editor/Services/Transport/TransportManager.cs` —
  holds both clients as lazily instantiated factories and switches `TransportMode`.
- Multi-instance-safe: `Server/src/transport/unity_instance_middleware.py` routes
  requests to specific Unity instances (addresses the "two editors open" issue).
- Port discovery is handled by `PortManager` on the Unity side and a registry
  on the Python side.

### Config file locations

- **Unity side:** settings live under `ProjectSettings/` via the Package
  Manager; per-user preferences via `EditorPrefs`.
- **MCP client side:** `MCPForUnity/Editor/Clients/Configurators/*` writes the
  client-specific config file — Claude Desktop (`claude_desktop_config.json`),
  Cursor (`~/.cursor/mcp.json`), VSCode, Cline, Windsurf, Antigravity, etc.
  There's a `ClientRegistry.cs` that lists them and a `Configurators/`
  directory with one file per supported client (observed files: Antigravity,
  CherryStudio, ClaudeCode, ClaudeDesktop, Cline, CodeBuddyCli, Codex,
  CopilotCli, Cursor, + more).
- **Server side:** `Server/src/core/config.py` reads a YAML / env, exposes as
  `config.*`.

### Notable architectural choices

1. **Heavy tool-per-domain split.** Each tool (`manage_material`, `manage_physics`,
   etc.) is a single file owning a hefty action-switch. Easy to grep, hard to
   share helpers across tools — so helpers have been factored into
   `Helpers/` (e.g. `ObjectResolver`, `MaterialOps`, `AssetPathUtility`).
2. **Client fan-out.** Unusual amount of investment in supporting every MCP
   client. `Configurators/*` handles each one's config-file quirks.
3. **Reflection as a tool (`UnityReflect`).** Exposes Unity API introspection
   — inspect live C# APIs, fetch Unity docs — but only as a query layer.
   Not used to dispatch method calls generically.
4. **Tool state persistence.** `ActiveTool.cs` + `ToolStates.cs` track which
   tools are enabled/disabled, surviving domain reloads.
5. **Telemetry.** `Server/src/core/telemetry.py` records milestones + usage.
6. **Roslyn runtime compilation** lives in `CustomTools/` (separate from
   `MCPForUnity/Editor/Tools/`). `ExecuteCode.cs` provides a `compile + run
   one-shot` loop.
7. **Python-first server.** Tool definitions live in C#, but the MCP contract
   is served by Python. This is unusual — most Unity+MCP efforts pick one
   language. It lets the Python side hide Unity reload/restart cycles from the
   MCP client.

### Pain points from issue tracker

Open issues (by comment count, April 2026):

- **#1023 — "MCP affects other projects when working in two or more editors."**
  Cross-project state pollution when running multiple editor instances. The
  `unity_instance_middleware` exists but the isolation isn't airtight.
- **#891 — "MCP stuck if Unity pops a reload window."** Blocking domain
  reloads freeze the server until manually dismissed.
- **#837 — "Custom tools not discovered in stdio transport mode."** Tool
  discovery is transport-conditional.
- **#1029 — "MCP screenshot always returns null."** Screenshot functionality
  is the canonical example of a fragile path (reinforces our philosophy).
- **#828 — "Feature Request: Custom prompts."** No user-defined prompt layer.
- **#817 — "VS 2026 MCP server manager install button disabled."** Client
  fan-out has maintenance cost.

Closed but illuminating:

- **#302 — "Large MCP response (~13.3k tokens)."** Tools return too much text
  by default. Structured state helps here *only if we keep payloads tight and
  let the LLM ask for depth*.
- **#136 — "Invalid JSON format error with large C# scripts."** String
  encoding of C# source crossed the JSON limit. Deserves a streaming or
  chunked-file protocol, not a bigger buffer.
- **#465 — "Screenshot action putting image in wrong folder."** Another
  screenshot fragility datapoint.
- **#307 — "Add support for Unity test execution"** — since shipped.

### Preserve vs replace (CoplayDev)

| Preserve                                        | Replace                                                 |
|-------------------------------------------------|----------------------------------------------------------|
| Tool-per-domain layout (easy to grok)           | Screenshot-first workflows — demote to escape hatch     |
| `ObjectResolver` + helper classes               | The dual-server (Python+C#) model — pick one, probably C# |
| Client configurators (at least Claude Code/Cursor) | Large action-switch tools — split read/write per tool  |
| Transport abstraction (`TransportManager`)      | Default response payload size — return paths, ask for depth |
| `ManageCamera` + Cinemachine integration        | Domain-reload blocking — make the server reload-resilient |
| `Profiler/*` — genuinely useful diagnostics     | Tool name prefix `manage_` — too verb-y for read-heavy tools |
| Physics depth (raycast/overlap/forces)          |                                                          |
| ProBuilder tooling                              |                                                          |

---

## 2. IvanMurzak/Unity-MCP

### Repo structure (top-level)

```
.claude/ .specify/ .vscode/      Tooling
CLAUDE.md                         Claude guidance
Installer/                        .unitypackage installer project
Unity-MCP-Plugin/
  └─ Assets/Plugins/NuGet/        Bundled NuGet dependencies
  └─ Packages/com.ivanmurzak.unity.mcp/
       Editor/                    Editor-side plugin
         DependencyResolver/      NuGet installer/restore inside Unity
         Scripts/
           API/
             Prompt/              Prompt templates exposed as MCP
             Resource/            Resource providers (GameObject.Hierarchy)
             SystemTool/          Skills-related tools (Ping, Skills.*)
             Tool/                THE tool implementations (partial classes)
       Runtime/                   Runtime-side (works inside compiled game)
         Converter/Json/          Vector/Bounds/Color JSON converters
Unity-MCP-Server/                 C# / .NET MCP server (not Python)
  src/Program.cs                  ASP.NET Kestrel host, stdio + streamable-HTTP
Unity-Tests/                      Integration test project
cli/                              Node CLI (unity-mcp-cli)
commands/                         PowerShell + shell scripts
docs/                             Public docs (including default-mcp-tools.md)
specs/                            Spec-driven design artifacts
```

**Layout style:** package + Unity-Tests + server, plus a Node CLI for
cross-platform install. Server is C#/.NET — unusual for the MCP ecosystem.
Plugin is a true Unity package under `Packages/com.ivanmurzak.unity.mcp/`.

### Tools

Count: **86 C# tool files** under `API/Tool/` (86 non-meta, non-pre-Unity-6.5
variants). Naming pattern: `Domain.Action.cs`, one action per file. These are
aggregated via `public static partial class Tool_Domain` so `Assets.cs`,
`Assets.Copy.cs`, `Assets.Move.cs`, etc. all contribute to the same class.

Concrete tool groups (from `API/Tool/`):

| Category           | Representative files                                                          |
|--------------------|--------------------------------------------------------------------------------|
| Assets             | `Assets.Copy`, `Assets.Move`, `Assets.GetData`, `Assets.Modify`, `Assets.Prefab.*`, `Assets.Material.*`, `Assets.Shader.*` |
| Console            | `Console.GetLogs`, `Console.ClearLogs`                                         |
| Editor             | `Editor.Application.GetState/SetState`, `Editor.Selection.*`                   |
| GameObject         | `GameObject.Create/Destroy/Duplicate/Find/Modify/SetParent`, `GameObject.Component.*` |
| Object             | `Object.GetData`, `Object.Modify`                                              |
| Package            | `Package.Add/List/Remove/Search`                                               |
| **Reflection**     | `Reflection.MethodCall`, `Reflection.MethodFind` — generic C# method access   |
| Scene              | `Scene.Create/Open/Save/SetActive/GetData/ListOpened/Unload`                   |
| Screenshot         | `Screenshot.Camera`, `Screenshot.GameView`, `Screenshot.SceneView`             |
| Script             | `Script.Read/UpdateOrCreate/Delete/Execute` — `Execute` uses Roslyn            |
| Tests              | `Tests.Run`                                                                    |
| Type               | Type-registry introspection                                                    |

**Tool design convention:** each tool is a `[McpPluginTool("kebab-id",
Title = "...")]` on a method (not class). The method signature *is* the MCP
tool schema — parameters have `[Description]`, return types are serialized by
ReflectorNet. Uses `.NET` reflection heavily.

**Prompts as tools:** `API/Prompt/*` exposes pre-made prompts
(`AnimationTimeline`, `DebuggingTesting`, `GameObjectComponent`,
`SceneManagement`, `ScriptingCode`) — an unusual first-class "prompt"
primitive that CoplayDev lacks.

**Runtime support:** `Runtime/` exists as a separate assembly. The plugin
works in compiled builds, not just the editor — a stated goal of this project
("enable AI within your games" for NPC behavior / live debugging).

### Transport layer

- Server is a **C#/.NET ASP.NET Core** host using Kestrel + NLog.
- Supports **stdio** and **streamable HTTP** (not WebSocket first like CoplayDev).
- IPv4/IPv6 bound separately (per the in-code comment — macOS dual-stack fix).
- In-project communication uses a custom `McpPlugin` package by the same
  author, distributed as NuGet.

### Config file locations

- Plugin: `Packages/com.ivanmurzak.unity.mcp/` — Unity UPM installable.
- Server: shipped as Docker image + local binary. Config via
  `appsettings.json` + CLI args.
- Client config: handled by the Node CLI (`unity-mcp-cli install-plugin`,
  `setup-skills`) rather than the plugin's editor UI. `specs/` contains a
  "Configure MCP" flow as a spec doc.

### Notable architectural choices

1. **Reflection-first.** `Tool_Reflection` (`reflection-method-call`,
   `reflection-method-find`) lets the LLM call any C# method by name — a
   generic escape hatch that shrinks the tool surface needed to cover new
   domains. Enabled=false by default (opt-in).
2. **Partial-class tools.** Each action lives in its own file; the compiler
   stitches them into one class. Easier diffs, fewer merge conflicts.
3. **ReflectorNet.** Custom serialization layer that handles Unity's awkward
   types (UnityEngine.Object refs, GUIDs, Component references). Makes
   `Object.GetData` possible as a generic tool.
4. **Runtime + Editor split.** Plugin is two assemblies — Editor for design-time,
   Runtime for in-game. This is the biggest architectural divergence from
   CoplayDev (which is Editor-only).
5. **Prompts as a first-class MCP primitive.** Not just tools: the plugin also
   exposes prompt templates the LLM can draw on.
6. **Skills auto-generation.** Ships a tool to generate Unity-specific skills
   for Claude Code, Cursor, etc.
7. **CLI-driven install.** `unity-mcp-cli` handles plugin install and client
   config, lowering the "open Unity to configure MCP" friction.
8. **Opinionated naming.** `assets-copy`, `gameobject-create` — flat
   kebab-case IDs, verb-after-noun. Consistent across the whole surface.

### Pain points from issue tracker

- **#297 — "Has an output schema but did not return structured content."**
  The ReflectorNet → MCP schema mapping has edge cases. Big deal for our
  structured-state thesis.
- **#666 — "VS Code Github Copilot - MCP Error."** Integration friction.
- **#334 — "Assets_Modify does not persist changes — missing
  EditorUtility.SetDirty."** Edits returned success but Unity didn't persist
  them — classic "wrote to memory, not to disk."
- **#340 — "GameObject_Find crashes Unity with infinite recursion on
  complex GameObjects."** Graph traversal lacks depth guards.
- **#169 — "Reflector.Instance.Serialize does not serialize nested
  serializable classes."** Reflection pipeline has blind spots.
- **#370 — "Allow each agent to launch an independent server with a unique
  port."** Same multi-instance pain as CoplayDev #1023.
- **#185 — "Feature Request: Network-based (TCP/WebSocket) options for
  server."** Shipped since. Originally stdio-only.

### Preserve vs replace (IvanMurzak)

| Preserve                                         | Replace                                                  |
|--------------------------------------------------|-----------------------------------------------------------|
| `Reflection.MethodFind` / `Reflection.MethodCall` — generic escape hatch | `Script.Execute` as a default-on tool — gate it behind explicit opt-in |
| Partial-class per-action layout (easier reviews) | Runtime MCP surface — we're editor-first, at least at start |
| `Object.GetData` / `Object.Modify` generic tools  | Node CLI as the sole install path — keep Unity-UI install too |
| ReflectorNet serialization (once bugs are fixed)  | Prompts as MCP resources — useful, but not a day-one feature |
| Kebab-case flat tool IDs                          | Skills auto-generation — keep, but not first priority     |
| Partial-file per Unity version (`*.pre-Unity.6.5.cs`) convention | Docker-first server delivery — add a single-binary path |
| C# server (one language top-to-bottom)            |                                                           |
| Prompts-as-first-class (we'll use differently)    |                                                           |

---

## 3. Overlap matrix (tool-level, not exhaustive)

Tools present in **both** plugins (semantic overlap — names differ):

| Capability                  | Coplay                          | Murzak                              | Cleaner? | Notes |
|-----------------------------|---------------------------------|-------------------------------------|----------|-------|
| Scene CRUD                  | `manage_scene` (multi-action)   | `scene-create/open/save/...`        | Murzak   | One-verb-per-tool easier to reason about |
| GameObject CRUD             | `manage_gameobject` + `find_gameobjects` | `gameobject-create/destroy/find/modify/duplicate/set-parent` | Murzak | Per-action split matches structured-state style |
| Component add/remove/modify | `manage_components`             | `gameobject-component-add/destroy/modify/get/list-all` | Murzak | Murzak exposes `list-all` (structured discovery) |
| Asset CRUD                  | `manage_asset`, `manage_scriptableobject` | `assets-copy/move/delete/find/...` | Murzak   | Coplay bundles too many actions behind one name |
| Material                    | `manage_material` (create/set/assign/info) | `assets-material-create`, `assets-material`, `assets-modify` on a material | Coplay | Coplay has a dedicated, richer surface |
| Shader                      | `manage_shader`                 | `assets-shader-get-data`, `assets-shader-list-all` | Murzak   | Get-data is structured-state-friendly |
| Script CRUD                 | `manage_script`                 | `script-read/update-or-create/delete/execute` | Murzak | Split is clearer |
| Script execute (Roslyn)     | `ExecuteCode.cs` + `CustomTools/RoslynRuntimeCompilation/` | `script-execute` | Murzak | Tighter API, explicit static-method contract |
| Editor state                | `manage_editor`                 | `editor-application-get-state/set-state`, `editor-selection-*` | Murzak | Much more readable over time |
| Console logs                | `read_console`                  | `console-get-logs`, `console-clear-logs` | Murzak | Split read/clear |
| Selection                   | inside `manage_editor`          | `editor-selection-get/set`           | Murzak   | First-class |
| Screenshot                  | multi-view screenshot tool       | `screenshot-camera/game-view/scene-view` | Murzak | Clearer; though per philosophy, demote both |
| Packages                    | `manage_packages`               | `package-add/list/remove/search`     | Murzak   | Per-action |
| Tests                       | `RunTests.cs` + `GetTestJob.cs` | `tests-run`                          | Coplay   | Async job + polling is worth keeping |
| Batch exec                  | `BatchExecute.cs`               | —                                    | Coplay   | Not present upstream on Murzak side |
| Menu items                  | `ExecuteMenuItem.cs`            | —                                    | Coplay   | Useful escape hatch |
| Refresh asset DB            | `RefreshUnity.cs`               | `assets-refresh`                     | Tie      | Same thing, different framing |

Tools **only** in CoplayDev:

- Camera / Cinemachine (`manage_camera` + presets)
- Physics suite (21 actions, 3D + 2D, raycast/overlap/forces)
- Graphics / URP (`manage_graphics` 33 actions — volumes, PP, renderer features)
- Animation suite (animator read/control, clip create, controller layers, blend trees)
- UI (`manage_ui`)
- Build (`manage_build` + player settings + batch builds)
- Profiler (`manage_profiler` 14 actions — frame timing, memory snapshots, frame debugger)
- ProBuilder (`manage_probuilder` — mesh editing)
- VFX
- `UnityReflect` (docs + API query, read-only)
- Texture (`manage_texture`)
- Batch execute

Tools **only** in IvanMurzak:

- `Reflection.MethodCall` / `Reflection.MethodFind` (generic C# method dispatch)
- `Object.GetData` / `Object.Modify` (generic UnityEngine.Object CRUD via reflection)
- Runtime-mode tools (in-game calls)
- Prompt templates (`API/Prompt/*`)
- Skills auto-generation (`API/SystemTool/Skills.*`)
- `Type.*` (C# type registry introspection)

### Summary of counts

| Metric                             | Coplay | Murzak | Notes |
|------------------------------------|--------|--------|-------|
| C# tool implementation files       | ~98    | ~86    | After excluding `.meta` and pre-Unity-6.5 variants |
| Distinct user-facing tool names    | ~30    | ~50    | Coplay bundles multi-action tools behind one name; Murzak splits per action |
| Semantic overlap (both cover)      | ~15-18 domains | ~15-18 domains | Scene/GO/Component/Asset/Script/Editor/Console/Selection/Screenshot/Packages/Tests |
| Unique to Coplay                   | Physics, Graphics, Camera, Animation, UI, Build, Profiler, ProBuilder, VFX, Texture, Batch, UnityReflect, MenuItem | — |
| Unique to Murzak                   | — | Reflection dispatch, Prompts, Runtime, Object-generic, Type registry, Skills generator |

**Rough read:** Coplay has more *vertical depth* (many domains, rich per-domain
action sets). Murzak has more *horizontal flexibility* (generic reflection,
prompt templates, runtime reach). The merge thesis: take Coplay's domain
depth, restructure it in Murzak's per-action partial-class style, add the
reflection escape hatch, and layer philosophy-specific tools on top.

---

## 4. Gap analysis — what neither does well

These are gaps relative to the structured-state philosophy. Building them is
the reason this fork exists.

### Gap 1: Mesh native bounds *before* placement

**Problem:** LLMs guess scale. A "place a cube at origin" gets you a 2-meter
cube if you're lucky and a 0.2m or 20m one if you're not. Neither plugin
exposes the native bounds of a prefab/mesh asset in meters as a first-class
read, before instantiation.

**What's missing:**
- `mesh_native_bounds(prefab_path | mesh_guid)` → `{center, size_meters, min, max}`
- `prefab_placement_preview(prefab, transform)` → where would this land, how big
  would it be after the prefab's root transform scale?
- `scene_bounds_summary()` → bounding box of every root GameObject, bucketed
  by size-of-magnitude, so the LLM knows what scale the scene is in.

**Partial coverage today:** Coplay's `manage_gameobject` lets you read the
attached Renderer's bounds *after* placement. Murzak's `Object.GetData` can
serialize mesh references but not the bounds of the referenced mesh.

### Gap 2: URP pipeline diagnostic

**Problem:** "Why isn't my skybox rendering?" Screenshot shows black. Neither
plugin has a URP diagnostic that returns structured state for the active
pipeline asset.

**What's missing:**
- `urp_pipeline_state()` → `{renderer_features: [...], post_processing: bool,
  render_scale, hdr, depth_priming, shadow_settings, msaa, upscaling}`
- `urp_volume_stack_at(position)` → for a world position, which volume
  profiles are contributing, with their weights and overrides.
- `render_feature_diagnostic(feature_name)` → what's enabled, what's the
  queue, what's the event, what assets it depends on.

**Partial coverage:** Coplay's `manage_graphics` gets close — it can read
volumes and renderer features. But it doesn't bundle the diagnostic into a
single "why" answer shape.

### Gap 3: Material property inspector with override source tracking

**Problem:** "I set the color but it didn't change." The actual render color
could come from (a) the asset material, (b) an instance material,
(c) a `MaterialPropertyBlock`. Neither plugin tells you which.

**What's missing:**
- `material_inspect(renderer)` → full property table flagging each value's
  source: asset | instance-material | MPB. Also shows shader keyword state and
  current shader variant.
- `material_diff(renderer_a, renderer_b)` → what differs, useful for
  diagnosing why "the red one looks different."

**Partial coverage:** Coplay has `get_material_info` but it reports the
material, not the rendered state. Murzak's `Object.GetData` hits the
material asset.

### Gap 4: Theme-aware scene state

**Problem:** Games with themed content (seasonal, per-level, per-faction)
swap materials/prefabs conditionally. An LLM looking at the hierarchy sees
the current theme's state, doesn't know which elements are theme-conditional,
can't reason about "what would this look like if the theme were X."

**What's missing:**
- `theme_audit()` → project-wide report of which prefabs and materials are
  theme-conditional. Requires a project convention — the tool should be
  configurable against that convention.
- `theme_snapshot(theme_name)` → the structured state that *would* apply if
  the theme were X.

This is the most speculative gap — depends on the user's project structure.
But even a generic "which assets are swapped in/out at runtime vs baked"
report would be valuable.

### Gap 5: Render queue / sorting / depth conflict diagnostic

**Problem:** Two transparent objects in the wrong order, or two UI elements
on the same sorting layer with the same order. LLMs can't diagnose this from
a screenshot.

**What's missing:**
- `render_queue_audit()` → every renderer in scene: render queue, sorting
  layer, sorting order. Flag ties and cross-layer overlaps.
- `transparency_sort_diagnostic(camera)` → from camera POV, in which order
  are transparent objects drawn, with the reasons (queue, distance, material
  setting).

### Gap 6: Animator state introspection

**Problem:** Coplay has `AnimatorRead.cs` that covers this *decently* — it
returns current state per layer, parameter table, clip table. Murzak lacks
this entirely.

**What's missing vs what's there:**
- Preserve Coplay's `AnimatorRead` — it's already close to what the
  philosophy wants.
- Add: active transitions + conditions, layer blend state, animator graph
  dump (states + transitions + parameters) as one JSON.
- Add: `animator_watch(gameobject, duration_frames)` → record the state
  timeline, return as structured data.

### Gap 7: Script-identifier-first scene navigation

**Problem:** "Find the GameObject with script FooController where field `bar
> 3`." Both plugins support find-by-name and find-by-tag. Neither supports
structured queries against scripts-attached-to-objects.

**What's missing:**
- `scene_query(selector)` → a structured selector: by component type, by
  component field value, by tag, by layer, by name glob. Returns GameObject
  paths + relevant field values.
- `scene_index()` → a one-shot structured dump of "what's in this scene"
  optimised for LLM grep.

**Partial coverage:** Coplay's `FindGameObjects.cs` has multiple search
strategies. Murzak's `GameObject.Find` is simpler. Neither does
field-value-predicate search.

### Gap 8: Lightmap / baking state diagnostic

**Problem:** "Why is the shadow missing?" or "Why is this object dark?"
Screenshot confirms but doesn't diagnose. Answer is usually lightmap UV
channel, `Static` flag, GI contribution, or bake failure.

**What's missing:**
- `lighting_diagnostic(gameobject)` → is it static, is it GI-contributing,
  does its mesh have lightmap UVs, is it lightmapped, what's the lightmap
  index/scale/offset.

### Gap 9: Asset dependency graph

**Problem:** "What breaks if I delete this material?" Neither plugin exposes
the dependency graph natively — you can call `AssetDatabase.GetDependencies`
via `execute_code`, but no tool reifies this.

**What's missing:**
- `asset_dependents(path)` → what uses this.
- `asset_dependencies(path)` → what this uses.
- `asset_orphans()` → assets used by nothing.

### Gap 10: Editor-time vs play-mode state distinction

**Problem:** Play-mode modifications don't persist. Neither plugin is
explicit about whether a read is editor-time or play-time state. This
silently confuses LLMs that don't know Unity's persistence model.

**What's missing:**
- Every read/write tool should return a `mode: "edit" | "play"` field and,
  for play-mode reads, a `persistence_warning` if edits won't survive.
- A `persistence_status(gameobject)` tool that tells the LLM whether the
  instance is a scene object, a prefab instance with overrides, a
  play-mode-only instantiation, etc.

### Gap 11: Domain-reload resilience

**Problem:** Coplay's #891 and Murzak's implicit behavior: Unity's domain
reload cycle blocks MCP. The LLM sends a tool call, the server is restarting,
the LLM gets a timeout or a hung channel.

**What's missing (architectural, not a tool):**
- Server-side request queue that survives domain reload.
- A `wait_for_unity_ready(timeout)` tool the LLM can call before a batch.
- A `domain_reload_status()` read that says "reloading, 37% done, ETA ~4s."

---

## 5. Merge thesis (feeds the roadmap)

In one paragraph: **adopt Murzak's partial-class per-action layout and
reflection escape hatch, port Coplay's domain depth (animation, physics,
graphics, profiler, probuilder, camera) onto that layout, add philosophy
tools (`mesh_native_bounds`, `material_inspect`, `urp_pipeline_state`,
`render_queue_audit`, `scene_query`), unify the server on one language
(leaning C# to match the plugin, unless a strong reason to keep Python),
and keep screenshots as an opt-in escape hatch.**

Detailed phasing lives in [`ROADMAP.md`](ROADMAP.md).
