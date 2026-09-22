---
description: Starting sessions, the working directory, chatting with an agent, attachments, renaming, stopping and deleting sessions.
---

# Sessions & chat

A **session** is one conversation with one agent in one working directory. Each session owns an agent process, a transcript, a permission policy and (once you open it) a terminal. Sessions run independently, so you can keep several open side by side — with different agents or in different repositories.

## Starting a session

Click **＋ New** in the rail and fill in the form:

![The new-session form](../assets/screenshots/new-session.png){ width="480" align=right loading=lazy }

Agent
:   One of the agents from the [agent catalog](../configuration/agent-catalog.md).

Working directory
:   The directory the agent works in — usually a repository. It must exist **on the machine running the Bridge**; the path is checked there, not in your browser. The 📁 button opens a directory browser.

Title
:   Optional. Defaults to `<agent name> · <folder name>`. You can rename the session later.

Model
:   Optional. A drop-down when the agent has a model list (Copilot, via `gh`), otherwise free text. Passed to the agent as an environment hint — see [Choosing a model](../getting-started/agents.md#choosing-a-model).

⚙ Pre-config
:   Shows the CopilotScope address the Bridge reads quality scores from, and lets you change it for the running Bridge before the session starts. See [CopilotScope integration](copilotscope.md#configuration).

**Start session** launches the agent process in the working directory, performs the ACP handshake (`initialize`, then `session/new`) and opens the session. New sessions start with the `ask` policy.

<div style="clear: both"></div>

### The directory browser

The 📁 button browses directories **on the Bridge's machine**, starting at the home directory of the user running the Bridge, with **↑ Up**, **🏠 Home**, **/ Root** and a **Show hidden** toggle. **Select this directory** copies the path into the form.

In Chromium-based browsers the button first opens your operating system's folder picker. Browsers only reveal the *name* of the folder you pick, not its path, so AgentHelm then opens its own browser with a hint — *Local: acme-api — navigate to where this folder is mounted on the Bridge server*. When the Bridge runs on the same machine as the browser, that is simply where the folder is; for containers see [Deployment & topology](../configuration/deployment.md#working-directories).

### If the session does not start

| Message | Meaning |
|---|---|
| *Unknown agent 'x'. Configure it under AgentHelm:Agents.* | The catalog has no agent with that id — check the Bridge's `appsettings.json` and restart it. |
| *Working directory does not exist: …* | The path does not exist on the Bridge's machine. With the Bridge in a container, mount the directory and use the container path. |
| *Could not start agent 'x': …* | The agent process could not be started — usually the command is not on the Bridge's `PATH`. |

A form that stays on *Starting…* means the agent started but never answered the handshake; see [Troubleshooting](../project/troubleshooting.md#a-session-stays-on-starting).

## Chatting

Type in the composer at the bottom and press ++ctrl+enter++ or **Send**. While the agent works:

- its reply streams into a bubble marked *assistant · streaming*;
- tool calls appear as `tool` entries, and permission requests as banners ([Permissions & policies](permissions.md));
- the status badge reads `running`, and **Send** is disabled — one turn at a time per session.

When the turn ends, the streamed text becomes a permanent `assistant` entry and the status returns to `idle`. If the Bridge refuses a prompt — it cannot be reached, or the attachments are too large — the reason appears above the composer and your prompt stays in it. **Stop** asks the agent to cancel the turn (ACP `session/cancel`); the agent decides how quickly it stops.

### What the transcript records

| Role | Entries |
|---|---|
| `user` | Your prompts, with the names of attached files. |
| `assistant` | The agent's replies, one entry per stretch of text. |
| `tool` | Each tool call the agent announces, by title. |
| `system` | Audit entries: permission requests and decisions, policy changes, git accept/reject, handoffs, and agent errors. |

The agent's "thinking" stream and tool-status updates are shown live but not recorded. The full list of entry kinds and audit texts is in [Events & transcript](../reference/events.md).

## Attachments

Click the **📎** in the composer to attach files to the next prompt:

- up to **4 files**, at most **2 MB** each;
- **images** travel as ACP `image` content blocks — the agent receives the picture itself;
- **text files** travel as embedded `resource` blocks with the file's content;
- other **binary files** are refused in the browser.

The capability chips in the header show what the agent advertised: `images` and `files`. If it advertised neither, the paperclip's tooltip warns that the agent may reject attachments.

> [!NOTE]
> The Bridge also caps a whole prompt's attachments at 8,000,000 characters of encoded data (images are base64-encoded, which adds about a third) and refuses larger prompts with HTTP 413. Four images close to the 2 MB limit exceed that cap. The UI then says *The attachments are too large for one prompt* and keeps your text and attachments, so you can remove some and send again.

## Renaming

Click ✎ next to the title, type, and press ++enter++ (or ++escape++ to cancel). Titles are 1–120 characters. The new title appears everywhere at once and is saved with the session.

## Deleting

**Delete** ends the session for good: the agent process and the session's terminal are stopped, the session disappears from the rail, and — with PostgreSQL configured — its history record is deleted too. A permission request that was still waiting is answered as denied.

> [!WARNING]
> There is no undo. If you only want to stop the agent's current turn, use **Stop**.

## Sessions and Bridge restarts

Live sessions live in the Bridge's memory, and their agents are child processes of the Bridge. When the Bridge stops, so do they. With PostgreSQL configured, their transcripts remain available under [History](history.md) and can be resumed if the agent supports it.
