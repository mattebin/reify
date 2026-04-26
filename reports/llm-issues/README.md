# LLM-reported issues

This folder is where an LLM using reify writes a structured bug or
unexpected-behavior report when it encounters something worth fixing.

## Flow

```text
LLM hits a problem
  -> calls reify-log-issue with { model_name, issue_title, effort, ... }
  -> file is written to reports/llm-issues/pending/<timestamp>-<slug>.md
  -> user runs: python scripts/review-llm-issues.py
  -> each pending file is submitted, dismissed, deleted, skipped, or kept
```

Review outcomes:

- `submitted/`: filed to GitHub; the file records the issue URL.
- `dismissed/`: reviewed and rejected, duplicate, stale, or already fixed.
- `pending/`: still awaiting human review.

## Why not open GitHub issues directly

Every LLM would file duplicates, near-duplicates, and false positives. The
user-in-the-loop gate keeps the GitHub issue tracker signal high. It also lets
the user rewrite the title or body before submission.

## Folder structure

```text
reports/llm-issues/
  README.md
  TEMPLATE.md
  pending/
  submitted/
  dismissed/
```

`pending/`, `submitted/`, and `dismissed/` are created on first use by the
tool; they may not exist yet.

## Required fields

Every report MUST have:

| Field | Meaning |
|---|---|
| `model_name` | which LLM filed it, e.g. `claude-sonnet-4.5` |
| `issue_title` | one-line human-readable headline |
| `effort` | `S` / `M` / `L`, the LLM's estimate of fix size |
| `severity` | `info` / `warn` / `error` / `critical` |
| `context` | what the LLM was doing when the issue surfaced |
| `symptom` | what went wrong; verbatim output preferred |

Auto-captured by the `reify-log-issue` tool:

- `reify_tool_count` + tool-registry hash
- `unity_version`, `platform`
- `frame_at_detection`
- `timestamp_utc`
- `reify_commit` if detectable from the build

Optional but encouraged:

- `affected_tool`
- `reproduction_steps`
- `logs`
- `suggested_fix`
- `ai_recommendation`: `send`, `do_not_send`, or `unsure`
- `ai_reason`

## Unity Command Center

The same local review gate is available inside Unity at
`Window > Reify > Command Center`. It shows pending/submitted/dismissed
reports, the LLM's recommendation, and safe actions. GitHub submission still
requires explicit human confirmation before any `gh issue create` call is
made.

## For humans filing a report manually

Copy `TEMPLATE.md` to `pending/YYYYMMDD-HHMMSS-short-slug.md` and fill in the
frontmatter + body. The review script treats human- and LLM-filed reports
identically.
