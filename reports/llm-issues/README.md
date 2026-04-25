# LLM-reported issues

This folder is where an LLM using reify writes a structured bug or
unexpected-behavior report when it encounters something worth fixing.

## Flow

```
LLM hits a problem
   │
   ▼
 calls  reify-log-issue  with { model_name, issue_title, effort, ... }
   │
   ▼
 file written to  reports/llm-issues/pending/<timestamp>-<slug>.md
   │
   ▼
 user runs:   python scripts/review-llm-issues.py
   │
   ▼
 for each pending file:  show content, prompt y/n/skip/delete
     - y      → gh issue create --label llm-reported, move to submitted/
     - n      → move to dismissed/
     - delete → remove file
     - skip   → leave in pending/
```

## Why not open GitHub issues directly

Every LLM would file duplicates, near-duplicates, and false-positives.
The `user-in-the-loop` gate means the GitHub issue tracker stays
signal, not noise. It also lets the user rewrite the title / body
before submission without fighting an LLM's prose.

## Folder structure

```
reports/llm-issues/
├── README.md        — this file
├── TEMPLATE.md      — canonical shape for a report
├── pending/         — reports written by LLMs, awaiting user review
├── submitted/       — filed to GitHub; each file records the issue URL
└── dismissed/       — reviewed + rejected by user
```

`pending/` + `submitted/` + `dismissed/` are created on first use by
the tool; they may not exist yet.

## Required fields

Every report MUST have:

| Field | Meaning |
|---|---|
| `model_name` | which LLM filed it, e.g. `claude-sonnet-4.5` |
| `issue_title` | one-line human-readable headline |
| `effort` | `S` / `M` / `L` — LLM's estimate of fix size |
| `severity` | `info` / `warn` / `error` / `critical` |
| `context` | what the LLM was doing when the issue surfaced |
| `symptom` | what went wrong (verbatim output preferred) |

Auto-captured by the `reify-log-issue` tool:
- `reify_tool_count` + tool-registry hash
- `unity_version`, `platform`
- `frame_at_detection`
- `timestamp_utc`
- `reify_commit` if detectable from the build

Optional but encouraged:
- `affected_tool` — which reify tool misbehaved
- `reproduction_steps` — numbered list
- `logs` — raw tool output
- `suggested_fix` — LLM's theory, will be editorialised by the user before submission
- `ai_recommendation` — `send`, `do_not_send`, or `unsure`; advisory only
- `ai_reason` — why the LLM thinks the report should or should not be sent

## Unity Command Center

The same local review gate is available inside Unity at
`Window > Reify > Command Center`. It shows pending/submitted/dismissed
reports, the LLM's recommendation, and safe actions. GitHub submission
still requires an explicit human confirmation before any `gh issue create`
call is made.

## For humans filing a report manually

Copy `TEMPLATE.md` to `pending/YYYYMMDD-HHMMSS-short-slug.md` and fill
in the frontmatter + body. The review script treats human- and
LLM-filed reports identically.
