# MCP client configs for reify

Drop-in examples for wiring reify's stdio MCP server into your LLM client.
One file per client. Pick one, copy it to the client's config location,
and substitute `<PATH_TO_REIFY>` with the absolute path where you cloned
reify.

## Substitution

Every file here uses the literal placeholder `<PATH_TO_REIFY>`. On the
machine that runs the MCP client, replace it with the absolute path to
your reify checkout.

**Windows (PowerShell), example:**

```
(Get-Content client-config\claude-code.mcp.json) `
  -replace '<PATH_TO_REIFY>', (Get-Location).Path `
  | Set-Content $env:APPDATA\Claude\claude_desktop_config.json
```

**macOS / Linux (bash), example:**

```
sed "s|<PATH_TO_REIFY>|$(pwd)|g" client-config/claude-code.mcp.json \
  > ~/.config/Claude/claude_desktop_config.json
```

The path uses `\\` (double-escaped backslashes) on Windows variants so
the config is still valid JSON. On macOS / Linux you should end up with
forward slashes after substitution.

## Files

| File | Client | Install location (typical) |
|---|---|---|
| `claude-code.mcp.json` | Claude Code / Claude Desktop | `%APPDATA%/Claude/claude_desktop_config.json` (Win) · `~/.config/Claude/claude_desktop_config.json` (macOS/Linux) |
| `claude-code.mcp.published.json` | Claude Code / Desktop using a pre-built `reify-server.exe` | Same as above |
| `cursor.mcp.json` | Cursor | `~/.cursor/mcp.json` or per-workspace `.cursor/mcp.json` |
| `vscode-mcp.json` | VS Code (Claude / Copilot Chat MCP extensions) | workspace `.vscode/mcp.json` |
| `windsurf.mcp.json` | Windsurf | per Windsurf's MCP config path |

## After substitution — sanity check

Once the client has the config, a working install answers:

```
ping                    → { unity_version, project_name, ... }
reify-tool-list         → full tool listing
reify-self-check        → contract test battery, should be pass=all
```

If `ping` fails, the bridge isn't running in Unity — open the project in
Unity and wait for the `[Reify] Bridge listening on http://127.0.0.1:17777/`
log line, then retry.

## Published vs dev config

- `claude-code.mcp.json` expects you to have the .NET SDK installed and
  runs `dotnet run` on the server project source. Good for development.
- `claude-code.mcp.published.json` expects a **pre-built** single-file
  executable from `dotnet publish`. Good for shipping to non-dev users.
  Build with:
  ```
  dotnet publish src/Server/Reify.Server.csproj -c Release -r win-x64 `
    --no-self-contained -p:PublishSingleFile=true
  ```
