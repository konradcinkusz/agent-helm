---
description: AgentHelm's Agent Client Protocol client — transport, the messages it sends and handles, permission outcomes, file-system requests and the path guard.
---

# Agent Client Protocol

The [Agent Client Protocol](https://agentclientprotocol.com) (ACP) standardises how an editor-like **client** talks to a coding **agent**. AgentHelm's Bridge is the client. Its implementation, `AcpClient` in [`Agents/Acp/AcpClient.cs`](https://github.com/konradcinkusz/agent-helm/blob/master/src/AgentHelm.Bridge/Agents/Acp/AcpClient.cs), is deliberately small and tolerant: agents differ in the details of a young protocol, and a client that throws on the unexpected would break on every new agent release.

## Transport

- One **child process** per session, started by `ProcessTransport` with the session's working directory (see [how agents are started](../configuration/agent-catalog.md#how-an-agent-process-is-started)).
- **JSON-RPC 2.0**, one message per line (newline-delimited JSON) on the agent's standard input and output, encoded as UTF-8 **without a byte-order mark** — a BOM in front of `initialize` is enough for some agents to ignore it.
- The agent's standard error is drained continuously, so a chatty agent can never block on a full pipe, and logged at `Debug`.
- Transports implement `IAcpTransport` (`ReadLineAsync` / `WriteLineAsync`); the tests drive the client with a scripted in-memory transport instead of a process.

## Client → agent

| Method | When | Parameters AgentHelm sends |
|---|---|---|
| `initialize` | right after the process starts | `protocolVersion: 1`, `clientCapabilities.fs.readTextFile: true`, `clientCapabilities.fs.writeTextFile: true` |
| `session/new` | new session | `cwd` (the working directory), `mcpServers: []` |
| `session/load` | resuming an archived session | `sessionId` (the agent's own id), `cwd`, `mcpServers: []` — only if the agent advertised `loadSession` |
| `session/prompt` | each prompt | `sessionId`, `prompt`: a `text` block plus one block per attachment |
| `session/cancel` | **Stop** (a notification, no reply) | `sessionId` |

From the `initialize` result AgentHelm reads `agentCapabilities.loadSession` and `agentCapabilities.promptCapabilities` (`image`, `audio`, `embeddedContext`); they become the capability chips in the UI. From `session/prompt` it reads `stopReason` (defaulting to `end_turn`).

Attachments become content blocks:

```json
[
  { "type": "text", "text": "What is in this screenshot?" },
  { "type": "image", "data": "<base64>", "mimeType": "image/png" },
  { "type": "resource", "resource": { "uri": "file:///notes.md", "mimeType": "text/markdown", "text": "…" } }
]
```

AgentHelm does not pass MCP servers to agents (`mcpServers` is always empty).

## Agent → client

### Notifications: `session/update`

Every streamed update arrives as `session/update` with an `update.sessionUpdate` discriminator. The adapter translates the ones it knows into AgentHelm's uniform events:

| `sessionUpdate` | AgentHelm event | Effect |
|---|---|---|
| `agent_message_chunk` | `assistant_chunk` | streamed text, later one `assistant` transcript entry |
| `agent_thought_chunk` | `thought_chunk` | ignored — not shown, not persisted |
| `tool_call` | `tool_call` | a `tool` transcript entry with the tool's title |
| `tool_call_update` | `tool_update` | live event with the tool's status, not persisted |
| `plan` | `plan` | forwarded as a live event |
| anything else | passed through by name | forwarded as a live event |

When `session/load` replays a stored conversation, the replay arrives as ordinary `session/update` notifications *before* the `session/load` response.

### Requests the client answers

| Method | Handling |
|---|---|
| `session/request_permission` | Routed to the session's permission handler — the [policy engine](../guide/permissions.md), then you. |
| `fs/read_text_file` | Reads a file inside the working directory; honours the optional `line` (1-based) and `limit`. |
| `fs/write_text_file` | Writes a file inside the working directory, creating parent directories. |
| anything else | Error `-32601` *Method not found*, so a well-behaved agent can carry on. |

Each request is handled on its own task, so a permission request waiting for you does not stop the client from reading other messages. Every request gets an answer: a refused path is `-32602`, a missing file `-32002`, and any other failure — a denied write, say — `-32603` with the reason, so an agent is never left waiting.

## Permission outcomes

The agent sends the tool call (`title`, `kind`) and its options (`optionId`, `name`, `kind` such as `allow_once`, `allow_always`, `reject_once`, `reject_always`). AgentHelm answers with one of:

```json
{ "outcome": { "outcome": "selected", "optionId": "allow" } }
{ "outcome": { "outcome": "cancelled" } }
```

An allow decision returns the chosen option. A denial returns the first reject-type option the agent offered, or `cancelled` if it offered none. If no permission handler is attached at all, requests are denied — safe by default.

## The path guard

`fs/read_text_file` and `fs/write_text_file` let an agent use the Bridge to touch files. Both go through `GuardPath`:

1. the path is required;
2. a relative path is combined with the session's working directory, and the result is normalised (`Path.GetFullPath`);
3. anything that is not the working directory itself or below it is refused with JSON-RPC error `-32602` *Access outside the session working directory is not allowed*;
4. the same check is repeated on the **real** location: the path and the working directory are resolved the way the operating system will open them, following every symbolic link — file or directory, relative or absolute, chained — so a link inside the directory that points outside it is refused too. A link cycle is refused as well.

The comparison is ordinal (case-sensitive), so on case-insensitive file systems a differently-cased spelling of an allowed path is refused rather than allowed. The git endpoints use the same rule (`GitService.GuardPath`); both share `PathGuard` in [`Security/PathGuard.cs`](https://github.com/konradcinkusz/agent-helm/blob/master/src/AgentHelm.Bridge/Security/PathGuard.cs). The guard covers requests an agent sends *to the Bridge*; what an agent's own tools do inside its process is governed by the permission gateway — see the [security model](../security/index.md).

## Errors and robustness

| Situation | Behaviour |
|---|---|
| A line on stdout that is not JSON | ignored (logged at `Debug`) — some agents log to stdout |
| A JSON-RPC error response | raised as `AcpException(code, message)`; during a prompt it becomes an *Agent error: …* transcript entry |
| The agent exits | every outstanding request fails with *Agent connection closed.* |
| A file request fails (missing file, denied write) | answered with a JSON-RPC error rather than left unanswered |
| Unknown notification methods | ignored |
| Unknown update kinds | forwarded, never fatal |

There is currently no timeout on the handshake: an agent that never answers `initialize` leaves the session start waiting.
