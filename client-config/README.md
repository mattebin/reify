# MCP client configs for reify

Drop-in examples for wiring reify's stdio MCP server into your LLM client.
One file per client. Pick one, copy it to the client's config location, and
substitute `<PATH_TO_REIFY>` with the absolute path where you cloned reify.

## Quick Windows setup

For the usual stable setup, build the published server once and install a
client config from a template:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-server.ps1 -Publish
powershell -ExecutionPolicy Bypass -File scripts\install-client-config.ps1 -Client claude
```

Supported `-Client` values are `claude`, `cursor`, `vscode`, and `windsurf`.
Claude and Cursor have conventional default paths. VS Code and Windsurf write
to `.scratch/client-config/` unless you pass `-OutputPath`, because their MCP
config locations vary by extension/workspace.

Use `-Preview` to print the generated JSON without writing it.

## Manual substitution

Every file here uses the literal placeholder `<PATH_TO_REIFY>`. On the machine
that runs the MCP client, replace it with the absolute path to your reify
checkout.

**Windows (PowerShell), example:**

```powershell
(Get-Content client-config\claude-code.mcp.published.json) `
  -replace '<PATH_TO_REIFY>', ((Get-Location).Path -replace '\\', '\\') `
  | Set-Content $env:APPDATA\Claude\claude_desktop_config.json
```

**macOS / Linux (bash), example:**

```bash
sed "s|<PATH_TO_REIFY>|$(pwd)|g" client-config/claude-code.mcp.json \
  > ~/.config/Claude/claude_desktop_config.json
```

The path uses `\\` (double-escaped backslashes) on Windows variants so the
config is still valid JSON. On macOS / Linux you should end up with forward
slashes after substitution.

## Files

| File | Client | Install location (typical) |
|---|---|---|
| `claude-code.mcp.json` | Claude Code / Claude Desktop dev config (`dotnet run`) | `%APPDATA%/Claude/claude_desktop_config.json` on Windows, `~/.config/Claude/claude_desktop_config.json` on macOS/Linux |
| `claude-code.mcp.published.json` | Claude Code / Desktop using `dist/reify-server/reify-server.exe` | Same as above |
| `cursor.mcp.json` | Cursor | `~/.cursor/mcp.json` or per-workspace `.cursor/mcp.json` |
| `vscode-mcp.json` | VS Code (Claude / Copilot Chat MCP extensions) | workspace `.vscode/mcp.json` |
| `windsurf.mcp.json` | Windsurf | per Windsurf's MCP config path |

## After substitution sanity check

Once the client has the config, a working install answers:

```text
ping                    -> { unity_version, project_name, ... }
reify-tool-list         -> full tool listing
reify-self-check        -> contract test battery, should be pass=all
```

If `ping` fails, the bridge is not running in Unity. Open the project in Unity
and wait for the `[Reify] Bridge listening on http://127.0.0.1:17777/` log
line, then retry.

## Published vs dev config

- `claude-code.mcp.json` expects you to have the .NET SDK installed and runs
  `dotnet run` on the server project source. Good for development.
- Published configs expect a pre-built executable at
  `dist/reify-server/reify-server.exe`. Good for day-to-day client use and for
  non-dev users. Build with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-server.ps1 -Publish
```

If a client is already running reify, avoid rebuilding the binary it is using.
The helper builds validation outputs into `.scratch/server-build/<timestamp>`
by default and publishes client binaries into `dist/reify-server` only when
you pass `-Publish`. Use `scripts/stop-reify-servers.ps1 -WhatIf` to see which
local reify server processes would be stopped before republishing.
