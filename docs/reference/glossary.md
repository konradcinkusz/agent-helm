---
description: The terms used throughout the AgentHelm documentation.
---

# Glossary

ACP — Agent Client Protocol
:   An open protocol, JSON-RPC 2.0 over stdio, between a client (an editor, or AgentHelm's Bridge) and a coding agent. See [agentclientprotocol.com](https://agentclientprotocol.com) and [Agent Client Protocol](../architecture/acp.md).

Adapter
:   The Bridge component that drives one kind of agent protocol behind the `IAgentAdapter` interface: `AcpAdapter` today, `CopilotSdkAdapter` as a skeleton. See [Agent adapters](../architecture/adapters.md).

Agent
:   A program that plans and carries out coding tasks with tools — GitHub Copilot CLI, Claude Code, Gemini CLI, the echo agent. In AgentHelm, an entry in the agent catalog.

Agent catalog
:   The list of agents under `AgentHelm:Agents` in the Bridge's configuration. See [Agent catalog](../configuration/agent-catalog.md).

Aspire
:   .NET Aspire, used by `AgentHelm.AppHost` to run PostgreSQL, the Bridge and the Web UI together during development.

Audit entry
:   A `system` transcript entry recording a decision or action: permission requests and answers, policy changes, git accept/reject, handoffs. See [Events & transcript](events.md).

Bridge
:   `AgentHelm.Bridge`, the local ASP.NET Core process that owns sessions and agent processes, decides permissions and serves the HTTP API. The security boundary.

Capability hints
:   What an agent advertised in the ACP `initialize` handshake — `loadSession`, `image`, `embeddedContext` — shown as the `resume`, `images` and `files` chips.

CopilotScope
:   AgentHelm's sibling project, a local collector that scores sessions from agent telemetry. See [CopilotScope integration](../guide/copilotscope.md).

Echo agent
:   `AgentHelm.EchoAgent`, the built-in demo agent: echoes prompts, asks for a permission when a prompt contains "tool", supports resume.

Handoff
:   Continuing a conversation with another agent in a new session, with a summary prefilled — never sent automatically. See [Agent handoff](../guide/handoff.md).

History
:   Archived session snapshots in PostgreSQL. See [History & resume](../guide/history.md).

Native session id
:   The agent's own id for a session (from `session/new`), stored with the snapshot and used by `session/load` to resume.

Path guard
:   The rule that file requests an agent sends to the Bridge, and the git endpoints, may only touch paths inside the session's working directory — checked as written and after following symbolic links.

Permission policy
:   How a session answers permission requests: `ask`, `auto_read` or `yolo`. Policies can only allow automatically. See [Permissions & policies](../guide/permissions.md).

Permission request
:   An agent's question, before running a tool, whether it may — with the tool's title, its kind and the answer options.

PTY
:   Pseudo-terminal. The Terminal tab runs its shell in a PTY on Linux through `script(1)`, otherwise through a plain pipe.

Session
:   One conversation with one agent in one working directory, with its own agent process, transcript, policy and terminal.

SSE — server-sent events
:   The `text/event-stream` format the Bridge uses to stream session, terminal and login events.

Tool kind
:   The ACP classification of a tool call — `read`, `edit`, `delete`, `move`, `search`, `execute`, `think`, `fetch`, `other`.

Transcript
:   The ordered, append-only record of a session: prompts, replies, tool calls and audit entries.

Turn
:   One prompt and everything the agent does in response, until it returns a stop reason.

Web UI
:   `AgentHelm.Web`, the Blazor Server application you use in the browser. It talks to the Bridge; the browser only talks to it.

Working directory
:   The directory a session's agent works in — a path on the Bridge's machine, validated when the session starts.

Write-behind
:   Persisting changes asynchronously, about once a second, instead of on every change. See [Persistence](../architecture/persistence.md).

YOLO
:   The policy that allows every tool call automatically; needs explicit confirmation per session and is still audited.
