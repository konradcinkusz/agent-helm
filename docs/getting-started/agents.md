---
description: Connect GitHub Copilot CLI, Claude Code, Gemini CLI or any other Agent Client Protocol agent to AgentHelm.
---

# Connecting agents

AgentHelm drives agents through the [Agent Client Protocol](https://agentclientprotocol.com) (ACP): the Bridge starts the agent as a child process and exchanges JSON-RPC messages with it over standard input and output. Any agent that can run in that mode works; there are no plugins to install. Each agent is one entry in the [agent catalog](../configuration/agent-catalog.md), `AgentHelm:Agents` in the Bridge's `appsettings.json`.

Four entries are preconfigured:

| Agent | Catalog `Id` | Command | You need |
|---|---|---|---|
| Echo (built-in demo agent) | `echo` | the bundled `AgentHelm.EchoAgent` | nothing |
| GitHub Copilot CLI | `copilot` | `copilot --acp --stdio` | `copilot` on the `PATH`, logged in, a Copilot subscription |
| Claude Code | `claude` | `npx @zed-industries/claude-code-acp` | Node.js; Claude Code authentication |
| Gemini CLI | `gemini` | `gemini --acp` | `gemini` on the `PATH`, authenticated |

An agent whose command is not installed still appears in the **＋ New** drop-down; starting a session with it fails with *Could not start agent '…'* and the reason.

> [!IMPORTANT]
> Agents run **as the user running the Bridge**, with the Bridge's environment variables and `PATH`, and in the session's working directory. Install and authenticate each CLI for that user, on that machine. If the Bridge runs in a container, only the echo agent is available — see [Deployment & topology](../configuration/deployment.md).

## GitHub Copilot CLI

1. Install the GitHub Copilot CLI and make sure `copilot` is on the `PATH` of the Bridge.
2. Log in once: `copilot login`.
3. Start a session with **GitHub Copilot CLI**.

The preconfigured entry runs `copilot --acp --stdio` and adds an `Environment` block that switches on Copilot's OpenTelemetry export to `http://localhost:4318` — the default address of a local [CopilotScope](../guide/copilotscope.md) collector. Remove or change those variables if you do not run CopilotScope.

If the GitHub CLI (`gh`) is installed and authenticated, the new-session form offers a **model drop-down** for Copilot, filled from `gh api /copilot/models`; see [Provider accounts](../guide/providers.md).

> [!WARNING]
> The Copilot CLI has removed a programmatic interface without deprecation before ([github/copilot-cli#1606](https://github.com/github/copilot-cli/issues/1606)). If the `copilot` entry stops working after the CLI updated itself, pin a known-good CLI version and run it with `--no-auto-update`.

## Claude Code

Claude Code speaks ACP through Zed's adapter, [`@zed-industries/claude-code-acp`](https://www.npmjs.com/package/@zed-industries/claude-code-acp), which `npx` downloads the first time you start a session — the first start takes a little longer.

1. Install Node.js so that `npx` is on the Bridge's `PATH`.
2. Authenticate Claude Code for the user running the Bridge, the way you normally do.
3. Start a session with **Claude Code**.

## Gemini CLI

1. Install the Gemini CLI and make sure `gemini` is on the Bridge's `PATH`.
2. Authenticate it once in a terminal.
3. Start a session with **Gemini CLI**.

> [!NOTE]
> The flag that puts the Gemini CLI into ACP mode has changed between releases; older versions used `--experimental-acp`. If `gemini --acp` is rejected, check `gemini --help` and adjust `Args` in the catalog.

## Any other ACP agent

The ACP ecosystem lists dozens of agents. To add one, append an entry to `AgentHelm:Agents` and restart the Bridge:

```json
{
  "Id": "myagent",
  "Name": "My Agent",
  "Command": "my-agent",
  "Args": ["--acp"],
  "Environment": { "MY_AGENT_LOG_LEVEL": "info" }
}
```

`Id` must be unique; `Args` and `Environment` are optional. The [agent catalog reference](../configuration/agent-catalog.md) documents every field, the `${AGENTHELM_DIR}` placeholder and the `Type` switch between the ACP adapter and the Copilot SDK adapter.

> [!TIP]
> **Windows:** the Bridge starts agents directly, without a shell, and Windows then only resolves `.exe` files. Command-line tools installed through npm are `.cmd` shims, so write `npx.cmd`, `gemini.cmd` or `copilot.cmd` in `Command` if the plain name is not found.

## Choosing a model

The new-session form has a **Model** field. For agents with a model list (currently Copilot, through `gh`) it is a drop-down; otherwise it is free text. The chosen value is passed to the agent process as the environment variables `GH_COPILOT_MODEL`, `CLAUDE_MODEL` and `GEMINI_MODEL` — a hint that each CLI may or may not read. An agent that ignores these variables uses its own default model.

## What AgentHelm learns from an agent

During the ACP `initialize` handshake the agent advertises its capabilities. AgentHelm shows them as chips in the session header:

| Chip | Capability | What it enables |
|---|---|---|
| `resume` | `loadSession` | Resuming archived sessions — see [History & resume](../guide/history.md) |
| `images` | `promptCapabilities.image` | Image attachments |
| `files` | `promptCapabilities.embeddedContext` | Text-file attachments as embedded resources |

## When an agent does not start

- **Command not found** — the executable is not on the `PATH` of the *Bridge process*, which can differ from your interactive shell (for example when the Bridge was started from an IDE). Use an absolute path in `Command`.
- **The agent exits immediately** — usually missing authentication. The agent's standard error is logged by the Bridge at `Debug` level under the category `agent.<id>`; enable it with `Logging__LogLevel__agent=Debug`.
- **The form stays on *Starting…*** — the agent started but never answered the ACP handshake. Check that the arguments put it into ACP mode.

More in [Troubleshooting](../project/troubleshooting.md).
