# Session report — 2026-04-21 — Phase A shipped

Summary of the session that took reify from a scaffold to a compiling
end-to-end smoke test (pending Unity validation).

---

## Name locked

**reify.** See [`NAME.md`](NAME.md) for full rationale.

Tagline: *Structured state for Unity, for LLMs that reason.*

## Architecture decisions made

See [`ARCH_DECISION.md`](ARCH_DECISION.md) for the long form. Short version:

| Decision           | Choice                                  |
|--------------------|-----------------------------------------|
| Server language    | C# / .NET 8 (`ModelContextProtocol` SDK) |
| Unity scope        | Editor-only for Phase A + B             |
| Upstream skeleton  | IvanMurzak structural template, CoplayDev tool-coverage reference |
| Folder layout      | `src/Editor`, `src/Server`, `src/Shared` |
| Transport          | MCP stdio (client ↔ server) + HTTP localhost (server ↔ Editor) |
| `script-execute`   | Enabled by default (matches agentic expectations) |
| Remote repo        | Stays private. No push this session.    |

## What shipped

- **`/src/Shared`** — three DTO files (`PingResponse`, `SceneInfo`,
  `SceneListResponse`) + `BridgeEnvelope`. net8.0.
- **`/src/Server`** — MCP stdio server. `Program.cs`, `UnityClient.cs`,
  `Tools/PingTool.cs`, `Tools/SceneListTool.cs`. **Builds clean:**
  `dotnet build -c Release` → 0 warnings, 0 errors, `reify-server.dll`
  produced.
- **`/src/Editor`** — Unity UPM package `com.reify.unity` (editor-only
  asmdef). Bridge (`ReifyBridge` HttpListener on 127.0.0.1:17777,
  `MainThreadDispatcher` via `EditorApplication.update`) + tool handlers
  (`PingTool`, `SceneListTool`).
- **`/client-config/`** — `claude-code.mcp.json` (dev, `dotnet run`) and
  `claude-code.mcp.published.json` (release, points at published
  `reify-server.exe`).
- **`docs/NAME.md`**, **`docs/ARCH_DECISION.md`**,
  **`docs/GETTING_STARTED.md`**.
- **Obsidian vault reorganised** — `second-brain/unity-mcp-personal`
  renamed to `second-brain/reify`, new `PROJECT.md` master file, run
  summary at `runs/2026-04-21-phase-a-shipped.md`.

## What's flagged (not verified this session)

- **End-to-end round trip** Claude Code → stdio → server → HTTP → Unity
  → JSON back. The server builds. The Unity package is syntactically
  correct. No live Unity Editor was available in the agent environment,
  so the full round trip is documented (see `GETTING_STARTED.md`) but
  not observed.
- **Stdio handshake smoke test** on the server alone (feeding
  `initialize` + `tools/list` via stdin) — attempted once, aborted on
  shell escaping. Build output is the evidence for now.
- **`ModelContextProtocol` 0.3.0-preview.4 attribute shape** — code is
  written to `[McpServerToolType]` + `[McpServerTool]`. If the preview
  SDK bumps, `Program.cs` and `Tools/*.cs` are the surface to revisit.
- **`com.unity.nuget.newtonsoft-json` 3.2.1 pin** — first real Unity
  install will confirm this resolves cleanly on the user's Unity version.

## Blockers hit (and resolved)

- `netstandard2.1` target on the Shared project rejected C# `record
  init` setters (missing `IsExternalInit`). **Fix:** flipped Shared to
  `net8.0`. Safe because Unity never references `Reify.Shared`; it
  mirrors the DTOs via Newtonsoft.
- Implicit usings were off on the Server project —
  `CancellationToken`/`Task`/`Exception`/`HttpClient` didn't resolve.
  **Fix:** `<ImplicitUsings>enable</ImplicitUsings>`.
- PowerShell-inside-Bash escaping ate the stdio smoke-test attempt.
  Deferred to next session with a real MCP client.

## 3-5 highest-leverage things for the next session

Ordered by expected payoff per hour.

### 1. Run the end-to-end smoke test (30 min)

Open Unity, add `com.reify.unity` to `Packages/manifest.json`, run
`curl` against the bridge, connect Claude Code, ask it to call `ping`.
Acceptance criterion for Phase A. Everything downstream waits on this.

### 2. Port four Phase B tools: `scene-open`, `scene-save`,
`gameobject-create`, `gameobject-find` (2-3 hours)

Proves the "add new tool" flow across Editor + Server. Murzak has clean
source for all four — port with structured-state rewrites (JSON returns,
`read_at_utc` fields, verify-after-write).

### 3. Write ADR-001: tool-naming convention (30 min)

`docs/decisions/ADR-001-tool-naming.md`. Lock kebab-case, noun-first for
reads, one-action-per-file, mandatory `read_at_utc`/`frame` on every
return. Future port PRs reference this.

### 4. Implement `mesh-native-bounds` — first philosophy tool (1 hour)

Skip-ahead from Phase C. Solves the "LLM guesses prefab scale" pain in
one call. Cheap to build, huge to demonstrate.

### 5. Decide GitHub repo rename timing (5 min)

The private repo is still called `unity-mcp-personal`. Cheapest fix now:
`gh repo rename unity-mcp-personal reify` + `git remote set-url origin
https://github.com/mattebin/reify.git`. Do it before Phase B commits
accrue so history is clean.

---

## Meta

This report lives in-repo at `docs/SESSION_REPORT.md` and is mirrored as
a dated run summary at
`second-brain/reify/runs/2026-04-21-phase-a-shipped.md`. The run summary
is vault-facing; this one is repo-facing.
