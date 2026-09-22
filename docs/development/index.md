---
description: Set up a development environment for AgentHelm, find your way around the code, and contribute changes.
---

# Development guide

Everything you need to work on AgentHelm itself. The short version lives in [`CONTRIBUTING.md`](https://github.com/konradcinkusz/agent-helm/blob/master/CONTRIBUTING.md); this page adds the map.

## Prerequisites

- .NET SDK **8.0.303 or newer** (`dotnet --version`) — the AppHost needs it for the Aspire SDK; the other projects build with any .NET 8 SDK.
- Docker — optional, for PostgreSQL through Aspire.
- Node.js — optional, to try the Claude Code adapter.
- Python 3 — optional, to preview this documentation ([Editing the docs](documentation.md)).

## Get it running

```bash
git clone https://github.com/konradcinkusz/agent-helm.git
cd agent-helm

# Everything, with PostgreSQL (needs Docker):
dotnet run --project src/AgentHelm.AppHost

# …or without Docker, in two terminals:
dotnet run --project src/AgentHelm.Bridge   # http://127.0.0.1:5199
dotnet run --project src/AgentHelm.Web      # https://localhost:53168
```

The built-in echo agent needs nothing else, so you can exercise sessions, streaming, permissions and resume immediately. [Installation](../getting-started/installation.md) covers the options in detail.

## Repository layout

```text
AgentHelm.sln
src/
  AgentHelm.AppHost/        .NET Aspire orchestration (Postgres container, Bridge + Web as local processes)
  AgentHelm.Bridge/         the API: sessions, agents, permissions, git, terminal, persistence
    Agents/Acp/             AcpClient, ProcessTransport — the ACP client and path guard
    Agents/CopilotSdk/      CopilotSdkAdapter — gated skeleton (COPILOT_SDK)
    Integrations/           ScopeClient — CopilotScope
    Persistence/            SessionRepository, PersistenceWriter — PostgreSQL snapshots
    Providers/              ProviderAccountService — agent CLI accounts, models, login
    Sessions/               SessionManager, HelmSession, AcpAdapter, PermissionPolicy
    Workbench/              GitService, TerminalService
    Program.cs              endpoints, token middleware, DI
  AgentHelm.Web/            Blazor Server UI
    Components/Pages/       Home (dashboard), Settings (providers)
    Services/BridgeClient.cs  the only way the UI talks to the Bridge
    wwwroot/                app.css, terminal.js (xterm.js host, folder picker)
tests/AgentHelm.Tests/      xUnit tests (CoreTests.cs)
tools/AgentHelm.EchoAgent/  the built-in demo ACP agent
scripts/                    run.sh / run.ps1 — release launchers; build- and smoke-release-layout.sh
docs/                       this wiki (MkDocs) and working notes
Dockerfile, Dockerfile.web  container images; docker-compose*.yml
```

The [architecture overview](../architecture/index.md) explains how the pieces fit together.

## Conventions

- **Nullable reference types are on** — keep them on and keep the build warning-free.
- **Comments explain *why*.** The code says what; add a comment when the reason is not obvious (the existing code is full of good examples).
- **New agent integrations go behind `IAgentAdapter`**, not into the session layer — see [Agent adapters](../architecture/adapters.md).
- **Dependencies are a cost.** The Bridge has one NuGet dependency (Npgsql); propose a new one in an issue first.
- **Security-relevant code is conservative.** The path guard, the policy engine and the git reject logic fail closed; changes there need tests that prove they still do.

### Protected paths

Some files carry more weight than their size suggests. A change to one of them is never merged past a red CI run ([ROADMAP.md §5](https://github.com/konradcinkusz/agent-helm/blob/master/ROADMAP.md#5-protected-paths)):

| Path | Why |
|---|---|
| `.github/workflows/**` | The verification pipeline itself. |
| `AgentHelm.sln`, `**/*.csproj` | The build graph. |
| `src/AgentHelm.Bridge/Sessions/PermissionPolicy.cs` | The no-auto-reject invariant and the policy taxonomy. |
| `src/AgentHelm.Bridge/Agents/Acp/AcpClient.cs` | The working-directory path guard. |
| `src/AgentHelm.Bridge/Workbench/GitService.cs` | The same guard for git, and server-side tracked/untracked decisions. |

## Contributing a change

1. **Open an issue first** for anything non-trivial — a short conversation saves duplicate work.
2. Fork and branch: `git checkout -b your-change`.
3. Keep commits focused; one logical change per commit.
4. **Add or update tests** for new behaviour — the test suite is the specification. See [Testing](testing.md).
5. Run `dotnet test`; everything must pass.
6. If behaviour or configuration changes, update this wiki in the same pull request ([Editing the docs](documentation.md)).
7. Open a pull request against `master` and explain *why* the change is needed, not only what it does. Pull requests are squash-merged.

CI builds the solution and runs the tests on every pull request; pull requests that touch the docs also build the site in strict mode. See [CI/CD & releases](ci-cd.md).

## Reporting bugs and ideas

Use the issue templates. For a bug, include the steps (commands and clicks), expected and actual behaviour, `dotnet --version` and your OS, and the Bridge and Web logs (both log to stdout). Security problems go through [private reporting](../security/index.md#reporting-a-vulnerability), not issues.

By contributing you agree that your changes are licensed under the [MIT License](https://github.com/konradcinkusz/agent-helm/blob/master/LICENSE).
