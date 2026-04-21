# reify

> Structured state for Unity, for LLMs that reason.

## What this is

`reify` is a Unity MCP stack built for API agents that need structured,
code-grounded evidence instead of screenshots, inspector eyeballing, or
free-form summaries. The project exposes Unity Editor state as machine-readable
JSON so an agent can reason from identifiers, numbers, timestamps, frame
context, and read-back verification.

The guiding idea is simple:

- screenshots are an escape hatch, not the default workflow
- read tools should return evidence the model can diff, grep, and trust
- write tools should verify by reading back through Unity's own code path

See [docs/PHILOSOPHY.md](docs/PHILOSOPHY.md) for the longer thesis.

## Current status

This repo is no longer a planning-only scaffold. The current local worktree
contains:

- a .NET 8 MCP stdio server
- a Unity Editor package (`com.reify.unity`)
- a localhost HTTP bridge between the server and Unity
- shared contracts used by the server transport
- `69` MCP tools in the current local codebase

Current tool domains in the local worktree:

- Scene: 7
- GameObject: 4
- Component: 5
- Asset: 7
- Prefab: 7
- Play mode: 6
- Console log: 3
- Editor ops: 6
- Project info: 7
- Physics: 6
- Animator: 4
- Ping: 1

Philosophy and diagnostic highlights layered across those domains:

- `mesh-native-bounds`
- `material-inspect`
- `scene-query`
- `project-render-pipeline-state`
- `render-queue-audit`
- `lighting-diagnostic`
- `asset-dependents`
- `domain-reload-status`
- `persistence-status`

Latest local validation notes report live validation against Unity
`6000.4.3f1`. Development is currently local-first, so the local worktree is
ahead of any pushed GitHub state.

## Architecture

- Client -> server: MCP over stdio
- Server -> Unity: HTTP on `127.0.0.1:17777` by default
- Unity side: `HttpListener` bridge + `MainThreadDispatcher`
- Server side: official `ModelContextProtocol` C# SDK
- Mutation policy: Unity `Undo` integration plus read-back verification where
  practical

Primary code paths:

- [src/Server/Program.cs](src/Server/Program.cs)
- [src/Server/UnityClient.cs](src/Server/UnityClient.cs)
- [src/Editor/Bridge/ReifyBridge.cs](src/Editor/Bridge/ReifyBridge.cs)
- [src/Editor/Bridge/MainThreadDispatcher.cs](src/Editor/Bridge/MainThreadDispatcher.cs)

## Why it exists

The project takes inspiration from two major upstreams:

- [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp)
- [IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP)

But the differentiator is not just combining tool coverage. The point is to
make Unity state legible to an API agent:

- mesh bounds before placement instead of scale guessing
- material and MPB provenance instead of screenshot debugging
- animator state as structured data instead of visual inference
- render queue, scene query, persistence, and reload diagnostics as explicit
  JSON

## Repo layout

```text
reify/
├── client-config/         Claude Code MCP config examples
├── docs/                  philosophy, architecture, roadmap, and status docs
├── scripts/               upstream sync helpers
├── src/
│   ├── Editor/            Unity Editor package, HTTP bridge, and handlers
│   ├── Server/            .NET 8 MCP stdio server
│   └── Shared/            transport and argument contracts used by the server
└── third_party/           preserved upstream license texts
```

## Start here

- [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md): bootstrap install and
  smoke-test flow
- [VALIDATION_STEPS.md](VALIDATION_STEPS.md): explicit bootstrap validation
  checklist
- [docs/ROADMAP.md](docs/ROADMAP.md): scope and next phases
- [docs/SESSION_REPORT.md](docs/SESSION_REPORT.md): current repo-facing status
  snapshot
- [docs/decisions/ADR-001-tool-naming.md](docs/decisions/ADR-001-tool-naming.md):
  tool naming and response-shape conventions

## License

Apache License 2.0. See [LICENSE](LICENSE), [NOTICE](NOTICE), and
[third_party/](third_party/).
