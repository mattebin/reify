# Pre-Phase-B Research Notes

## Unity MCP plugin landscape (as of 2026-04-21)

8+ Unity MCP plugins exist. reify takes an opinionated bidirectional-
cherry-pick relationship with the two strongest:

- **CoplayDev/unity-mcp** (MIT, 8.3k stars, actively maintained, weekly releases)
- **IvanMurzak/Unity-MCP** (Apache 2.0, ~300 stars, runtime + reflection + probuilder)

## Studied but NOT merged

### AnkleBreaker-Studio/unity-mcp-server
- 200+ tools across 30+ categories — legitimately comprehensive coverage.
- License: "MIT with Attribution Requirement" (requires "Made with
  AnkleBreaker MCP" branding on any product built with it). Not pure MIT,
  incompatible with clean-identity fork.
- Community: 18 stars, 1 fork, 2 contributors (one is "claude" GitHub user).
  Small.
- **Decision:** Category list copied to reify's ROADMAP.md as reference only.
  No code imported. Implementations built fresh against Unity API.

### youichi-uda/unity-mcp-pro-plugin
- 147 tools, 24 categories, MIT license (compatible).
- But: ~90% tool overlap with CoplayDev. Adding it would triple porting
  burden with no coverage gain.
- Has valuable polish patterns (Undo integration, port scanning, exponential
  backoff, domain reload safety).
- **Decision:** Patterns documented in `/docs/PHASE_D_NOTES.md` for fresh
  reimplementation. No code imported.

### Others surveyed
- **CoderGamester/mcp-unity** (Node.js) — different language, would force
  second stack.
- **MiAO-MCP-for-Unity** — fork of IvanMurzak, no additional value.
- **jackwrichards/UnityMCP**, **zabaglione/mcp-server-unity**,
  **nowsprinting/mcp-extension-unity** — smaller scope, not worth merging.

## Non-Unity MCPs considered

Filesystem, Git, GitHub MCPs exist and are excellent, but they don't belong
inside reify. Users configure them alongside reify, not within. reify stays
focused on Unity-specific structured state.

## Conclusion

Two upstreams remain: **CoplayDev + IvanMurzak**. **AnkleBreaker** as
roadmap reference. **youichi-uda** as Phase D polish patterns. Focus is
on reify's differentiator — structured-state-first tool design — not
breadth competition.
