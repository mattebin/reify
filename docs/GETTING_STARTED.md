# Getting started

This is the bootstrap path for getting `reify` talking to a Unity Editor and a
Claude Code MCP client.

The local `reify` worktree now contains many more tools than this guide uses.
This document intentionally keeps the smoke test cheap by using `ping` and
`scene-list` as the first proof that the bridge is alive.

## Prerequisites

- .NET 8 SDK
- Unity Editor 2022.3+ (Unity `6000.4.3f1` is known-good locally)
- Claude Code CLI
- Git

Default bridge port: `17777`

## Step 1 — Build the server

```powershell
cd "C:\Users\Matte\Desktop\Claude Brain\reify\src"
dotnet restore
dotnet build -c Release
```

Expected output:

- `src\Server\bin\Release\net8.0\reify-server.dll`

Optional published executable:

```powershell
dotnet publish Server\Reify.Server.csproj -c Release --no-self-contained
```

## Step 2 — Install the Unity package into a project

Add the local package to a Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.reify.unity": "file:<PATH_TO_REIFY>/src/Editor",
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

After Unity recompiles, the Console should show:

```text
[Reify] Bridge listening on http://127.0.0.1:17777/
```

## Step 3 — Smoke test the bridge directly

```powershell
Invoke-RestMethod -Method Post `
  -Uri 'http://127.0.0.1:17777/tool' `
  -ContentType 'application/json' `
  -Body '{"tool":"ping"}'
```

Then:

```powershell
Invoke-RestMethod -Method Post `
  -Uri 'http://127.0.0.1:17777/tool' `
  -ContentType 'application/json' `
  -Body '{"tool":"scene-list"}'
```

If both succeed, the Unity-side bridge is healthy.

## Step 4 — Wire up Claude Code

Two config examples live in `client-config/`:

- `claude-code.mcp.json`: development config using `dotnet run`
- `claude-code.mcp.published.json`: published executable config

You can also register the development server directly:

```powershell
claude mcp add reify `
  --command dotnet `
  --args 'run --project "C:\Users\Matte\Desktop\Claude Brain\reify\src\Server\Reify.Server.csproj" --no-launch-profile --' `
  --env REIFY_BRIDGE_PORT=17777
```

Verify:

```powershell
claude mcp list
```

## Step 5 — Use it from Claude Code

Good first prompts:

- `Use the reify ping tool.`
- `List the open scenes in Unity.`
- `Call reify persistence-status.`

## Troubleshooting

- `UNITY_UNREACHABLE`: Unity is closed, still compiling, or the package is not installed
- bind error on `17777`: choose a different `REIFY_BRIDGE_PORT` and use it on both sides
- timeout: Unity main thread is blocked, a modal dialog is open, or the editor is mid-refresh

## What a successful bootstrap looks like

- `ping` returns Unity version, project info, timestamp, and frame
- `scene-list` returns the open scenes with load / dirty / active metadata
- Claude Code can call the server through MCP without manual JSON copying

That proves the transport path. The local tool surface extends much further
than this bootstrap check.
