# Contributing to reify

reify is a structured-state Unity MCP stack. The core differentiator is
evidence discipline — every tool returns code-grounded data an LLM can
read, diff, and trust without reaching for a screenshot. Contributions
are welcome when they preserve that discipline.

## Before you start

1. Read [`docs/PHILOSOPHY.md`](docs/PHILOSOPHY.md) — the structured-state
   thesis in ~200 lines.
2. Read [`docs/decisions/ADR-001-tool-naming.md`](docs/decisions/ADR-001-tool-naming.md) —
   the naming + response-shape contract every tool follows.
3. Skim [`AGENTS.md`](AGENTS.md) — the operating contract agentic clients
   follow when using reify. Your changes should fit that.
4. Scan [`docs/NEXT_10_AND_TESTS.md`](docs/NEXT_10_AND_TESTS.md) for the
   current ranked work.

## Add-a-tool contract

Every new tool ships at minimum:

1. An editor-side handler decorated with `[ReifyTool("kebab-case-name")]`
   in `src/Editor/Tools/`.
2. A server-side wrapper in `src/Server/Tools/` attributed with
   `[McpServerTool(Name = "...")]` and a `[Description(...)]` that
   describes purpose, arguments, return shape, and warnings.
3. `read_at_utc` + `frame` in every response (ADR-001 §3).
4. Resolution by `instance_id` OR `gameobject_path` (or asset_path where
   applicable). No silent fallbacks.
5. Structural rejection on ambiguous identity — throw with concrete
   candidate instance_ids instead of picking one.
6. For writes: `Undo` integration + self-proving receipt per
   [`ADR-002`](docs/decisions/ADR-002-write-receipts.md):
   `applied_fields[]` with `{field, before, after}` pairs, plus
   domain-specific evidence (`guids_touched[]` for asset writes,
   `mesh_bounds` + `primitive_defaults` for primitive creates, etc.).
   Silent no-ops and rubber-stamp receipts are rejected in review.

## Repo layout

| Path | Purpose |
|---|---|
| `src/Editor/Tools/*.cs` | Unity-side handlers invoked by the bridge |
| `src/Editor/Bridge/*.cs` | `HttpListener`, attribute scan, safety caps |
| `src/Server/Tools/*.cs` | MCP stdio wrappers exposed to clients |
| `src/Server/ReifyResources.cs` / `ReifyPrompts.cs` | MCP resources + prompts |
| `src/Shared/**` | DTOs referenced by both server + editor where typing helps |
| `client-config/` | Claude Code, Cursor, Windsurf, VS Code MCP configs |
| `docs/` | philosophy, ADRs, roadmap, agent playbooks |

## Build + validate

The server is a .NET 8 console with stdio MCP; the Unity-side is a UPM
package.

Build the server (avoids DLL lock on a running `reify-server.dll`):

```powershell
dotnet build src/Server/Reify.Server.csproj -c Release `
  -o "$env:TEMP\reify-scratch-build"
```

Then focus the Unity Editor once to force a recompile of the Editor
assembly. The bridge logs `[Reify] Bridge listening on http://127.0.0.1:17777/`
when it is up.

Live-validate via the test harness described in [`VALIDATION_STEPS.md`](VALIDATION_STEPS.md)
or by calling `reify-tool-list` + the new tool from any MCP client.

## Evidence-layer warnings

Tools gain philosophy-layer warnings when structured state is ambiguous
or misconfigured. Examples that already ship:

- `mesh-native-bounds` flags tiny/huge meshes so the LLM plans for scale.
- `material-inspect` flags MPB overrides so the Inspector is not misread.
- `render-queue-audit` flags transparent-renderer bounds overlap so
  camera-dependent sorting bugs are caught in one call.

If your new tool exposes a subsystem where "looks right but wrong in
subtle ways" is common, add a warning. This is the part that sets reify
apart from a generic Unity MCP.

## Commit + PR expectations

- One logical change per commit. Squash style is fine but keep the
  boundary clean (new domain, bug fix, refactor, docs).
- Commit message starts with `feat:`, `fix:`, `refactor:`, `docs:`, or
  `chore:` and stays under ~72 chars on the first line.
- Include a run note under `second-brain/reify/runs/` style when the
  change reshapes the tool surface or operational contract (Codex/Claude
  sessions follow this convention today).
- Keep `CHANGELOG.md` honest under `[Unreleased]` until a version tag.

## What does not belong

- Screenshot-first tools (we ship one, and it is the explicit escape
  hatch).
- Tools that wrap arbitrary reflection without `REIFY_ALLOW_REFLECTION_CALL`.
- Tools that expand the MCP surface without adding evidence to the
  response.
- Any dependency that forces the package onto a specific render pipeline,
  input system, or other optional Unity package. Use reflection when the
  tool needs a package that might not be installed.

## License

Apache 2.0. Contributions are accepted under the same licence.
