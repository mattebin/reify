# Session report — 2026-04-20 (updated after relocation)

Duration: one session plus a follow-up relocation.
Scope: analysis + scaffold only, no code ported from upstream into `/src/`.

---

## Ambiguities resolved + mount limitations

### Project location

**Originally requested:** `C:\Users\Matte\claude-brain\unity-mcp-personal`
(path didn't exist — `claude-brain` was a misremembered name).
**Corrected to:** `C:\Users\Matte\second-brain\unity-mcp-personal` (inside
the Obsidian vault). Scaffold content files are now at that path.

The initial fallback attempt at `C:\Users\Matte\Documents\unity-mcp-personal`
is still on disk and needs to be deleted manually — see cleanup steps below.

### Windows mount limitation: git cannot run on the final path

The Cowork mount exposes the Windows filesystem with **create/overwrite
allowed but delete/unlink blocked**. `git init` writes `.git/config` via
`config.lock` + rename-over-old, and `git add`/`git commit` rely on similar
atomic file replacements. Every git operation fails with
`unable to unlink ... Operation not permitted`. As a result:

- All git work (init, remote add, fetch, the two commits) happened **inside
  a sandbox** during the session. That sandbox is ephemeral — its `.git/`
  directory does not exist on your Windows filesystem.
- The **content files** (README, LICENSE, NOTICE, docs/, scripts/,
  third_party/, src/, .gitignore) have been copied to
  `C:\Users\Matte\second-brain\unity-mcp-personal\`. These match the
  sandbox state exactly.
- A stray `.gittest/` directory and a `test.txt` file, both artifacts of
  the session testing whether the mount would accept git operations, exist
  at the new location and need to be removed by you on Windows.
- The entire old `Documents\unity-mcp-personal\` folder (including a broken
  `.git/`, a `testfile.txt`, and the scaffolding) could not be removed
  from the sandbox side.

**Action for you — PowerShell commands to finish the job:**

```powershell
# Remove the obsolete attempt in Documents (entire folder)
Remove-Item -Recurse -Force "$env:USERPROFILE\Documents\unity-mcp-personal"

# Move to the real location
cd "$env:USERPROFILE\second-brain\unity-mcp-personal"

# Clean up session cruft
Remove-Item -Recurse -Force .gittest
Remove-Item test.txt

# Initialize git with the intended history
git init -b main
git config user.email "nejjerxd@gmail.com"
git config user.name "king"
git remote add coplay https://github.com/CoplayDev/unity-mcp.git
git remote add murzak https://github.com/IvanMurzak/Unity-MCP.git
git fetch coplay --depth=1
git fetch murzak --depth=1

# Replay the sandbox history as a single initial commit. If you'd rather
# have two separate commits (matching the sandbox history: "chore: initial
# setup..." then "docs: session report..."), stage and commit the docs/
# SESSION_REPORT.md last instead of adding everything at once.
git add .
git commit -m "chore: initial setup with upstream remotes and licensing + session report"
```

No remote push is configured (private project, per hard rules).

---

## What was created

Top-level:

- `README.md` — project-description placeholder, philosophy summary, pointer
  to the doc set, attribution, getting-started stub.
- `LICENSE` — Apache License 2.0 verbatim, followed by our project
  copyright line and a note about the upstream licenses.
- `NOTICE` — attribution rules and the per-file header template required
  when code is ported from either upstream.
- `.gitignore` — standard OS/editor/Python/Node/Unity ignore.
- `src/.gitkeep` — placeholder; no code ported yet.

`docs/`:

- [`PHILOSOPHY.md`](PHILOSOPHY.md) — the "structured state over screenshots"
  thesis in a ~200-line document. Core rules for tool design, examples of
  the friction that motivates the project, and a list of philosophy
  features to build.
- [`ARCHITECTURE_ANALYSIS.md`](ARCHITECTURE_ANALYSIS.md) — the big document.
  Side-by-side of CoplayDev and IvanMurzak: repo structures, tool lists,
  transport layers, config locations, architectural choices, pain points
  from both issue trackers, preserve-vs-replace matrices, a tool-level
  overlap matrix, and an 11-gap analysis against the structured-state
  philosophy.
- [`NAMING.md`](NAMING.md) — 12 candidate names with trade-offs and a
  shortlist of four favorites. Nothing picked; decision prompts at the
  bottom.
- [`ROADMAP.md`](ROADMAP.md) — Phase A (foundations), B (core tool ports),
  C (philosophy features), D (polish). Each phase has exit conditions and
  an open questions list.

`scripts/`:

- [`sync-upstream.sh`](../scripts/sync-upstream.sh) — bash; fetches both
  remotes, reports new commits since last sync, persists state in
  `.upstream-sync-state`.
- [`sync-upstream.ps1`](../scripts/sync-upstream.ps1) — PowerShell
  equivalent for Windows-native use. Same behavior.

`third_party/`:

- [`coplay-LICENSE.md`](../third_party/coplay-LICENSE.md) — MIT text from
  CoplayDev/unity-mcp, verbatim, with a small header metadata block.
- [`murzak-LICENSE.md`](../third_party/murzak-LICENSE.md) — Apache 2.0 text
  from IvanMurzak/Unity-MCP, verbatim, with a small header metadata block.

---

## Decisions still owed (you, not me)

Listed in rough order of how much downstream work they block:

### 1. Project name

Pick from [`NAMING.md`](NAMING.md) or invent another. Blocks: package name,
namespace, repo rename, README headline. My shortlist from the doc:
`reify`, `probe`, `scenic`, `ontic`. None are picked.

### 2. Server language

C# (match Murzak) or Python (match Coplay) or split. See
[`ROADMAP.md`](ROADMAP.md) Phase A step 2. Recommendation in the doc is
C#, but I deliberately didn't commit — this is a load-bearing decision
for the rest of the project.

### 3. Editor-only or editor + runtime

Murzak's runtime plugin is real value; it's also an additional assembly and
a doubled API surface. Recommendation: editor-only to start, add runtime in
Phase D if you want it. Not decided.

### 4. Philosophy refinement

[`PHILOSOPHY.md`](PHILOSOPHY.md) is a draft. Specifically:

- Is the "theme-aware scene state" gap (Gap 4 in
  [`ARCHITECTURE_ANALYSIS.md`](ARCHITECTURE_ANALYSIS.md)) in scope, or does
  that belong in a separate project? It's the most speculative item — the
  others are all generally applicable.
- How aggressive about deprecating screenshots? The current draft says
  "opt-in escape hatch." Some users will object; worth deciding how hard
  to hold the line.
- Any friction points I didn't name? I pattern-matched from generally
  common Unity+LLM pain. You may have specific pains I haven't seen.

### 5. Roadmap priorities

[`ROADMAP.md`](ROADMAP.md) phases are defensible but opinionated. Specifically:

- Phase B porting order puts material/shader redesign (item 5) before
  animator (item 7). Swap if you personally hit animator pain more often
  than material pain.
- `script-execute` is gated behind opt-in in Phase B. Switchable.
- Prompts-as-first-class is deferred to Phase D. Could be earlier.

### 6. Public-release intent

Is this "my personal Unity plugin I maintain for myself" or "a public
OSS project I might eventually ship"? The polish bar in Phase D is very
different between the two. Affects license/attribution strictness,
docs-site investment, and whether we care about Windows/Mac/Linux parity.

### 7. Which MCP clients matter

Phase A ships Claude Code first. Others (Cursor, VS Code Copilot,
Windsurf, Cline, etc.) are in Phase D. If one of those is your primary
client, reorder.

### 8. `script-execute` default state

On-by-default (convenient) or off-by-default (auditable). I lean off.

---

## 3-5 highest-leverage things for the NEXT session

Ordered by expected payoff per hour of your time:

### 1. Name the project.

Everything downstream — package id, namespace, repo renames, README
headline — waits on this. It's a 30-minute decision if you let it be.

### 2. Pick server language + write a smoke-test tool end-to-end.

This is Phase A step 2 + step 8. The goal of the session: a `ping` tool
that round-trips from Claude Code through the MCP server into Unity and
back, returning a structured JSON response. Once that path is proven, the
rest of Phase B is mechanical porting.

Estimate: 2-4 hours if the server-language choice is clear going in.
Possibly longer if you stumble on Unity domain reload + server lifecycle
(this is where Coplay's #891 lives — worth designing resilience in
early, not patching later).

### 3. Build `mesh-native-bounds` as the first "philosophy" tool.

This is the single most demonstrable differentiator vs upstream. It's
cheap to implement (read `AssetImporter` + `MeshFilter.sharedMesh.bounds`)
and it solves an extremely common LLM-Unity friction in one tool. Would
pair well with the smoke test — prove the philosophy pitch with one tool.

Estimate: 1-2 hours after Phase A is done.

### 4. Decide the tool-naming convention and write it down.

Something like `ADR-001.md` in `/docs/`. The convention affects every
future tool file, and fighting it later is painful. Candidate rules:

- kebab-case IDs, grouped by noun (`mesh-native-bounds`, not
  `get-mesh-bounds` and not `gameobject-mesh-bounds`).
- Nouns over verbs for reads. Verbs allowed for writes.
- One action per file (Murzak's partial-class pattern).
- Every read returns a `read_at` or `frame` timestamp for staleness
  reasoning.

Should be a 30-minute write-up that you can point at for every PR.

### 5. Drain the open-questions list in `ROADMAP.md`.

C# vs Python. Runtime scope. `script-execute` defaults. Upstream-sync
strategy. Each of these is a 10-minute answer once, saves hours later.

---

## Meta-note

Everything here is reversible. The scaffold is docs and scripts, not code.
Re-reading this report and the docs before the next session is probably
worth the 15 minutes — the two most useful prior-art references for the
merge thesis are in [`ARCHITECTURE_ANALYSIS.md`](ARCHITECTURE_ANALYSIS.md)
(§ overlap matrix and § gap analysis).
