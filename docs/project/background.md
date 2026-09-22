---
description: Why AgentHelm exists — the Scope/Helm pair, the integration options that were weighed, the original MVP cut and the risks named up front.
---

# Background & vision

AgentHelm began as an analysis of a question: *what would a good GUI for GitHub Copilot CLI and other coding agents look like, and should it be built next to CopilotScope?* This page summarises that analysis and how the project turned out. The original working document (in Polish) is kept in the repository as [`docs/ANALYSIS.md`](https://github.com/konradcinkusz/agent-helm/blob/master/docs/ANALYSIS.md).

## Two projects, one platform

| | CopilotScope | AgentHelm |
|---|---|---|
| Role | **observation** plane — read-only | **control** plane — interactive |
| Input | the OpenTelemetry agents already emit | agent sessions driven over ACP |
| Question | "Was that worth the tokens?" | "Run the session: prompts, permissions, diffs." |
| Risk | low — a passive receiver | high — executes tools on the user's machine |

The two share a stack — .NET Aspire, Blazor, PostgreSQL, GHCR images — and meet in two places: AgentHelm configures agents to send telemetry to CopilotScope, and shows CopilotScope's quality scores next to its sessions. Cost meters are common; showing whether a session was *worth it* is the part other agent UIs do not have.

They are separate repositories on purpose: different release cadences and very different security profiles.

## How to drive an agent

The options for talking to Copilot CLI (as of mid-2026) were weighed like this:

| Option | Mechanism | Verdict |
|---|---|---|
| GitHub Copilot SDK (.NET) | Official SDK in preview; JSON-RPC to the CLI; sessions, streaming, a permission handler, models, telemetry configuration | Promising for Copilot-specific depth — but preview, and it has broken before |
| CLI headless server | `copilot --headless --port` — a shared TCP server | A deployment variant for the SDK |
| **Agent Client Protocol** | `copilot --acp --stdio` — JSON-RPC 2.0 over stdio | **The multi-agent path** |
| One-shot `copilot -p "…"` | a single non-interactive call | Batch jobs only |
| Scraping the TUI through a PTY | parse terminal output | Rejected — fragile; SDKs and ACP exist to avoid it |

ACP settled it. The ACP registry lists dozens of agents — Claude Code (through Zed's adapter), Gemini CLI, Codex, GitHub Copilot, Goose, Junie, OpenCode, Qwen Code and more — and is co-maintained by Zed and JetBrains. ACP is to coding agents what LSP is to languages: one client implementation reaches the whole registry.

The plan originally put the Copilot SDK adapter first and ACP in the last milestone. It was turned around: ACP shipped first and serves Copilot today, while the SDK adapter became a gated skeleton — the SDK was not restorable where the code was written, and the Copilot CLI had just broken every SDK version by removing an interface without deprecation ([github/copilot-cli#1606](https://github.com/github/copilot-cli/issues/1606)).

## The constraint that shaped the architecture

ACP's remote transport (HTTP/WebSocket) is still a proposal, so every ACP agent is a **local subprocess**. The backend therefore has to run where the repositories and agent binaries are. Aspire orchestrates that well on a developer machine; "everything in Docker" does not work for real agents. This is why the Bridge is a local process and why [Deployment & topology](../configuration/deployment.md) reads the way it does.

## The MVP cut

The feature list of a full agent cockpit is months of work, so it was cut into releases that are each usable:

| Stage | Scope | Outcome |
|---|---|---|
| M0 — skeleton | Aspire, Blazor, PostgreSQL; one adapter; prompt → streamed answer → history | "I talk to an agent through my own GUI" |
| M1 — control | Permission gateway with approve/deny and audit; multiple sessions; resume | Control and transparency — the core value |
| M2 — workbench | Git diff viewer with accept/reject; terminal; attachments | The heart of a cockpit |
| M3 — multi-agent | More agents, agent choice per session, handoff | "Every agent, one cockpit" |

Deliberately out of the MVP: a canvas, plugins, full theming, and CLI↔GUI continuity beyond what ACP's `session/load` provides. All four milestones shipped; see the [roadmap](roadmap.md).

## Positioning

AgentHelm does not try to out-feature desktop cockpits built for a single agent. It differentiates on three axes:

1. **Multi-agent through ACP** — any agent in the registry, one UI.
2. **Web-first** — Windows, macOS and Linux, from a browser.
3. **The value axis** — quality scores from CopilotScope next to the session.

It is MIT-licensed from the first commit, and clean-room: inspired by features, never by anyone else's code, assets or text.

## Risks named up front

1. **SDK churn.** The Copilot SDK is in preview and has broken compatibility without warning. Mitigation: pin the CLI, isolate the SDK behind an adapter, keep ACP as the fallback — which is what happened.
2. **No remote transport in ACP.** The "backend next to the repository" architecture is forced, not chosen. When the remote transport matures, a client–server mode becomes possible.
3. **Security surface.** A browser GUI that executes tools on the machine is an attack vector — a web page can send requests to `localhost`. Loopback binding, a token and the permission gateway are the minimum, not options. See the [security model](../security/index.md).
4. **Scope creep.** Cockpit feature lists are seductive; without the M0–M3 discipline a project like this stalls at 70 %.
5. **Focus.** Two projects at 70 % are worth less than one at 100 % and one at 30 % — CopilotScope was finished and announced first.
