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
- over **230** MCP tools (call `reify-tool-list` for the live inventory)
- first-class MCP resources and prompts for discovery and guidance

Current local scope is broad editor-side coverage across roughly 56 domains,
including scene/gameobject/component/asset/prefab work, scripts, packages,
physics, animator, audio, navigation, UI, particles, profiler, tilemap,
terrain, import settings, builds, scriptable objects, animation clips, input
system, asmdefs, tests, project config writes, and meta/introspection layers.

Highlights that reflect the project's philosophy:

- evidence-first tools like `mesh-native-bounds`, `material-inspect`,
  `scene-query`, `render-queue-audit`, `domain-reload-status`,
  `persistence-status`, and `structured-screenshot`
- identity hardening and ambiguity rejection for scene/object/component lookup
- async test jobs with `tests-run`, `tests-status`, `tests-results`, and
  `tests-cancel`
- code-evidence project pipeline tools such as `asmdef-inspect`,
  `asmdef-update-or-create`, `project-tag-add`, and `project-layer-set`
- `batch-execute`, `reify-tool-list`, `reify-version`,
  `reflection-method-find`, and opt-in `reflection-method-call`
- MCP resources such as `reify://about`, `reify://philosophy/structured-state`,
  and `reify://tools/catalog`
- MCP prompts for structured diagnosis, safe change loops, and capability
  escalation

Latest tools validated live against Unity `6000.4.3f1`. The python integration
contract suite under `tests/integration/` covers the critical path (ping,
read-evidence shape, write-receipt shape, error discrimination,
spatial proofs, ADR-002 receipts).

## Architecture

- Client -> server: MCP over stdio
- Server -> Unity: HTTP on `127.0.0.1:17777` by default
- Unity side: `HttpListener` bridge + `MainThreadDispatcher`
- Server side: official `ModelContextProtocol` C# SDK
- Mutation policy: Unity `Undo` integration plus read-back verification where
  practical
- Robustness guards: bridge-side response-size cap and async polling for test
  runs

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
- project structure, tests, and project settings as editable API surfaces

## Repo layout

```text
reify/
|-- client-config/         Claude Code MCP config examples
|-- docs/                  philosophy, architecture, roadmap, and status docs
|-- scripts/               upstream sync helpers
|-- src/
|   |-- Editor/            Unity Editor package, HTTP bridge, and handlers
|   |-- Server/            .NET 8 MCP stdio server
|   `-- Shared/            transport and argument contracts used by the server
`-- third_party/           preserved upstream license texts
```

## Start here

- [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md): bootstrap install and
  smoke-test flow
- [VALIDATION_STEPS.md](VALIDATION_STEPS.md): explicit bootstrap validation
  checklist
- [docs/NEXT_10_AND_TESTS.md](docs/NEXT_10_AND_TESTS.md): current ranked next
  picks and top validation tests
- [AGENTS.md](AGENTS.md): shortest universal instruction contract for LLMs
- [docs/AGENT_PLAYBOOKS.md](docs/AGENT_PLAYBOOKS.md): client-tailored guidance
  for Claude Code, Codex Desktop, Cursor, Windsurf, and VS Code MCP
- [docs/ROADMAP.md](docs/ROADMAP.md): scope and next phases
- [docs/SESSION_REPORT.md](docs/SESSION_REPORT.md): current repo-facing status
  snapshot
- [docs/decisions/ADR-001-tool-naming.md](docs/decisions/ADR-001-tool-naming.md):
  tool naming and response-shape conventions

## License

Apache License 2.0. See [LICENSE](LICENSE), [NOTICE](NOTICE), and
[third_party/](third_party/).
