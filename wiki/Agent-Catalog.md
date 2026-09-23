The agent catalog is the list under `AgentHelm:Agents` in the Bridge's configuration. Each entry turns a command into an agent you can pick in **＋ New**. The catalog is read **once, when the Bridge starts** — restart the Bridge after changing it.

## Fields

| Field | Required | Default | Description |
|---|:---:|---|---|
| `Id` | yes | — | Unique, case-insensitive identifier. Used by the API (`agentId`) and shown on session cards. The ids `copilot`, `claude` and `gemini` also get a card on the [Providers](Provider-Accounts.md) page. |
| `Name` | yes | — | Display name in the UI. |
| `Command` | yes | — | Executable to start: a name on the Bridge's `PATH` or an absolute path. |
| `Args` | no | `[]` | Arguments, one array item per argument. |
| `Environment` | no | `{}` | Extra environment variables for the agent process. |
| `Type` | no | `acp` | Adapter: `acp` or `copilot-sdk` — see [below](#type-acp-or-copilot-sdk). |

`Command` and `Args` may contain the placeholder `${AGENTHELM_DIR}`.

## How an agent process is started

For every session the Bridge starts `Command` with `Args`:

- **without a shell** — no globbing, no variable expansion, no pipes; each `Args` item reaches the agent as exactly one argument;
- with the **session's working directory** as its current directory;
- with the **Bridge's environment**, plus the entry's `Environment`, plus the model hints `GH_COPILOT_MODEL`, `CLAUDE_MODEL` and `GEMINI_MODEL` when a model was chosen for the session;
- with standard input and output carrying ACP (newline-delimited JSON-RPC 2.0) and standard error logged at `Debug` under the category `agent.<Id>`.

When the session ends — deleted, or the Bridge stops — the agent's whole process tree is killed.

> **Tip:** **Windows:** because there is no shell, Windows only resolves `.exe` files by name. CLIs installed through npm are `.cmd` shims, so use `npx.cmd`, `gemini.cmd` and so on in `Command` if the plain name is not found.

## The `${AGENTHELM_DIR}` placeholder

`${AGENTHELM_DIR}` is replaced with the directory containing the Bridge's binaries (with a trailing separator). Use it for anything that ships next to the Bridge. A relative path would not work: agents start in the *session's* working directory, so `tools/agent.dll` would be looked up inside the user's repository.

The built-in echo agent uses it in all three layouts:

| Layout | Echo agent entry |
|---|---|
| From source | `"Command": "dotnet", "Args": ["run", "--project", "${AGENTHELM_DIR}../../../../../tools/AgentHelm.EchoAgent"]` |
| Release zip | `"Command": "dotnet", "Args": ["${AGENTHELM_DIR}../echo-agent/AgentHelm.EchoAgent.dll"]` |
| Container | `"Command": "dotnet", "Args": ["/app/echo-agent/AgentHelm.EchoAgent.dll"]` |

The release and container variants are written by the build (`release.yml` and the `Dockerfile` rewrite the entry with `jq`).

## `Type`: `acp` or `copilot-sdk`

| Type | Adapter | Status |
|---|---|---|
| `acp` | `AcpAdapter` — the Agent Client Protocol over stdio | Default; used by every preconfigured agent. |
| `copilot-sdk` | `CopilotSdkAdapter` — the GitHub Copilot SDK | Skeleton. Only compiled with the `COPILOT_SDK` build symbol and the SDK package; otherwise starting such an agent fails with an explanation. See [Agent adapters](Agent-Adapters.md#the-copilot-sdk-adapter). |

Any other value fails at session start with *Unknown agent type '…'*.

## The default catalog

```json
"Agents": [
  {
    "Id": "copilot",
    "Name": "GitHub Copilot CLI",
    "Command": "copilot",
    "Args": ["--acp", "--stdio"],
    "Environment": {
      "COPILOT_OTEL_ENABLED": "true",
      "COPILOT_OTEL_EXPORTER_TYPE": "otlp-http",
      "OTEL_EXPORTER_OTLP_ENDPOINT": "http://localhost:4318",
      "OTEL_EXPORTER_OTLP_PROTOCOL": "http/protobuf",
      "OTEL_EXPORTER_OTLP_TRACES_PROTOCOL": "http/protobuf",
      "OTEL_EXPORTER_OTLP_METRICS_PROTOCOL": "http/protobuf",
      "OTEL_EXPORTER_OTLP_LOGS_PROTOCOL": "http/protobuf",
      "OTEL_EXPORTER_OTLP_HEADERS": "x-api-key=dev-secret-123",
      "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT": "true"
    }
  },
  { "Id": "claude", "Name": "Claude Code", "Command": "npx", "Args": ["@zed-industries/claude-code-acp"] },
  { "Id": "gemini", "Name": "Gemini CLI", "Command": "gemini", "Args": ["--acp"] },
  {
    "Id": "echo",
    "Name": "Echo (built-in demo agent)",
    "Command": "dotnet",
    "Args": ["run", "--project", "${AGENTHELM_DIR}../../../../../tools/AgentHelm.EchoAgent"]
  }
]
```

The `Environment` block of `copilot` exports telemetry to [CopilotScope](CopilotScope-Integration.md).

## Examples

A custom agent with its own environment:

```json
{
  "Id": "myagent",
  "Name": "My Agent",
  "Command": "/opt/my-agent/bin/my-agent",
  "Args": ["--acp", "--log-level", "warn"],
  "Environment": { "MY_AGENT_HOME": "/opt/my-agent" }
}
```

A pinned Copilot CLI that must not update itself (see the churn warning in [Connecting agents](Connecting-Agents.md#github-copilot-cli)):

```json
{
  "Id": "copilot",
  "Name": "GitHub Copilot CLI (pinned)",
  "Command": "/opt/copilot-cli/1.2.3/copilot",
  "Args": ["--acp", "--stdio", "--no-auto-update"]
}
```

## Overriding the catalog with environment variables

Arrays are addressed by index, so environment variables can change single fields — useful for containers:

```bash
AgentHelm__Agents__2__Args__0=--experimental-acp     # third entry (gemini), first argument
AgentHelm__Agents__4__Id=myagent                     # add a fifth entry…
AgentHelm__Agents__4__Name="My Agent"
AgentHelm__Agents__4__Command=my-agent
```

For more than a field or two, mount your own `appsettings.json` instead.
