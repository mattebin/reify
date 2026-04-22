---
issue_title:       "<short one-line headline>"
model_name:        "<e.g. claude-sonnet-4.5 or gpt-5-codex>"
effort:            "S"            # S / M / L
severity:          "warn"          # info / warn / error / critical
affected_tool:     "<reify tool name, if any>"
unity_version:     "<autofilled by reify-log-issue>"
platform:          "<autofilled>"
reify_tool_count:  0                # autofilled
frame_at_detection: 0               # autofilled
timestamp_utc:     "<autofilled>"
reify_commit:      "<optional, if known>"
---

## Context

What the LLM (or human) was doing when the issue surfaced. Why the
operation was being attempted. The goal that the problem blocked.

## Symptom

The observable problem. Prefer verbatim tool output over paraphrase.
If a value was wrong, both the expected and the actual.

## Reproduction

1.
2.
3.

## Logs

```
(paste raw tool output, error messages, bridge responses etc.)
```

## Suggested fix

Optional. The reporter's theory of what's wrong and how to address it.
The user will rewrite this for the GitHub issue body — its job here is
to give the user a starting point, not a final statement.
