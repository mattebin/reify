# Architecture decisions — Phase A

Three decisions locked here: **server language**, **editor vs runtime scope**,
**upstream skeleton**. Each is a call I made after weighing alternatives; the
"if you want to reverse this" note at the end of each section names the cost.

---

## Decision 1: Server language — **C# / .NET 8**

### Chosen: C#

A single-language stack for both server and Unity plugin, targeting .NET 8 for
the server and whatever Unity's current scripting runtime supports for the
Editor assembly (currently .NET Standard 2.1 compatible C# 9).

### Why

1. **Single toolchain, shared types.** The MCP server and the Unity Editor
   bridge both speak JSON over HTTP on localhost. With C# on both sides, the
   request/response DTOs (`PingResponse`, `SceneInfo`, `SceneListResponse`,
   etc.) live once in a `Reify.Shared` assembly and are referenced by both
   projects. Python-server + C#-plugin means maintaining two parallel
   definitions (Pydantic models + C# records) and keeping them in lockstep —
   a known source of friction in CoplayDev's repo.
2. **Reflection is free.** The philosophy tools in Phase C (material
   inspection, shader variant state, animator graph) want deep reflection
   into Unity types. C# on the Unity side has that natively. Python only
   sees what the C# bridge chooses to serialize, so the bridge ends up
   being the harder surface anyway.
3. **MCP C# SDK is production-ready.** `ModelContextProtocol` (the
   official C# SDK co-authored by Anthropic and Microsoft) supports stdio
   and Streamable HTTP, has first-class `[McpServerTool]` attribute-based
   tool discovery, and handles all the protocol plumbing. Fewer moving parts
   than rolling a Python FastMCP server + HTTP bridge.
4. **Distribution story is simpler.** One self-contained `reify-server.exe`
   (framework-dependent, ~200 KB; self-contained, ~15 MB) ships alongside
   the Unity package. No Python interpreter to manage, no venv, no `pip`.
5. **Matches IvanMurzak/Unity-MCP's approach** — which is the upstream we're
   leaning on for structure — so porting is copy-paste-level, not
   translation.

### Costs of this choice

- The Python MCP ecosystem (tutorials, examples, community tools) is bigger.
  If we ever want to plug in community MCP servers as sub-tools we'd have
  to shell out to Python processes anyway — fine, but not free.
- CoplayDev's server is Python. Any tool we want to port directly from
  Coplay gets translated C#→C# on the Unity side but Python→C# on the
  server side. Minor tax; the Unity side is where the real logic lives.

### If you want to reverse this

The natural reversal point is "before any Phase B tools are ported." After
Phase B, ~25 tools × ~50 lines of C# server scaffolding each is a real
migration. Before Phase B, the server is 3 files and a `ping` tool — trivial.

---

## Decision 2: Editor vs Runtime scope — **Editor-only for Phase A & B**

### Chosen: Editor-only

`Reify.Editor` is the only Unity assembly in Phases A and B. `Reify.Runtime`
is deferred to Phase D.

### Why

1. **Ping + scene-list are editor concepts.** Scenes-as-assets only exist
   at edit time; in a built player, scenes are baked into the binary.
   `EditorSceneManager` lives in `UnityEditor`, which is editor-only by
   construction.
2. **Every Phase B tool targets editor workflows.** Adding components,
   creating prefabs, running tests, modifying assets — all
   `UnityEditor`-gated. Building a runtime mirror of each tool doubles the
   API surface for zero Phase-A value.
3. **Distribution is simpler.** Editor-only = one asmdef, one target
   platform (the Unity Editor itself), no per-platform builds, no IL2CPP
   concerns, no stripping config. The package is small and debuggable.
4. **Runtime tools are best shipped as a separate package later.** A
   hypothetical `com.reify.unity.runtime` in Phase D can share a small set
   of read-only tools (`scene-hierarchy`, `render-queue-audit`,
   `shader-variant-state`) by linking against a small `Reify.Runtime` asmdef.
   That migration is easy *from* an editor-only starting point; the reverse
   is painful.

### Costs of this choice

- Interrogating a running game build is not possible in v1. You have to run
  the game *in the Editor* (play mode) to introspect it. For the immediate
  use case (LLM pair-programming on Unity projects), play-mode-in-editor
  covers 95%+ of what you'd want runtime for.
- Some "runtime-only" state (e.g., per-frame CPU cost, dynamic occlusion
  culling state) is harder to capture from the Editor's managed-code side.
  Acceptable in Phase A.

### If you want to reverse this

Add a `Runtime/` folder, a second asmdef, and move the subset of read tools
that don't reference `UnityEditor` into it. It's a mechanical refactor, not
an architectural one. The sooner you do it the cheaper; if the Editor
assembly accumulates runtime-unfriendly imports across its read tools, the
refactor gets messier. Keep the read tools self-disciplined about imports
to keep the option open.

---

## Decision 3: Upstream skeleton — **IvanMurzak/Unity-MCP structure, selective CoplayDev porting**

### Chosen

- **Structural template:** IvanMurzak/Unity-MCP. File layout, partial-class
  per action, asmdef organization, attribute-based tool registration.
- **Tool-surface reference:** CoplayDev/unity-mcp. Broader set of tools,
  more pragmatic error handling, better asset-path sanitization helpers.

We port from both. Murzak teaches us *how to organize*; Coplay teaches us
*what to cover*.

### Why

- Murzak is C#-native, which matches Decision 1. Its partial-class pattern
  (one file per action method on a `Tool_<Domain>` class) scales cleanly to
  ~100 tools without any file becoming a megabyte.
- Coplay has more coverage (asset operations, menu execution, batch
  dispatch, animator read helpers) and better utilities (asset path
  sanitization, sentinel-based error returns) that Murzak either lacks or
  implements less completely.
- Neither upstream meets the structured-state philosophy (see
  `ARCHITECTURE_ANALYSIS.md` gap list). Our Phase C tools have no upstream
  equivalent and will be written from scratch.

### Costs

- Porting-while-restructuring is slower than straight porting. Every
  ported tool gets reviewed against the structured-state rules
  (JSON return shape, staleness timestamp, verify-after-write) and often
  rewritten rather than copied. Budget accordingly in Phase B.

### If you want to reverse this

No reversal cost per se — "pick a different upstream" is always available.
The decision affects the first 10-20 tool ports; after that each tool is on
its own merits.

---

## Folder structure for `/src`

```
src/
├── Editor/                         # Unity Editor assembly
│   ├── Reify.Editor.asmdef
│   ├── package.json                # UPM package manifest
│   ├── Bridge/
│   │   ├── ReifyBridge.cs          # HttpListener lifecycle
│   │   └── MainThreadDispatcher.cs # Marshal work onto Unity main thread
│   └── Tools/
│       ├── PingTool.cs
│       └── SceneListTool.cs
├── Server/                         # .NET 8 MCP stdio server
│   ├── Reify.Server.csproj
│   ├── Program.cs                  # MCP server entrypoint
│   ├── UnityClient.cs              # HTTP client → Editor bridge
│   └── Tools/
│       ├── PingTool.cs             # MCP tool wrapping UnityClient.Ping
│       └── SceneListTool.cs        # MCP tool wrapping UnityClient.SceneList
└── Shared/                         # DTOs referenced by both
    ├── Reify.Shared.csproj
    └── Contracts/
        ├── PingResponse.cs
        ├── SceneInfo.cs
        └── SceneListResponse.cs
```

### Rationale

- **Three assemblies, three folders, no ambiguity.** `Editor` is what Unity
  loads; `Server` is what Claude Code spawns; `Shared` is the contract.
- **`Tools/` is the growth folder.** Every new tool is a new file in
  `Editor/Tools/` and a new file in `Server/Tools/`. The Murzak
  partial-class convention (`Tool_Scene.Open.cs`, `Tool_Scene.Save.cs`)
  kicks in once a domain has 3+ actions; below that threshold, one file
  per action without partial classes is fine.
- **`Bridge/` is the infrastructure.** It should stay small — ideally two
  or three files. If it grows, something has gone wrong.
- **No `/src/Client/`** for Claude Code or other client configs. Client
  installation lives in `/docs/GETTING_STARTED.md` for Phase A. If we add
  a `/src/Installer/` in Phase D, it'll be a separate decision.

### HTTP bridge contract

Server → Editor over `http://127.0.0.1:17777` (configurable via
`REIFY_BRIDGE_PORT` env var). Single endpoint:

```
POST /tool
Content-Type: application/json

{ "tool": "ping", "args": { ... } }
```

Response:

```
200 OK
Content-Type: application/json

{ "ok": true, "data": { ... } }
```

or

```
500 Internal Server Error
Content-Type: application/json

{ "ok": false, "error": { "code": "UNITY_BUSY", "message": "..." } }
```

Why HTTP and not a named pipe / websocket / shared memory: localhost HTTP
is trivial to implement with `System.Net.HttpListener` (built into Unity's
mono runtime), trivial to debug with `curl`, and has no platform-specific
surprises on Windows/Mac/Linux. The latency cost (~1 ms per round-trip) is
dwarfed by anything Unity does in response.
