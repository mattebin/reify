# Handoff — clean redo from the keyboard

One document. Read it, run the PowerShell block, end up with one clean
folder containing the scaffold and a proper `.git`.

---

## 1. Current state (both folders, as of handoff)

### `C:\Users\Matte\Documents\unity-mcp-personal\` — the fallback attempt, should not exist

```
Documents\unity-mcp-personal\
├── .git\                            ← CRUFT (broken: config.lock stuck)
│   ├── HEAD
│   ├── config
│   ├── config.lock                      ← unlink-blocked, kept git from initializing
│   ├── description
│   ├── hooks\ (15 *.sample files)
│   └── info\exclude
├── .gitignore                       ← legit scaffold (early copy)
├── LICENSE                          ← legit scaffold
├── NOTICE                           ← legit scaffold
├── README.md                        ← legit scaffold
├── docs\
│   ├── ARCHITECTURE_ANALYSIS.md     ← legit
│   ├── NAMING.md                    ← legit
│   ├── PHILOSOPHY.md                ← legit
│   ├── ROADMAP.md                   ← legit
│   └── SESSION_REPORT.md            ← legit, but SUPERSEDED by the copy in second-brain
├── scripts\
│   ├── sync-upstream.ps1            ← legit
│   └── sync-upstream.sh             ← legit
├── src\.gitkeep                     ← legit
├── testfile.txt                     ← CRUFT (session-1 write probe, 10 bytes)
└── third_party\
    ├── coplay-LICENSE.md            ← legit
    └── murzak-LICENSE.md            ← legit
```

**Verdict:** nothing unique lives here that isn't also in second-brain. Nuke the whole thing.

### `C:\Users\Matte\second-brain\unity-mcp-personal\` — the real location, scaffold intact + two cruft items

```
second-brain\unity-mcp-personal\
├── .gitignore                       ← legit scaffold
├── .gittest\                        ← CRUFT (session-2 git-init probe; full .git internals left behind)
│   └── .git\ (HEAD, config, config.lock, description, hooks\*, info\exclude)
├── LICENSE                          ← legit
├── NOTICE                           ← legit
├── README.md                        ← legit
├── docs\
│   ├── ARCHITECTURE_ANALYSIS.md     ← legit
│   ├── HANDOFF.md                   ← THIS FILE, legit
│   ├── NAMING.md                    ← legit
│   ├── PHILOSOPHY.md                ← legit
│   ├── ROADMAP.md                   ← legit
│   └── SESSION_REPORT.md            ← legit (current version)
├── scripts\
│   ├── sync-upstream.ps1            ← legit
│   └── sync-upstream.sh             ← legit
├── src\.gitkeep                     ← legit
├── test.txt                         ← CRUFT (session-2 write probe, 5 bytes)
└── third_party\
    ├── coplay-LICENSE.md            ← legit
    └── murzak-LICENSE.md            ← legit
```

**Verdict:** every file you actually want is here. The redo script backs up the
legit ones, nukes both folders, and restores clean.

---

## 2. What happened

**Session 1** (intended target: `C:\Users\Matte\claude-brain\`, which didn't
exist): fell back to `C:\Users\Matte\Documents\unity-mcp-personal\`, wrote the
full scaffold there, tried `git init` — but the Cowork mount that exposes
Windows to the Linux sandbox allows *create* and *overwrite* while blocking
*unlink*. `git init` writes `.git/config` via the standard write-lock-rename
dance, which requires unlinking the lock, which the mount refused. The init
left a broken `.git/` behind that the sandbox can't clean up (same unlink
block). All real git work (init, remotes, fetches, commits) happened inside a
sandbox-side scratch repo that is now gone.

**Session 2** (relocation to `C:\Users\Matte\second-brain\unity-mcp-personal\`,
the actual vault path): copied the clean sandbox scaffold to the new location
and re-tested whether `git init` would work there — same mount, same unlink
block, same failure, plus a stray `.gittest\` probe directory and a `test.txt`
write probe that also couldn't be removed. The old `Documents\` folder stayed
put because the mount doesn't allow its removal either. So the handoff state
is: two folders on disk, one in the wrong place and one with two pieces of
cruft, neither with a working `.git`. From a keyboard with unrestricted
filesystem access, this is a five-second fix. Hence this doc.

---

## 3. Clean redo — one paste-ready PowerShell block

Paste this into a PowerShell window. It backs up the legitimate scaffold from
the current second-brain location to `$env:TEMP\unity-mcp-backup`, nukes both
folders, recreates `second-brain\unity-mcp-personal\`, restores the backup,
deletes the backup, and runs the full git init + remotes + fetch + commit.

Safe to re-run: if something fails partway, fix the complaint and run again.
Uses `$ErrorActionPreference = 'Stop'` so it halts on the first real error.

```powershell
$ErrorActionPreference = 'Stop'

$OldPath   = Join-Path $env:USERPROFILE 'Documents\unity-mcp-personal'
$NewPath   = Join-Path $env:USERPROFILE 'second-brain\unity-mcp-personal'
$Backup    = Join-Path $env:TEMP 'unity-mcp-backup'
$Email     = 'nejjerxd@gmail.com'
$User      = 'king'

# --- Sanity: verify the scaffold we're about to back up is there ---
if (-not (Test-Path (Join-Path $NewPath 'README.md'))) {
    throw "Expected scaffold not found at $NewPath. Aborting before we nuke anything."
}

# --- 1. Backup legitimate content only (exclude .gittest, test.txt) ---
Write-Host "Backing up scaffold to $Backup ..."
if (Test-Path $Backup) { Remove-Item -Recurse -Force $Backup }
New-Item -ItemType Directory -Path $Backup | Out-Null

$Items = @(
    '.gitignore',
    'LICENSE',
    'NOTICE',
    'README.md',
    'docs',
    'scripts',
    'src',
    'third_party'
)
foreach ($item in $Items) {
    $source = Join-Path $NewPath $item
    if (Test-Path $source) {
        Copy-Item -Recurse -Force -Path $source -Destination $Backup
    } else {
        Write-Warning "  Missing (skipped): $item"
    }
}

# --- 2. Nuke both locations ---
Write-Host "Removing $OldPath ..."
if (Test-Path $OldPath) { Remove-Item -Recurse -Force $OldPath }

Write-Host "Removing $NewPath ..."
if (Test-Path $NewPath) { Remove-Item -Recurse -Force $NewPath }

# --- 3. Recreate destination, restore from backup ---
Write-Host "Recreating $NewPath ..."
New-Item -ItemType Directory -Path $NewPath | Out-Null
Copy-Item -Recurse -Force -Path (Join-Path $Backup '*') -Destination $NewPath

# --- 4. Clean up backup ---
Remove-Item -Recurse -Force $Backup

# --- 5. Git init + remotes + fetch + initial commit ---
Set-Location $NewPath

git init -b main
git config user.email $Email
git config user.name  $User

git remote add coplay https://github.com/CoplayDev/unity-mcp.git
git remote add murzak https://github.com/IvanMurzak/Unity-MCP.git

git fetch coplay --depth=1
git fetch murzak --depth=1

git add .
git commit -m "chore: initial setup with upstream remotes and licensing + docs"

Write-Host ""
Write-Host "Done. Repo at $NewPath"
git log --oneline
git remote -v
```

**Expected end state:**

- `C:\Users\Matte\Documents\unity-mcp-personal\` — gone.
- `C:\Users\Matte\second-brain\unity-mcp-personal\` — one folder, one clean
  `.git\`, two upstream remotes (`coplay`, `murzak`) fetched at `--depth=1`,
  one initial commit on `main`, scaffold exactly as listed in §4.
- No `.gittest\`, no `test.txt`, no `testfile.txt`, no stray lock files.
- `$env:TEMP\unity-mcp-backup` — gone (deleted at end of script).

**If the initial commit message bothers you** (it bundles the original two
sandbox commits into one), replace the final two git lines with:

```powershell
git add .gitignore LICENSE NOTICE README.md docs\PHILOSOPHY.md docs\ARCHITECTURE_ANALYSIS.md docs\NAMING.md docs\ROADMAP.md scripts\ src\ third_party\
git commit -m "chore: initial setup with upstream remotes and licensing"
git add docs\SESSION_REPORT.md docs\HANDOFF.md
git commit -m "docs: session report and handoff"
```

---

## 4. File manifest — verify nothing's missing

After the redo, `git ls-files` should print these 15 paths and only these 15 paths.

| Path                                   | Bytes  | Purpose                                                           |
|----------------------------------------|--------|-------------------------------------------------------------------|
| `.gitignore`                           | 416    | OS / editor / Python / Node / Unity ignore patterns               |
| `LICENSE`                              | 12,357 | Apache 2.0 full text + project copyright + upstream notice        |
| `NOTICE`                               | 3,338  | Attribution rules, per-file header template for ported code       |
| `README.md`                            | 3,224  | Project description placeholder + doc index + philosophy summary  |
| `docs/PHILOSOPHY.md`                   | 7,068  | Core thesis: structured state over screenshots                    |
| `docs/ARCHITECTURE_ANALYSIS.md`        | 32,740 | Side-by-side CoplayDev vs IvanMurzak + overlap matrix + gap analysis |
| `docs/NAMING.md`                       | 6,198  | 12 project-name candidates with trade-offs (nothing picked)       |
| `docs/ROADMAP.md`                      | 8,289  | Phase A-D plan with exit conditions and open questions            |
| `docs/SESSION_REPORT.md`               | 10,022 | What got built session 1+2, decisions owed, next-session priorities |
| `docs/HANDOFF.md`                      | ~10 KB | This file                                                         |
| `scripts/sync-upstream.sh`             | 2,400  | bash: fetch coplay+murzak, diff since last sync, persist state    |
| `scripts/sync-upstream.ps1`            | 2,608  | PowerShell equivalent of the sync script                          |
| `src/.gitkeep`                         | 0      | Placeholder; no code ported yet                                   |
| `third_party/coplay-LICENSE.md`        | 1,270  | MIT text from CoplayDev/unity-mcp, verbatim                       |
| `third_party/murzak-LICENSE.md`        | 10,418 | Apache 2.0 text from IvanMurzak/Unity-MCP, verbatim               |

Quick verification one-liner:

```powershell
git ls-files | Measure-Object -Line   # expect Lines: 15
```

And a shape check:

```powershell
Get-ChildItem -Recurse -File | Where-Object { $_.FullName -notmatch '\\\.git\\' } |
  Sort-Object FullName |
  ForEach-Object { "{0,8}  {1}" -f $_.Length, $_.FullName.Substring($PWD.Path.Length + 1) }
```

Sizes can drift a few bytes with line-ending conversions on Windows; order of
magnitude is what matters.
