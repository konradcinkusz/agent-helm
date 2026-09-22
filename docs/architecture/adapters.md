---
description: The IAgentAdapter seam that keeps protocol details out of sessions, the ACP adapter, the adapter factory and the gated Copilot SDK adapter.
---

# Agent adapters

Everything in the Bridge above the protocol — sessions, permissions, persistence, the UI — talks to agents through one interface, `IAgentAdapter`. That seam is what makes AgentHelm multi-agent and keeps protocol churn contained: when an agent protocol or SDK changes, one adapter changes.

## The interface

```csharp
public interface IAgentAdapter : IDisposable
{
    string? NativeSessionId { get; }          // the agent's own session id — needed for resume
    AgentCaps? Capabilities { get; }          // hints for the UI; null when the protocol has none

    Task StartAsync(CancellationToken ct);
    Task<string> PromptAsync(string text, IReadOnlyList<PromptAttachment>? attachments, CancellationToken ct);
    Task CancelAsync(CancellationToken ct);

    event Action<AgentEvent>? OnEvent;                          // streamed updates
    Func<PermissionAsk, Task<string?>>? PermissionHandler { get; set; }  // tool permission requests
}
```

- `PromptAsync` resolves when the turn ends, with the stop reason; everything in between arrives through `OnEvent`.
- `PermissionHandler` returns the chosen option id, or `null` to deny. The session sets it; the adapter must call it for every permission request and treat a missing handler as a denial.
- Events are uniform records — `AgentEvent(Kind, Text, Raw)` with kinds `assistant_chunk`, `thought_chunk`, `tool_call`, `tool_update`, `plan` or a pass-through name — and permission requests are `PermissionAsk(RequestKey, ToolTitle, ToolKind, Options, RawJson)`, whatever the protocol.

## The ACP adapter

`AcpAdapter` is the adapter every preconfigured agent uses. On `StartAsync` it:

1. expands `${AGENTHELM_DIR}` in the catalog entry's `Command` and `Args`;
2. builds the environment: the entry's `Environment` plus, when the session has a model, `GH_COPILOT_MODEL`, `CLAUDE_MODEL` and `GEMINI_MODEL`;
3. starts the process through `ProcessTransport` and wires an `AcpClient` to it;
4. sends `initialize`, then `session/new` — or `session/load` with the archived id when resuming — and remembers the agent's session id as `NativeSessionId`.

It then translates ACP's `session/update` kinds into `AgentEvent`s and ACP permission requests into `PermissionAsk`s; the tables are on the [Agent Client Protocol](acp.md) page.

## The adapter factory

`SessionManager` picks the adapter from the catalog entry's `Type`:

| `Type` | Adapter |
|---|---|
| `acp` (or empty) | `AcpAdapter` |
| `copilot-sdk` | `CopilotSdkAdapter` — only when the Bridge was built with the `COPILOT_SDK` symbol |
| anything else | error: *Unknown agent type '…' for agent '…'. Supported: acp, copilot-sdk.* |

Without the `COPILOT_SDK` symbol, a `copilot-sdk` agent fails at session start with a message that says what is missing, instead of failing somewhere deep inside. Both error paths are covered by tests.

## The Copilot SDK adapter

[`Agents/CopilotSdk/CopilotSdkAdapter.cs`](https://github.com/konradcinkusz/agent-helm/blob/master/src/AgentHelm.Bridge/Agents/CopilotSdk/CopilotSdkAdapter.cs) is a **structured skeleton**, compiled only when `COPILOT_SDK` is defined. Copilot users are fully served today through ACP (`copilot --acp --stdio`); the SDK adapter would add Copilot-specific depth:

- model selection per session through the SDK's session configuration,
- resume through SDK-native session ids,
- registration of custom tools, agents and skills,
- a telemetry configuration pointed at the local CopilotScope collector.

It is gated rather than finished because the SDK is in public preview, its package could not be restored where the adapter was written, and the CLI underneath has already shipped a silent breaking change ([github/copilot-cli#1606](https://github.com/github/copilot-cli/issues/1606)). Shipping code that pretends to be done would be worse than an honest skeleton.

To activate it, on a machine with access to the preview package:

1. In `AgentHelm.Bridge.csproj`, add a `PackageReference` to the GitHub Copilot SDK and `<DefineConstants>$(DefineConstants);COPILOT_SDK</DefineConstants>`.
2. Fill in the four `TODO` blocks against the SDK's documentation: create the client, create or resume the session, translate SDK events into `AgentEvent`s, and route the SDK's permission callback to `PermissionHandler`.
3. Bring `PromptAsync` in line with the interface: the skeleton predates attachments and still declares `PromptAsync(string, CancellationToken)`.
4. Pin the Copilot CLI to a known-good version and run it with `--no-auto-update`.
5. Add a catalog entry with `"Type": "copilot-sdk"`.

## Adding an adapter

A new way of driving agents — another SDK, a remote protocol — needs:

1. a class implementing `IAgentAdapter`, translating its protocol into `AgentEvent`s and `PermissionAsk`s;
2. a `case` in `SessionManager.BuildAdapter` for a new `Type` value;
3. tests next to `AgentFactoryTests` and, for the protocol itself, a scripted transport like the ACP tests use;
4. a row in the [agent catalog reference](../configuration/agent-catalog.md#type-acp-or-copilot-sdk).

Keep protocol details inside the adapter. The session layer should not need to change.
