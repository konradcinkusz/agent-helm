# Contributing to AgentHelm

Thank you for your interest in contributing. This guide covers everything you need to get started.

## Prerequisites

- .NET SDK **8.0.303 or newer** (`dotnet --version`)
- Docker (optional — only needed for the Postgres history backend)
- Node.js (optional — only needed to test the Claude Code ACP adapter)

## Setting up the dev environment

```bash
git clone https://github.com/konradcinkusz/AgentHelm.git
cd AgentHelm

# Option A — with Aspire (starts Postgres + Bridge + Web automatically)
dotnet run --project src/AgentHelm.AppHost

# Option B — without Docker
dotnet run --project src/AgentHelm.Bridge   # http://127.0.0.1:5199
dotnet run --project src/AgentHelm.Web      # https://localhost:53168 (launch profile)
```

The built-in echo agent works without any external tools, so you can test the full permission/audit loop immediately.

## Running tests

```bash
dotnet test
```

The suite covers the ACP client, session layer, policy engine, git service, terminal, agent handoff, Scope integration, and more. All tests should pass before you open a PR.

## Project layout

```
src/
  AgentHelm.AppHost   .NET Aspire orchestrator
  AgentHelm.Bridge    ASP.NET Core API backend (sessions, ACP, permissions, git)
  AgentHelm.Web       Blazor Server frontend
tests/
  AgentHelm.Tests     xUnit test suite
tools/
  AgentHelm.EchoAgent Built-in demo ACP agent
```

## How to contribute

1. **Open an issue first** for any non-trivial change — a quick conversation avoids duplicate work.
2. Fork the repo and create a branch: `git checkout -b your-feature`.
3. Make your changes. Keep commits focused; one logical change per commit is easier to review.
4. Add or update tests for any new behaviour. The test suite is the specification.
5. Run `dotnet test` — all tests must pass.
6. If the change affects behaviour, configuration, the API or the workflows, update the matching page of the documentation wiki in the same PR (see below).
7. Open a pull request against `master`. Describe *why* the change is needed, not just what it does.

## Documentation

The [documentation wiki](https://konradcinkusz.github.io/agent-helm/) is built from `docs/` with MkDocs (Material theme) and deployed to GitHub Pages when a docs change reaches `master`. To preview it locally:

```bash
python3 -m venv .venv
source .venv/bin/activate            # Windows: .venv\Scripts\activate
pip install -r docs/requirements.txt
mkdocs serve                         # http://127.0.0.1:8000
```

Pull requests that touch `docs/` or `mkdocs.yml` run `mkdocs build --strict`, which fails on broken links, missing anchors and pages left out of the navigation. See [Editing the docs](https://konradcinkusz.github.io/agent-helm/development/documentation/) for the conventions.

## Code style

- Standard C# formatting; nullable reference types are enabled — keep it that way.
- No unnecessary comments. The code is the documentation; add a comment only when the *why* is not obvious from the code.
- The `IAgentAdapter` seam is intentional — new agent adapters belong there, not scattered across the session layer.

## Reporting bugs

Open a GitHub issue with:
- Steps to reproduce (exact commands / clicks)
- Expected vs. actual behaviour
- .NET SDK version (`dotnet --version`) and OS
- Relevant Bridge or Web logs (start both processes; logs go to stdout)

## Feature requests

Open a GitHub issue describing the use case. The roadmap is tracked in the README — check there first to see if the idea is already planned.

## License

By contributing you agree that your changes will be licensed under the [MIT License](LICENSE).
