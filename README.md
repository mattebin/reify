# reify

**Unity Editor MCP server for LLMs that need checkable evidence instead of screenshots.** 259 tools returning structured JSON — so an agent can diff, grep, and verify what it just did, and a human reviewer can reject writes that don't prove themselves.

[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Unity](https://img.shields.io/badge/Unity-6000.4%2B-black?logo=unity)](https://unity.com/releases/editor/archive)
[![MCP](https://badge.mcpx.dev?status=on 'MCP Enabled')](https://modelcontextprotocol.io/introduction)
[![CI](https://github.com/mattebin/reify/actions/workflows/ci.yml/badge.svg)](https://github.com/mattebin/reify/actions/workflows/ci.yml)
[![Contract tests](https://img.shields.io/badge/contract%20tests-21%2F21-brightgreen)](tests/integration/test_reify_contract.py)
[![MCP clients](https://img.shields.io/badge/clients-any%20MCP--capable%20LLM-6f42c1)](client-config/)

Works with any LLM client that can launch an MCP stdio server. Claude Code / Claude Desktop, Cursor, VS Code MCP, and Windsurf configs are included as examples, not the ceiling. Live-validated against **Unity 6000.4.3f1**. Apache 2.0.

## What an LLM actually does with reify

Here's the stickman-build receipt from a real session, trimmed. The agent reads live dimensions, places geometry, and the evidence layer proves every joint:

```json
// agent calls: primitive-defaults  → Capsule is 2m tall, radius 0.5m
{"kind": "Capsule", "height": 2.0, "radius": 0.5, "axis": "Y"}

// agent calls: gameobject-create  primitive=Capsule scale=(0.2, 0.5, 0.2)
// reify's response (trimmed) — receipt is self-proving:
{
  "gameobject": { "name": "LeftLeg", "instance_id": -43654, ... },
  "primitive_defaults": { "height": 2.0, "radius": 0.5, ... },
  "mesh_bounds":        { "world_size": { "x": 0.20, "y": 1.00, "z": 0.20 } },
  "world_height": 1.000,     // proven, not guessed
  "applied_fields":     [ { "field": "primitive_created", "before": null, "after": {...} } ]
}

// agent calls: spatial-anchor-distance  LeftLeg.top ↔ Torso.bottom
{
  "distance_meters": 0.0,         // proves they touch
  "axis_gap_meters": { "x": 0, "y": 0, "z": 0 },
  "within_tolerance": true
}

// agent calls: scene-diff (against pre-build snapshot)
{ "added_count": 7, "removed_count": 0, "changed_count": 0 }
```

A reviewer reading the transcript doesn't have to trust the agent's word on *anything*. The receipts are the proof.

> 🛑 **One thing to know up front — reify is a *discipline*, not just tools.**
> Evidence + guides. Tools without the discipline look like they work and are quietly wrong. Have your agent call `reify-orient` before its first write — one MCP call, returns the full reading list. Or read [AGENTS.md](AGENTS.md) + [docs/AGENT_TRAPS.md](docs/AGENT_TRAPS.md) yourself, both short.

> ⚠️ **Honest disclaimer.**
> - **Editor-only.** reify runs inside the Unity Editor. It does not ship to built players. No runtime gameplay hooks.
> - **One Unity version battle-tested.** Live-validated on Unity `6000.4.3f1`. Other versions probably work (reflection is version-tolerant for known drift points) but the contract suite only runs against 6.
> - **Opinionated writes.** Write tools reject silent no-ops (per [ADR-002](docs/decisions/ADR-002-write-receipts.md)) and spatial claims require anchor proofs (per [ADR-003](docs/decisions/ADR-003-spatial-claims.md)). If your agent is used to rubber-stamping "done!", reify will catch it.
> - **If you want plug-and-play with broader installer polish, try [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) or [IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP) first.** reify prioritises *verifiability over breadth*. It overlaps them on most domains, is ahead on evidence/trust, behind on installer UX.

## How it works

```
  LLM client ──► MCP stdio ──► .NET 8 server (Reify.Server)
                                     │
                                     ▼
                         HTTP on 127.0.0.1:17777
                                     │
                                     ▼
                      Unity Editor HttpListener bridge
                                     │
                 ┌───────────────────┼───────────────────┐
                 ▼                   ▼                   ▼
           read tools         write tools        evidence tools
        (scene-snapshot,   (gameobject-modify, (spatial-primitive-
         component-get,     component-set-      evidence,
         asset-snapshot,    property,           spatial-anchor-
         shader-inspect,    asset-create/       distance, scene-diff,
         ...)               move/delete,        asset-diff,
                            prefab-save, ...)   primitive-defaults)
                                     │
                                     ▼
                           returns JSON with
                           read_at_utc, frame,
                           stable identifiers,
                           before/after on writes
```

Every tool call is a JSON request to `/tool` on the bridge. Every read returns evidence fields (`read_at_utc`, `frame`, stable IDs). Every write returns `applied_fields` with `{field, before, after}` per ADR-002. Spatial claims return anchor/bounds data you can check directly.

## Install

### Easy way — from prebuilt release
1. Grab the latest `reify-server-<platform>.zip` or `.tar.gz` from the [Releases page](https://github.com/mattebin/reify/releases).
2. Unzip somewhere permanent.
3. Add the Unity Editor package to your project — in `Packages/manifest.json`:
   ```json
   "com.reify.unity": "https://github.com/mattebin/reify.git?path=/src/Editor"
   ```
4. Open your Unity project. Watch the Console for `[Reify] Bridge listening on http://127.0.0.1:17777/`.
5. Point your MCP client at the unpacked `reify-server` binary. Any MCP-capable LLM client should work; `client-config/` includes ready-to-go examples for [Claude Code](client-config/claude-code.mcp.json), [Cursor](client-config/cursor.mcp.json), [VS Code](client-config/vscode-mcp.json), and [Windsurf](client-config/windsurf.mcp.json).

### From source (you want the latest or you want to contribute)
```bash
git clone https://github.com/mattebin/reify.git
cd reify
dotnet build src/Server/Reify.Server.csproj -c Release
```
Add the Unity package via a `file:` reference (see `client-config/README.md` for `<PATH_TO_REIFY>` substitution).

### Verify the install

From any MCP client connected to reify:
```
ping                → { status: ok, unity_version: 6000.4.3f1, ... }
reify-orient        → full orientation dump (thesis + loop + reading list)
reify-self-check    → contract test battery, expect fail_count: 0
reify-tool-list     → live inventory of all 259 tools
```

If `ping` fails, the Unity Editor side isn't running yet. Open your project; wait for the bridge log line.

## What's in the toolbox

Call `reify-tool-list` for the live list. A sampling:

| Category | Tools |
|---|---|
| **Meta / orientation** | `reify-orient`, `reify-self-check`, `reify-tool-list`, `reify-version`, `reify-command-center-open`, `ping`, `domain-reload-status` |
| **Scene + GameObjects** | `scene-snapshot`, `scene-diff`, `scene-query`, `gameobject-create`, `gameobject-modify`, `gameobject-find`, `gameobject-duplicate`, `component-add`, `component-get`, `component-set-property`, `component-remove` |
| **Spatial proofs** | `primitive-defaults`, `spatial-primitive-evidence`, `spatial-anchor-distance`, `geometry-line-primitive`, `mesh-native-bounds` |
| **Assets + prefabs** | `asset-snapshot`, `asset-diff`, `asset-create`, `asset-move`, `asset-rename`, `asset-delete`, `asset-copy`, `asset-shader-list-all`, `prefab-create`, `prefab-save`, `material-inspect` |
| **Rendering / graphics** | `shader-inspect`, `shader-graph-inspect`, `render-queue-audit`, `lighting-diagnostic`, `camera-inspect`, `light-inspect`, `structured-screenshot` (escape hatch) |
| **Animation + VFX** | `animation-clip-inspect`, `animation-clip-events-read/set`, `animator-state`, `animator-parameter-set`, `animator-crossfade`, `particle-system-inspect`, `particle-play/stop/simulate`, `visual-effect-inspect`, `timeline-director-*` |
| **Physics** | `physics-raycast`, `physics-spherecast`, `physics-overlap-*`, `physics-settings`, 2D variants |
| **Scripts (Roslyn-backed)** | `script-inspect`, `script-execute`, `script-update-or-create`, `script-delete` |
| **Async jobs** | `tests-run`, `tests-status`, `tests-results`, `build-execute-job`, `asset-refresh-job`, `addressables-build-job`, `job-list`, `job-cancel` |
| **Remote Unity control** | `editor-request-script-compilation` (compile + domain reload without alt-tabbing), `editor-menu-execute`, `editor-selection-set` |
| **Issue reporting** | `reify-log-issue`, `reify-list-pending-issues` (see below) |

## LLM-reported issues (optional)

Reify includes an issue-reporting loop for when an LLM hits a reify bug and wants the maintainer to know. **Completely optional** — skip this section if you don't care. Users can review reports from `Window > Reify > Command Center` inside Unity or with the CLI script below.

### How it works
```
  LLM hits a problem using reify
            │
            ▼
  calls  reify-log-issue  with { model_name, issue_title, effort, ... }
            │
            ▼
  file written to  <reify_repo>/reports/llm-issues/pending/<timestamp>-<slug>.md
            │
            ▼
  ⏸  waits for the user to review
            │
            ▼
  user runs:   python scripts/review-llm-issues.py
            │
            ▼
  for each pending report: [y]es / [n]o / [d]elete / [s]kip / [q]uit
            │
            ▼
  on y: gh issue create --repo <your fork> --label llm-reported
                          (you approve the identity + repo first)
```

### Why the user-in-the-loop gate
LLMs are prolific; without a gate they'd flood any tracker with duplicates and half-baked reports. The review script:

1. **Auto-detects your fork's repo** from `git remote get-url origin`. Your LLM's reports go to *your* tracker, not upstream.
2. **Shows filing identity before the prompt** — `Filing target: <repo> / Filing as: <gh user>`. No silent posting.
3. **Asks per-report** — you can say no, delete, skip, or quit.
4. **Auto-creates the labels** (`llm-reported`, `severity:{info,warn,error,critical}`, `effort:{S,M,L}`, `reporter:<model_name>`) on first use.

The LLM tool **never hits GitHub directly**. Everything is local markdown until you say otherwise.

### Opting out entirely
Just never run `scripts/review-llm-issues.py`. Pending reports accumulate in `reports/llm-issues/pending/` harmlessly — they're gitignored. Delete the folder, done. You can also tell your LLM "don't call reify-log-issue" in your system prompt if you want to prevent the reports from being written at all.

See [`reports/llm-issues/README.md`](reports/llm-issues/README.md) for the full field schema and [`docs/AGENT_TRAPS.md`](docs/AGENT_TRAPS.md) for when an LLM should report.

## Start here (reading order)

In rough order of what to read first:

1. [AGENTS.md](AGENTS.md) — operating loop for LLMs, 12 numbered rules
2. [docs/PHILOSOPHY.md](docs/PHILOSOPHY.md) — why evidence, not screenshots
3. [docs/AGENT_TRAPS.md](docs/AGENT_TRAPS.md) — 5 observed LLM failure modes + one-line heuristics each
4. [docs/decisions/ADR-001-tool-naming.md](docs/decisions/ADR-001-tool-naming.md), [ADR-002](docs/decisions/ADR-002-write-receipts.md), [ADR-003](docs/decisions/ADR-003-spatial-claims.md) — normative rules
5. [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md) — install + substitute `<PATH_TO_REIFY>`
6. [docs/AGENT_PLAYBOOKS.md](docs/AGENT_PLAYBOOKS.md) — client-specific setup
7. [CONTRIBUTING.md](CONTRIBUTING.md) — add-a-tool contract
8. [VALIDATION_STEPS.md](VALIDATION_STEPS.md) — explicit bootstrap checklist

Total reading time: ~15 minutes. All short on purpose.

## Testing

Python contract suite against a live Unity:
```bash
python tests/integration/test_reify_contract.py
# 21 passed, 0 failed
```
Runs ping, read-evidence shape, write-receipt shape (ADR-002), error code discrimination (`INVALID_ARGS` / `COMPONENT_NOT_FOUND` / `TOOL_EXCEPTION` / `UNKNOWN_TOOL`), spatial anchor proofs (ADR-003), and the `reify-self-check` tool. No pytest required — standalone runner.

## Technical honesty

- **reify does not make an LLM "good at Unity".** It makes an LLM's claims *checkable*. A sloppy agent still makes sloppy games, you just catch it one tool call earlier.
- **Package-gated tools fail gracefully.** Addressables / Cinemachine / Timeline / VFX Graph / TextMeshPro / MPPM tools return a structured "package not installed" error when the package is absent, not an NRE.
- **Unity version drift is a real risk.** Frame Debugger is currently broken on Unity 6 because the API moved (file an issue — `reify-log-issue`-reported ones land cleanly). Reflection-based tools scan all loaded assemblies to survive most assembly renames, but nothing is bulletproof.
- **The bridge is localhost-only.** No auth, because `127.0.0.1:17777` is not exposed. Don't run reify on a shared machine where untrusted local processes could hit the port.
- **If you find reify ignoring your args or silently succeeding**, that's the bug ADR-002 was written to prevent. File a `reify-log-issue`.

## Architecture

- Client → server: MCP over stdio
- Server → Unity: HTTP on `127.0.0.1:17777`
- Unity side: `HttpListener` bridge + `MainThreadDispatcher` for marshalling to the main thread
- Server side: official [`ModelContextProtocol`](https://github.com/modelcontextprotocol/csharp-sdk) C# SDK
- Mutation policy: Unity `Undo` integration + read-back verification + ADR-002 receipts
- Bridge guards: response-size cap (`REIFY_MAX_RESPONSE_BYTES`, default 786432), `MissingComponentException` routed to `COMPONENT_NOT_FOUND`, `ArgumentException` routed to `INVALID_ARGS`, ISO-8601 string fields preserved verbatim (no DateTime auto-coercion)

Primary code paths:
- [src/Server/Program.cs](src/Server/Program.cs) — MCP entry
- [src/Editor/Bridge/ReifyBridge.cs](src/Editor/Bridge/ReifyBridge.cs) — HTTP listener + error routing
- [src/Editor/Bridge/MainThreadDispatcher.cs](src/Editor/Bridge/MainThreadDispatcher.cs) — main-thread marshalling with `Func<Task<T>>` support
- [src/Editor/Tools/](src/Editor/Tools/) — all editor-side handlers

## Repo layout

```
reify/
├── client-config/          per-client MCP config examples (substitute <PATH_TO_REIFY>)
├── docs/                   philosophy, architecture, agent playbooks, ADRs
│   └── decisions/          ADR-001 (naming), ADR-002 (write receipts), ADR-003 (spatial claims)
├── reports/llm-issues/     LLM-reported issues; pending/submitted/dismissed folders are gitignored
├── scripts/                review-llm-issues.py + upstream sync helpers
├── src/
│   ├── Editor/             Unity Editor package, HTTP bridge, tool handlers
│   ├── Server/             .NET 8 MCP stdio server
│   └── Shared/             transport + argument contracts
├── tests/integration/      Python contract suite (live Unity required)
└── third_party/            preserved upstream license texts
```

## Alternatives — pick the right one

| You want | Use |
|---|---|
| Evidence-first, receipts, opinionated spatial correctness, LLM client IS the UI | **reify** (you are here) |
| Fastest setup, polished UX, broadest "just works" domain coverage | [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) |
| Runtime hooks, broader ecosystem, C# at runtime | [IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP) |
| A plug-and-play Unity assistant with no discipline requirement | not reify |

## License

Apache License 2.0. See [LICENSE](LICENSE), [NOTICE](NOTICE), and [third_party/](third_party/) for preserved upstream licenses (CoplayDev and IvanMurzak both MIT).
