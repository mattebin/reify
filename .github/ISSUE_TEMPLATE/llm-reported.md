---
name: LLM-reported issue
about: An issue surfaced by an LLM using reify. Usually filed via `scripts/review-llm-issues.py`, but this template lets humans file the same shape manually.
title: "[llm] "
labels: llm-reported
---

## Metadata

- **model_name**: <e.g. claude-sonnet-4.5>
- **effort**: S | M | L
- **severity**: info | warn | error | critical
- **affected_tool**: <reify tool name, if any>
- **unity_version**:
- **platform**:
- **reify_commit**:

## Context

What the LLM (or filer) was doing when the issue surfaced.

## Symptom

What went wrong, verbatim when possible.

## Reproduction

1.
2.
3.

## Logs

```
(raw tool output, bridge responses, etc.)
```

## Suggested fix

Optional — filer's theory of the cause and/or the shape of the fix.
