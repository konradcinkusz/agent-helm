---
description: A tour of the AgentHelm interface — the session rail, the session header, banners and the Chat, Changes and Terminal tabs.
---

# The interface

AgentHelm's UI has two pages: the **Dashboard**, where you work with sessions, and **Providers**, where you check the accounts of your agent CLIs. This page is a map of the Dashboard; the pages that follow cover each feature in depth.

![The AgentHelm dashboard with an open session](../assets/screenshots/session-chat.png){ loading=lazy }

## Top bar

The brand, links to **Dashboard** and **Providers**, and on the right the number of agents the Bridge has configured. If it keeps saying *connecting…*, the UI cannot reach the Bridge or the Bridge has no agents configured — see [Troubleshooting](../project/troubleshooting.md#the-ui-says-connecting-and-lists-no-agents).

## Session rail (left)

| Element | What it does |
|---|---|
| **Sessions / History** | The rail shows live sessions; **History** switches to archived sessions from PostgreSQL, **Live** switches back. See [History & resume](history.md). |
| **＋ New** | Opens the new-session form: agent, working directory, title, model. See [Sessions & chat](sessions.md#starting-a-session). |
| Session cards | One per live session: title, then `agent · status · policy`, plus `⏳ permission` while a permission request is waiting for you. |

The rail refreshes every few seconds; the open session itself updates live.

## Session header

| Element | What it does |
|---|---|
| Title and ✎ | Rename the session: ++enter++ saves, ++escape++ cancels. |
| Sub-line | Agent id, working directory, model (when known) and capability chips (`resume`, `images`, `files`). |
| **Scope** | Quality scores from [CopilotScope](copilotscope.md) for this session's time window. |
| **Handoff** | Continue the conversation with another agent — see [Agent handoff](handoff.md). |
| Policy drop-down | **Ask every tool**, **Auto-allow reads** or **YOLO** — see [Permissions & policies](permissions.md). |
| Status badge | `idle` or `running` (a turn is in progress). |
| **Stop** | Asks the agent to cancel the current turn (ACP `session/cancel`). |
| **Delete** | Ends the session: stops the agent and the terminal and deletes the session's history record. |

## Banners

Banners appear between the header and the tabs when something needs you:

- **Permission request** — the agent wants to run a tool; the buttons are the options the agent offered. See [Permissions & policies](permissions.md).
- **YOLO confirmation** — shown when you pick YOLO, before anything changes.
- **Handoff** and **Scope** panels — opened from their header buttons.

## Tabs

| Tab | What it shows |
|---|---|
| **Chat** | The transcript and the composer. See [Sessions & chat](sessions.md). |
| **Changes** | Uncommitted git changes in the working directory, with diff, accept and reject. See [Reviewing changes](changes.md). |
| **Terminal** | A shell in the working directory. See [Terminal](terminal.md). |

## Keyboard

| Keys | Where | Action |
|---|---|---|
| ++ctrl+enter++ | Composer | Send the prompt |
| ++enter++ / ++escape++ | Title editor | Save / cancel the new title |
| ++enter++ | Terminal input | Run the command |

## Providers page

Lists the account status of the Copilot, Claude Code and Gemini CLIs on the Bridge's machine, with login and logout actions and — for Copilot — the available models. See [Provider accounts](providers.md).
