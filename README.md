# unity-mcp-personal

> Personal, opinionated fork of the Unity + MCP plugin ecosystem. Name TBD — see
> [`docs/NAMING.md`](docs/NAMING.md).

## What this is

A private project to build a Unity MCP plugin that prioritizes **structured
state over screenshots**. LLMs reason better about JSON, hierarchies, numbers,
and code identifiers than they do about pixels — so the tools this plugin
exposes are designed to feed that strength.

It merges ideas and (eventually) code from two upstream projects:

- [**CoplayDev/unity-mcp**](https://github.com/CoplayDev/unity-mcp) — the
  mature "big" plugin (MIT, Python server, ~30 tools covering scene / asset /
  build / graphics / physics / animation / profiler / probuilder).
- [**IvanMurzak/Unity-MCP**](https://github.com/IvanMurzak/Unity-MCP) — the
  experimental "flexible" plugin (Apache 2.0, C# server, reflection-first, runtime
  support, `script-execute`).

See [`docs/ARCHITECTURE_ANALYSIS.md`](docs/ARCHITECTURE_ANALYSIS.md) for the
side-by-side breakdown that drives the merge.

## Philosophy (one line)

Expose rich structured state for everything — mesh native bounds, material
properties, shader keywords, animator current state, URP pipeline config,
render queue values — so the LLM can grep and reason rather than squint at a
screenshot.

Full thesis: [`docs/PHILOSOPHY.md`](docs/PHILOSOPHY.md).

## Status

Pre-alpha scaffolding. No runtime code in `/src` yet; this repo currently
contains analysis and design documents only. See
[`docs/ROADMAP.md`](docs/ROADMAP.md) for the intended path and
[`docs/SESSION_REPORT.md`](docs/SESSION_REPORT.md) for where the most recent
session left off.

## Repo layout

```
unity-mcp-personal/
├── LICENSE               Apache 2.0 (project license)
├── NOTICE                Attribution to CoplayDev + IvanMurzak upstreams
├── README.md             You are here
├── docs/
│   ├── PHILOSOPHY.md              Core thesis: structured state > screenshots
│   ├── ARCHITECTURE_ANALYSIS.md   Side-by-side of the two upstreams
│   ├── NAMING.md                  Candidate project names (pick one)
│   ├── ROADMAP.md                 Phase A-D plan
│   └── SESSION_REPORT.md          Status at end of each working session
├── scripts/
│   ├── sync-upstream.sh           POSIX: fetch both remotes, list new commits
│   └── sync-upstream.ps1          Windows: same, for PowerShell
├── src/                  Reserved for merged plugin code (currently empty)
└── third_party/
    ├── coplay-LICENSE.md          MIT text from upstream, verbatim
    └── murzak-LICENSE.md          Apache 2.0 text from upstream, verbatim
```

## License

Apache License 2.0 — see [`LICENSE`](LICENSE). Chosen because IvanMurzak's
upstream is Apache 2.0 and MIT (CoplayDev's upstream) is one-way compatible
into Apache 2.0. See [`NOTICE`](NOTICE) for attribution requirements and
[`third_party/`](third_party/) for the preserved upstream license texts.

## Getting started

TBD — no runtime yet. Once merging begins, this section will cover Unity
installation, server launch, and client setup. For now, this repo is a
planning surface.
