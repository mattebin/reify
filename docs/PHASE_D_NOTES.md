# Phase D Polish Patterns

These are techniques observed in youichi-uda/unity-mcp-pro-plugin and similar
production-grade Unity MCPs. Reimplement fresh in C# within reify's
architecture — these are patterns, not code ports.

## WebSocket / Bridge Resilience
- Port scanning across 6605–6609 (or reify's chosen range) so multiple Unity
  Editor instances can run simultaneously. Each picks the first free port.
- Heartbeat ping every N seconds to detect dead connections early.
- Auto-reconnect with exponential backoff (1s, 2s, 4s, 8s, max 30s).
- Domain reload safety — survive script recompilation without losing
  connection state. Store session state in `SessionState` / `EditorPrefs`
  before reload, restore on re-init.

## Undo/Redo Integration (high philosophy fit)
Every AI-mutating operation goes through Unity's Undo system
(`Undo.RecordObject`, `Undo.RegisterCreatedObjectUndo`, etc.). This means:
- Ctrl+Z reverses AI operations just like manual ones.
- AI ops appear in Unity's Undo history with labels like
  `AI: Create cube at origin`.
- Auditability: scene mutations have a clean reverse path.
- Fits reify's "structured state / verifiable by code" philosophy — every
  mutation is a reversible transaction, not a fire-and-forget side effect.

## Smart Type Parsing
- Auto-convert string values from tool args to Unity types:
  `"1,2,3"` → `Vector3`, `"#FF0000"` → `Color`, `"0,0,0,1"` → `Quaternion`.
- Centralize in a `TypeParser` utility so all tool handlers get consistent
  parsing.
- Handles locale differences (comma vs dot decimals) to prevent Unity C#
  parse failures.

## Response Size Protection
- Global truncation safety net on tool responses — large scene hierarchies
  or asset lists can exceed stdio transport limits and cause silent failures
  (observed as `Write EOF` errors).
- Cap response size at configurable limit, return pagination tokens or
  summary if exceeded.

## Multi-Instance Unity Editor Discovery
- On server startup, scan for all running Unity Editor instances (by port
  range or process enumeration).
- Auto-connect if only one, prompt for selection if multiple.
- Supports ParrelSync and MPPM clone workflows.
