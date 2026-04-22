# Bootstrap validation

This file is the narrow validation checklist for proving the `reify` transport
path is healthy. The local worktree now contains many more tools, but `ping`
and `scene-list` remain the cheapest baseline smoke tests.

## Pre-flight

- `.NET 8` installed
- Unity available
- Claude Code CLI available
- `src\Server\bin\Release\net8.0\reify-server.dll` exists after build
- port `17777` is free, or you have chosen a different `REIFY_BRIDGE_PORT`

## A — Install the Unity package

Add to the Unity project's `Packages/manifest.json`:

```json
"com.reify.unity": "file:<PATH_TO_REIFY>/src/Editor",
"com.unity.nuget.newtonsoft-json": "3.2.1"
```

Wait for Unity to compile, then verify the Console shows:

```text
[Reify] Bridge listening on http://127.0.0.1:17777/
```

## B — Call the bridge directly

```powershell
Invoke-RestMethod -Method Post `
  -Uri 'http://127.0.0.1:17777/tool' `
  -ContentType 'application/json' `
  -Body '{"tool":"ping"}'
```

```powershell
Invoke-RestMethod -Method Post `
  -Uri 'http://127.0.0.1:17777/tool' `
  -ContentType 'application/json' `
  -Body '{"tool":"scene-list"}'
```

If both return structured JSON, the bootstrap bridge path is healthy.

## C — Register the MCP server

Use either:

- `client-config/claude-code.mcp.json`
- `client-config/claude-code.mcp.published.json`

Or point Claude Code directly at the server DLL / published executable.

Verify:

```powershell
claude mcp list
```

## D — Ask Claude Code to use the tools

Try:

- `Call the reify ping tool and show me the response.`
- `Call the reify scene-list tool and show me the response.`

## Expected result

- `ping` returns project info, Unity version, `read_at_utc`, and `frame`
- `scene-list` returns the currently open scenes with structured metadata
- Claude Code can call `reify` through MCP without manual bridge interaction

## Common failure modes

- `UNKNOWN_TOOL`: wrong tool name
- `UNITY_UNREACHABLE`: Unity closed or bridge not running
- connection refused: Unity still compiling or wrong port
- timeout: Unity main thread blocked or editor mid-refresh

## Scope note

This file validates the bootstrap path only. It is not the full validation
story for the current local tool surface.
