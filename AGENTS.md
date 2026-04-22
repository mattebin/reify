# reify agent instructions

If you are an LLM operating in this repo, treat `reify` as a structured-state
Unity backend, not as a screenshot-first Unity assistant.

## Core operating loop

1. Start by orienting:
   - read [README.md](README.md)
   - read [docs/PHILOSOPHY.md](docs/PHILOSOPHY.md)
   - read [docs/AGENT_PLAYBOOKS.md](docs/AGENT_PLAYBOOKS.md) if you want
     client-specific guidance
2. Confirm the stack is alive before deeper work:
   - `ping`
   - `reify-version`
   - `reify-tool-list`
3. Prefer structured reads before writes.
4. Prefer `batch-execute` for related evidence collection.
5. Preserve `read_at_utc`, `frame`, and stable identifiers from tool output.
6. Reject ambiguous identity instead of guessing.
7. After code/package edits, assume Unity may be stale until it regains focus
   or `Assets/Refresh` runs. Do not chase ghost errors before a refresh.
8. Use `structured-screenshot` only if structured-state cannot answer the
   question.
9. Use `reflection-method-call` only as an explicit opt-in escape hatch.
10. After mutations, verify by reading back through the same Unity code path.

## Evidence rules

- Prefer tool output over assumptions.
- Prefer scene-qualified and instance-id-based identity when available.
- Treat dirty scenes, compile state, and domain reload state as first-class
  guards, not incidental noise.
- If a write tool succeeds but post-state is not yet trustworthy, say so.

## High-value discovery tools

- `reify-tool-list`
- `reify-version`
- `batch-execute`
- `domain-reload-status`
- `persistence-status`
- `scene-query`
- `tests-list`
- `tests-status`

## Current validation nuance

This repo is often edited live through a local Unity file package reference.
If a just-edited tool still behaves like old code, refresh/re-focus Unity
before concluding the patch failed.

