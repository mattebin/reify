# Local Status Snapshot — 2026-04-21

This file is the repo-facing status snapshot for the current local `reify`
worktree. Older Phase A notes have been superseded by the implementation that
now exists under `src/`.

## Summary

- `reify` is an editor-only Unity MCP stack built in C# / .NET 8
- the local worktree currently exposes `69` MCP tools
- latest local validation notes report live validation against Unity
  `6000.4.3f1`
- development is currently local-first, so the local worktree is more current
  than GitHub

## Implemented shape

- `src/Server/`: .NET 8 MCP stdio server using `ModelContextProtocol`
- `src/Editor/`: Unity package `com.reify.unity`
- `src/Shared/`: server-side transport and argument contracts
- transport: MCP stdio (client to server) + localhost HTTP (server to Unity)
- mutations use Unity `Undo` APIs and return structured results

## Current tool surface

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

Philosophy and diagnostic highlights in the current local worktree:

- `mesh-native-bounds`
- `material-inspect`
- `scene-query`
- `project-render-pipeline-state`
- `render-queue-audit`
- `lighting-diagnostic`
- `asset-dependents`
- `domain-reload-status`
- `persistence-status`

## Recently completed locally

- physics query domain
- animator mutation tools
- persistence and domain-reload diagnostics
- bridge registration refactor from manual `Register(...)` calls to
  `[ReifyTool("name")]` plus automatic handler discovery
- trustworthiness fixes for:
  - stale `instance_id` object-reference writes
  - play-mode transition reporting in `domain-reload-status`

## Current caveats

- GitHub may lag substantially behind the local worktree
- some historical docs still describe the early bootstrap phase
- this file is a repo snapshot, not the full local journal

## Next likely work

- remaining identity and ambiguity fixes for path-based object resolution
- packaging and installability improvements
- deeper coverage for scripts, richer validation flows, and Phase D polish

## Pointers

- [../README.md](../README.md)
- [ROADMAP.md](ROADMAP.md)
- [PHILOSOPHY.md](PHILOSOPHY.md)
- [ARCH_DECISION.md](ARCH_DECISION.md)
- [decisions/ADR-001-tool-naming.md](decisions/ADR-001-tool-naming.md)
