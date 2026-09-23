AgentHelm is a **web cockpit for AI coding agents**. You run it on your own machine; it starts coding agents such as GitHub Copilot CLI, Claude Code or Gemini CLI as local processes, and gives you one browser UI to talk to them — with every tool call the agent wants to make passing through a permission decision that ends up in a permanent transcript.

It is not a hosted service and not a library. It is a small local application made of two processes — the **Bridge** (an API that owns sessions and agents) and the **Web** UI — plus an optional PostgreSQL database for history.

## Why it exists

Coding agents are powerful precisely because they run tools on your machine: they read and write files, run commands, fetch URLs. Terminal UIs for these agents tend to be vendor-specific, and what was approved — and by whom — is easy to lose. AgentHelm adds three things on top of any agent that speaks the [Agent Client Protocol](https://agentclientprotocol.com) (ACP):

1. **One UI for many agents.** ACP is to coding agents roughly what LSP is to language servers. One ACP client in AgentHelm reaches every agent in the ACP ecosystem; adding an agent is a configuration entry, not a plugin.
2. **Explicit, audited permissions.** Every tool call is decided under a per-session policy — ask every time, auto-allow read-only kinds, or auto-allow everything after an explicit opt-in — and every decision, human or automatic, is written to the transcript.
3. **A workbench around the conversation.** Review the agent's changes as a git diff and accept or revert them per file, run commands in a terminal next to the session, attach files, hand the conversation over to another agent, and resume archived sessions.

## Core concepts

- **Session** — One conversation with one agent in one **working directory**. Each session has its own agent process, transcript, permission policy and terminal. You can run several sessions side by side.
- **Agent** — A command that speaks ACP over standard input/output — for example `copilot --acp --stdio`. Agents are declared in the [agent catalog](Agent-Catalog.md).
- **Working directory** — The directory the agent works in (usually a repository). It is a path **on the machine where the Bridge runs** — see [deployment & topology](Deployment-and-Topology.md).
- **Permission policy** — How the session answers the agent's permission requests: `ask`, `auto_read` or `yolo`. Policies can only auto-*allow*; rejecting is always a human decision. See [permissions & policies](Permissions-and-Policies.md).
- **Transcript** — The ordered record of a session: your prompts, the agent's replies, tool calls, and audit entries for every permission decision, policy change, git action and handoff.
- **Bridge and Web** — The Bridge (`AgentHelm.Bridge`) is the local API that owns sessions and agent processes and is the security boundary. The Web UI (`AgentHelm.Web`) is a Blazor Server app that talks to the Bridge; your browser only talks to the Web UI.

The [glossary](Glossary.md) has the full list of terms.

## Next steps

1. [Install AgentHelm](Installation.md) — release zip, from source with Aspire, or containers.
2. [Run your first session](Your-First-Session.md) with the built-in echo agent — it takes about a minute and needs no real agent.
3. Work through the [tutorial](Tutorial.md) for a guided tour of every feature.
4. [Connect a real agent](Connecting-Agents.md).
