# Phase A validation — step-by-step

Everything below is paste-ready. Paths are already resolved for this
machine. Follow steps A → D in order; E is reference; F is for when
something breaks.

---

## Pre-flight checklist (run these first)

| Check                                   | Command                                                                                                 | Expected                                                      |
|-----------------------------------------|---------------------------------------------------------------------------------------------------------|---------------------------------------------------------------|
| .NET 8 SDK installed                    | `dotnet --version`                                                                                      | `8.0.420` (or any 8.x.y)                                      |
| Unity 6 (or 2022.3 LTS+) available      | Open Unity Hub → Installs                                                                               | At least one Editor ≥ 2022.3                                  |
| Claude Code CLI working                 | `claude --version`                                                                                      | Any version output                                            |
| reify server build present              | `Test-Path 'C:\Users\Matte\Desktop\Claude Brain\reify\src\Server\bin\Release\net8.0\reify-server.dll'` | `True`                                                        |
| Port 17777 free                         | `Test-NetConnection -ComputerName 127.0.0.1 -Port 17777 -WarningAction SilentlyContinue \| Select-Object -ExpandProperty TcpTestSucceeded` | `False` *(if True, nothing is listening yet; both False and this command failing = port is free — only flag if something else is on 17777)* |

**Risks to watch for**

- **Domain reloads.** When Unity recompiles scripts, the bridge tears down and restarts. If you run the curl in step B during a compile, you'll get a connection refused. Wait for the Unity Console to go quiet.
- **Port collision.** If another tool is already on 17777, the bridge will log `HttpListenerException` in Unity's Console. Set `REIFY_BRIDGE_PORT` in the Unity launch env *and* in step C's `env` block.
- **Newtonsoft version.** The UPM package depends on `com.unity.nuget.newtonsoft-json` 3.2.1. If the host project pins a different version, Unity will resolve to one of them — usually fine, but watch for `TypeLoadException` in the Console on first load.

---

## Step A — Unity manifest edit

Target file: `C:\dev1\reify-test\Packages\manifest.json`

Paste this line into the `dependencies` object (alongside whatever's already there):

```json
"com.reify.unity": "file:C:/Users/Matte/Desktop/Claude Brain/reify/src/Editor"
```

**Wait — the path has a space.** Unity's package manager handles spaces in
`file:` paths on Windows correctly as of 2022.3+, but if resolution fails
(red error in Console), fall back to a junction without spaces:

```powershell
# Create once, then reference the junction instead of the original folder
New-Item -ItemType Junction -Path 'C:\dev1\reify-src' -Target 'C:\Users\Matte\Desktop\Claude Brain\reify\src\Editor'
```

Then use:
```json
"com.reify.unity": "file:C:/dev1/reify-src"
```

Also make sure Newtonsoft is in the manifest (same `dependencies` block):

```json
"com.unity.nuget.newtonsoft-json": "3.2.1"
```

**Verify:** save `manifest.json`, return focus to Unity. Package manager
re-resolves, compiler runs, and within ~5–15 seconds the Unity Console
should show:

```
[Reify] Bridge listening on http://127.0.0.1:17777/
```

---

## Step B — Curl the bridge from PowerShell

With Unity open and the "Bridge listening" line visible, run:

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

If both return structured data (see step E for shape), the Unity side is
proven green. Claude Code is just an MCP wrapper over this same path.

---

## Step C — Claude Code MCP config

Claude Code stores global MCP servers at `C:\Users\Matte\.claude.json`
under the root-level `"mcpServers"` key. It already contains `context7`,
`sequential-thinking`, `playwright`, `filesystem`, `github`, and more.

**Add this block** as a new sibling inside the existing `"mcpServers": {}`
object (don't replace the whole object — just add one key):

```json
    "reify": {
      "type": "stdio",
      "command": "C:\\Program Files\\dotnet\\dotnet.exe",
      "args": [
        "C:\\Users\\Matte\\Desktop\\Claude Brain\\reify\\src\\Server\\bin\\Release\\net8.0\\reify-server.dll"
      ],
      "env": {
        "REIFY_BRIDGE_PORT": "17777"
      }
    }
```

**Why we invoke the DLL with `dotnet` rather than `dotnet run`:** `dotnet run`
does an implicit restore/build on every launch, which is slow and can write
non-MCP output to stdout and break the protocol handshake. Running the
already-built DLL avoids both problems.

After saving `.claude.json`, **restart Claude Code** so it re-reads the
config and spawns the new MCP server. Then verify with:

```powershell
claude mcp list
```

`reify` should appear in the list. If it shows a red status, jump to
Failure modes → Step C.

---

## Step D — Test prompts for Claude Code

Once `claude mcp list` shows `reify`, open a new Claude Code session in
any directory and paste these prompts one at a time:

1. **Ping:**
   > Call the reify `ping` tool and show me the full response.

2. **Scene list:**
   > Call the reify `scene-list` tool and show me the full response.

Claude should call the MCP tool, receive JSON back, and surface it verbatim
(or summarised). If it refuses or says the tool isn't available,
`claude mcp list` is your diagnostic.

---

## Step E — Expected responses (what green looks like)

### `ping`

```json
{
  "status": "ok",
  "unity_version": "6000.0.30f1",
  "project_name": "reify-test",
  "project_path": "C:/dev1/reify-test",
  "platform": "StandaloneWindows64",
  "is_play_mode": false,
  "is_compiling": false,
  "frame": 1234,
  "read_at_utc": "2026-04-21T15:22:33.1234567Z"
}
```

Wrapped by the bridge envelope, the raw HTTP body looks like:

```json
{ "ok": true, "data": { ...ping payload... } }
```

Through Claude Code's MCP transport, you'll see the `data` payload
(structured JSON), not the envelope.

### `scene-list`

```json
{
  "open_scene_count": 1,
  "scenes": [
    {
      "name": "SampleScene",
      "path": "Assets/Scenes/SampleScene.unity",
      "build_index": 0,
      "is_loaded": true,
      "is_dirty": false,
      "is_active": true,
      "root_count": 2,
      "root_gameobjects": ["Main Camera", "Directional Light"]
    }
  ],
  "read_at_utc": "2026-04-21T15:22:34.4567890Z",
  "frame": 1235
}
```

**Quick sanity check:** dirty a scene in Unity (move a GameObject, don't
save), re-run `scene-list`, and `is_dirty` should flip to `true` and
`frame` / `read_at_utc` should advance.

---

## Step F — Failure modes and diagnostic commands

### Step A failed (Unity never logs "Bridge listening")

| Likely cause                              | Diagnostic                                                                                                        |
|-------------------------------------------|-------------------------------------------------------------------------------------------------------------------|
| Package path typo in `manifest.json`      | Open **Window → Package Manager**; `Reify` should appear under "In Project". If missing, path didn't resolve.     |
| Newtonsoft version conflict               | Unity Console will show a red `TypeLoadException` mentioning `Newtonsoft.Json`. Pin `3.2.1` in `manifest.json`.   |
| Compile error in the package source      | Unity Console shows the red error. `[Reify]` log line only appears after `InitializeOnLoad` runs cleanly.         |

### Step B failed (curl returns connection refused)

| Likely cause                              | Diagnostic                                                                                                        |
|-------------------------------------------|-------------------------------------------------------------------------------------------------------------------|
| Unity mid-compile                          | Watch Console for the spinner to stop, then retry.                                                               |
| Bridge bound to different port             | Search Unity Console for `Bridge listening on http://127.0.0.1:` — use that port.                                |
| Windows firewall                           | Localhost-to-localhost HTTP should never be firewalled, but if corporate policy bites: `New-NetFirewallRule -DisplayName 'reify-local' -Direction Inbound -LocalAddress 127.0.0.1 -LocalPort 17777 -Action Allow -Protocol TCP`. |

### Step B returned an error envelope

| Error `code`                              | Meaning                                                                                                            |
|-------------------------------------------|--------------------------------------------------------------------------------------------------------------------|
| `UNKNOWN_TOOL`                            | You misspelled the tool name. Only `ping` and `scene-list` exist.                                                  |
| `TOOL_EXCEPTION`                          | Handler threw. Unity Console has the stack trace.                                                                  |
| `NOT_FOUND`                               | Wrong URL path or method. Must be `POST /tool`.                                                                    |

### Step C failed (`claude mcp list` doesn't show `reify`, or shows it red)

| Likely cause                              | Diagnostic                                                                                                        |
|-------------------------------------------|-------------------------------------------------------------------------------------------------------------------|
| JSON syntax error in `.claude.json`       | `Get-Content 'C:\Users\Matte\.claude.json' -Raw \| ConvertFrom-Json` — non-zero exit = bad JSON.                   |
| `dotnet.exe` path wrong                   | `Get-Command dotnet \| Select-Object -ExpandProperty Source` — should match the `command` in the block.           |
| Server DLL missing                        | `Test-Path 'C:\Users\Matte\Desktop\Claude Brain\reify\src\Server\bin\Release\net8.0\reify-server.dll'`.            |
| Server crashes on start                   | Launch manually: `& 'C:\Program Files\dotnet\dotnet.exe' 'C:\Users\Matte\Desktop\Claude Brain\reify\src\Server\bin\Release\net8.0\reify-server.dll'` — should wait on stdin, not exit. Ctrl+C to stop. |

### Step D failed (Claude Code says tool unavailable or hangs)

| Likely cause                              | Diagnostic                                                                                                        |
|-------------------------------------------|-------------------------------------------------------------------------------------------------------------------|
| Claude Code not restarted after config edit | Fully quit and relaunch the Claude Code process; `claude mcp list` should then show `reify`.                    |
| Unity Editor closed                         | Response will carry `UNITY_UNREACHABLE`. Open Unity with the test project, wait for "Bridge listening", retry.   |
| 30 s timeout                                | Unity main thread blocked (modal dialog, infinite import). Dismiss, retry.                                       |

---

## Quick reference card

| Thing                  | Value                                                                                                       |
|------------------------|-------------------------------------------------------------------------------------------------------------|
| Repo root              | `C:\Users\Matte\Desktop\Claude Brain\reify`                                                                 |
| UPM package            | `C:\Users\Matte\Desktop\Claude Brain\reify\src\Editor` (contains `package.json`)                            |
| Server DLL             | `C:\Users\Matte\Desktop\Claude Brain\reify\src\Server\bin\Release\net8.0\reify-server.dll`                  |
| `dotnet.exe`           | `C:\Program Files\dotnet\dotnet.exe`                                                                        |
| Bridge transport       | HTTP POST `http://127.0.0.1:17777/tool`, body `{"tool":"<name>","args":<json or null>}`                     |
| Bridge port            | `17777` (override with env var `REIFY_BRIDGE_PORT`)                                                         |
| Claude Code MCP config | `C:\Users\Matte\.claude.json`, root-level `"mcpServers"` object                                             |
| Tools available        | `ping`, `scene-list`                                                                                        |
