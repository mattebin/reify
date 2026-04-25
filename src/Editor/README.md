# Reify - Unity Editor package

> Structured state for Unity, for LLMs that reason.

Editor-side HTTP bridge for the Reify MCP server. 259 tools that expose
Unity Editor state and operations as machine-readable JSON with
philosophy-layer warnings, Undo-backed writes, spatial anchor proofs,
and ambiguity rejection on path lookups. Call `reify-tool-list` for the
live inventory.

## Install

### Option A - Local file reference (current)

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.reify.unity": "file:<PATH_TO_REIFY>/src/Editor",
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

Use forward slashes even on Windows. If the path contains spaces and
resolution fails, create a junction to a space-free path and reference
that instead.

### Option B - Git URL (private or public)

```json
{
  "dependencies": {
    "com.reify.unity": "https://github.com/mattebin/reify.git?path=/src/Editor"
  }
}
```

For a private repo, use a GitHub credential that Unity Package Manager can
access on this machine.

### Option C - OpenUPM (future)

Planned. Currently local + git URL only.

## First-run verification

After Unity recompiles the package, the Console should show:

```
[Reify] Bridge listening on http://127.0.0.1:17777/
```

Open the human dashboard with `Window > Reify > Command Center` or
`Tools > Reify > Command Center`.

Direct probe (PowerShell):

```powershell
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:17777/tool' `
  -ContentType 'application/json' -Body '{"tool":"ping"}'
```

## MCP client configuration

Three ready-to-use configs live in [`client-config/`](../../client-config/)
in the repo root:

- `claude-code.mcp.json` - Claude Code
- `cursor.mcp.json` - Cursor
- `windsurf.mcp.json` - Windsurf
- `vscode-mcp.json` - VS Code MCP (Copilot Agents)

Each expects the published `reify-server.dll` - build it from `src/Server`:

```powershell
dotnet build src/Server/Reify.Server.csproj -c Release
```

## Dependencies

- `com.unity.nuget.newtonsoft-json` 3.2.1 (required)
- Unity 6000.0+ (validated on 6000.4.3f1)
- `com.unity.inputsystem` optional - enables `input-actions-asset-inspect`,
  `input-player-input-inspect`, `input-devices` via reflection
- `com.unity.ai.navigation` optional - enables full NavMesh features
- `com.unity.render-pipelines.universal` optional - enables URP-specific
  `project-render-pipeline-state` + `lighting-diagnostic` warnings

All optional packages are accessed via reflection - Reify compiles and
runs without them.

## Environment variables

- `REIFY_BRIDGE_PORT` - override the default `17777` (both Unity side and
  server side must agree)
- `REIFY_BRIDGE_HOST` - default `127.0.0.1`, rarely changed
- `REIFY_MAX_RESPONSE_BYTES` - bridge-side safety cap for one tool response
  (default 786432 bytes). If a tool exceeds this, reify returns a structured
  `RESPONSE_TOO_LARGE` error instead of silently breaking stdio transport.
- `REIFY_ALLOW_REFLECTION_CALL` - set to `1` to enable the
  `reflection-method-call` escape hatch. Disabled by default since it
  can invoke arbitrary .NET methods.

## What's included

Domains: scene, gameobject, component, asset, prefab, play-mode,
console-log, editor ops, project info, packages, scripts, physics,
animator, audio, navigation, UI, camera + light, particles, tilemap,
constraint + LOD, terrain, import settings, builds, scriptable objects,
animation clips, input system, asmdefs, tests, project config writes,
and meta/reflection/batch.

Philosophy tools: mesh-native-bounds, material-inspect, scene-query,
project-render-pipeline-state, animator-state, render-queue-audit,
asset-dependents, lighting-diagnostic, domain-reload-status,
persistence-status, structured-screenshot.

Meta: `reify-tool-list`, `reify-version`, `batch-execute`,
`reflection-method-find`, `reflection-method-call`.

MCP extras:

- resources: `reify://about`, `reify://philosophy/structured-state`,
  `reify://tools/catalog`, `reify://tools/{name}`
- prompts: `reify-structured-diagnosis`, `reify-safe-change-loop`,
  `reify-capability-escalation`

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `UNITY_UNREACHABLE` from server | Unity closed or not compiled | Open Unity, watch for `[Reify] Bridge listening` |
| Bind error on 17777 | Port in use | Set `REIFY_BRIDGE_PORT` on both sides |
| Tool returns `UNKNOWN_TOOL` | Stale Editor assembly | Focus Unity to force recompile |
| `RESPONSE_TOO_LARGE` | One tool returned too much JSON for safe transport | Narrow the query, use pagination, or raise `REIFY_MAX_RESPONSE_BYTES` |
| Timeout after 30 s | Unity main thread blocked | Dismiss modal dialogs; wait for import to finish |

## License

Apache 2.0. See [LICENSE](../../LICENSE) and [NOTICE](../../NOTICE).
