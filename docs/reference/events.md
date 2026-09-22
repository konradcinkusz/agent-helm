---
description: Every live event kind on a session stream, every transcript entry kind, and the exact audit texts AgentHelm writes.
---

# Events & transcript

Two related vocabularies: **transcript entries** are the permanent record of a session (persisted with the session), **live events** are what the Bridge streams while it happens. Every new transcript entry is also streamed as an `entry` event.

## Transcript entries

Each entry is `{time, role, text, kind}` (PascalCase inside event streams and snapshots, camelCase in API responses).

| Role | Kind | Text | Written when |
|---|---|---|---|
| `user` | `message` | your prompt, plus `[attached: a.png, b.md]` when files were attached | a prompt is sent |
| `assistant` | `message` | a stretch of the agent's reply | a tool call arrives or the turn ends |
| `tool` | `tool_call` | the tool's title | the agent announces a tool call |
| `system` | `permission` | *Permission requested: `<tool>` (`<kind>`)* | a request is put to you |
| `system` | `permission_result` | *Permission granted by user* / *Permission denied by user* | you answer, or the session is deleted while waiting |
| `system` | `permission_auto` | *Permission auto-allowed by policy '`<policy>`': `<tool>` (`<kind>`)* | the policy answers |
| `system` | `policy` | *Permission policy changed to '`<policy>`'* | the policy changes |
| `system` | `git` | *Accepted (staged) changes: `<path>`* / *Rejected (reverted) changes: `<path>`* | you accept or reject in the Changes tab |
| `system` | `handoff` | *Handoff to '`<agent>`' → session `<id>`* | you hand the session off (source session) |
| `system` | `error` | *Agent error: `<message>`* | the prompt fails — the agent returned an error or exited |

Not recorded: the agent's thought stream, tool status updates, plans and other pass-through updates. They are live-only.

## Live events

Streamed on `GET /api/sessions/{id}/stream` as `{"Kind","Text","Data"}`:

| Kind | Text | Data |
|---|---|---|
| `entry` | the entry's text | the transcript entry `{Time, Role, Text, Kind}` |
| `chunk` | a piece of streamed assistant text | — |
| `status` | `running` or `idle` | — |
| `turn_end` | the agent's stop reason, e.g. `end_turn` | — |
| `permission` | the tool's title | the pending request `{RequestKey, ToolTitle, ToolKind, Options: [{OptionId, Name, Kind}]}` |
| `permission_resolved` | the chosen option id, or `denied` | — |
| `policy` | the new policy | — |
| `title` | the new title | — |
| `tool_update` | the tool call's new status, e.g. `completed`, `failed` | — |
| `plan` and other kinds | empty | — |

Typical order for a prompt that asks for a permission:

```text
entry (user) → status running → chunk … → entry (assistant) → entry (tool)
→ entry (permission) → permission → [you answer] → entry (permission_result)
→ permission_resolved → tool_update → chunk … → entry (assistant) → turn_end → status idle
```

The terminal stream carries only `out` events whose `Text` is a chunk of shell output.

## Session status

| Status | Meaning |
|---|---|
| `idle` | no turn in progress; a prompt can be sent |
| `running` | a turn is in progress; prompts are refused with `409` |

## Permission option kinds

ACP agents describe each option with a kind. AgentHelm treats any kind containing `allow` as an allow option and any kind containing `reject` as a reject option:

| Kind | Meaning |
|---|---|
| `allow_once` | allow this call only — preferred by automatic decisions |
| `allow_always` | allow this and future calls of the same tool (stored by the agent) |
| `reject_once` | reject this call |
| `reject_always` | reject this and future calls of the same tool |
