# Getting started

Recipe for going from a clean machine to "Claude Code can `ping` your Unity
Editor". If you are future-me reading this after a week, follow the steps
in order — each one tells you the verification command to run before
moving to the next.

---

## Prerequisites

| Requirement              | Verify with                       | If missing                                                              |
|--------------------------|-----------------------------------|-------------------------------------------------------------------------|
| .NET 8 SDK               | `dotnet --list-sdks`              | `winget install Microsoft.DotNet.SDK.8`                                 |
| Unity Editor 2022.3 LTS+ | open Unity Hub, check installs    | install from Unity Hub                                                  |
| Claude Code CLI          | `claude --version`                | follow https://docs.claude.com/claude-code/getting-started               |
| Git                      | `git --version`                   | `winget install Git.Git`                                                |

The port `17777` must be free on localhost. Pick another by setting
`REIFY_BRIDGE_PORT` in both the Unity env and the Claude Code MCP config
if it is not.

---

## Step 1 — Build the Reify server

```powershell
cd "C:\Users\Matte\Desktop\Claude Brain\reify\src"
dotnet restore
dotnet build -c Release
```

**Verify:** `dotnet build -c Release` ends with `Build succeeded.` and no
errors. You should see `bin\Release\net8.0\reify-server.dll` produced under
`src\Server\`.

For a standalone `.exe`:

```powershell
dotnet publish Server\Reify.Server.csproj -c Release --no-self-contained
```

That writes `bin\Release\net8.0\publish\reify-server.exe`. Use
`client-config/claude-code.mcp.published.json` if you want to point Claude
Code at that `.exe` directly instead of invoking `dotnet run`.

---

## Step 2 — Install the Unity package into a project

Open any Unity project (a blank 3D URP project is fine for smoke-testing).
In `Packages/manifest.json` add a local package reference to the Reify
Editor source folder:

```json
{
  "dependencies": {
    "com.reify.unity": "file:C:/Users/Matte/Desktop/Claude Brain/reify/src/Editor",
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

(Forward slashes in the path — Unity's package manifest parses
backslashes as escape characters.)

Save the file. Unity will re-import and compile the package.

**Verify:** open the Unity Console. Within a few seconds of compile
finishing you should see:

```
[Reify] Bridge listening on http://127.0.0.1:17777/
```

If you see a bind error instead, the port is in use — set
`REIFY_BRIDGE_PORT` to a free port in both Unity's launch environment
and in the Claude Code MCP config below, then restart.

**Manual smoke test (no Claude Code yet):**

```powershell
curl -Method Post `
     -Uri http://127.0.0.1:17777/tool `
     -ContentType "application/json" `
     -Body '{"tool":"ping"}'
```

Expected response (pretty-printed):

```json
{
  "ok": true,
  "data": {
    "status": "ok",
    "unity_version": "6000.0.30f1",
    "project_name": "BlankTest",
    "project_path": "C:/Users/Matte/Unity/BlankTest",
    "platform": "StandaloneWindows64",
    "is_play_mode": false,
    "is_compiling": false,
    "frame": 12345,
    "read_at_utc": "2026-04-21T14:40:00.0000000Z"
  }
}
```

If this works, the Unity side is done. Claude Code is the next step — and
it does nothing fundamentally different from this `curl`, it just wraps
MCP protocol around it.

---

## Step 3 — Wire up Claude Code

Copy `client-config/claude-code.mcp.json` into your Claude Code MCP config.

Or register the server directly:

```powershell
claude mcp add reify `
  --command dotnet `
  --args 'run --project "C:\Users\Matte\Desktop\Claude Brain\reify\src\Server\Reify.Server.csproj" --no-launch-profile --' `
  --env REIFY_BRIDGE_PORT=17777
```

**Verify:**

```powershell
claude mcp list
```

`reify` should appear with status `connected` (or at least `ready`).

---

## Step 4 — Use it from Claude Code

Open a Claude Code session. Both of these should work:

| Ask                            | Expected                                                                            |
|--------------------------------|-------------------------------------------------------------------------------------|
| "Use the reify ping tool."     | Structured response with Unity version, project name, frame counter, timestamp.     |
| "List the open scenes in Unity." | Array of scene objects with paths, names, dirty flags, root GameObject names.     |

Both tools return timestamped JSON — if you call `scene-list`, dirty a scene
in Unity, and call it again, the `is_dirty` field should flip and the
`read_at_utc` timestamp should advance.

---

## Troubleshooting

| Symptom                                                          | Cause                                              | Fix                                                                                                           |
|------------------------------------------------------------------|----------------------------------------------------|---------------------------------------------------------------------------------------------------------------|
| Claude Code says `UNITY_UNREACHABLE`                             | Unity Editor is closed or package not installed    | Open Unity with the project that has `com.reify.unity` in `manifest.json`. Watch for the `[Reify] Bridge listening` log line. |
| `Bridge listening` never appears                                 | Package failed to compile                          | Open the Console, look for red errors. Most likely culprit: Newtonsoft missing.                               |
| `Bind failed` / `HttpListenerException`                          | Port 17777 is in use                               | Set `REIFY_BRIDGE_PORT` to something free in Unity's launch env and Claude Code's config; restart both.        |
| `scene-list` returns empty                                       | No scenes open in Editor (rare)                    | Open any scene; rerun.                                                                                        |
| Tools hang for 30 s then time out                                | Main thread blocked (compiling, modal dialog open) | Wait for compile to finish; dismiss any modal.                                                                |

---

## What "works" means in Phase A

- `ping` returns a populated response with non-empty `unity_version` and
  `project_name`, and the `read_at_utc` field advances between calls.
- `scene-list` returns the scenes currently open in the Editor, with
  correct `is_active`, `is_loaded`, `is_dirty` flags and the root GameObject
  names of each.
- Both are callable from Claude Code as MCP tools without any manual
  copying of JSON.

Anything beyond that is Phase B.
