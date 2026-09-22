---
description: Known issues, fixes for common problems with installation, agents, sessions and features, and frequently asked questions.
---

# Troubleshooting & FAQ

## Known issues

Defects confirmed in the current code or in the latest release, with workarounds where one exists. Each entry should be removed by the change that fixes it — or, for released artifacts, once a release with the fix is out.

### The release zip starts without agents and without styles

**Affects:** the `run.sh` / `run.ps1` launchers in releases v1.0.0 to v1.0.5. The launchers on `master` are fixed, and CI now starts the release layout on every pull request; the next release ships the fix.

**Symptoms:** the UI is unstyled and says *connecting…*; the new-session form has no agents; the Bridge's startup banner shows an empty `Agents :` line and the Web UI logs *The WebRootPath was not found*.

**Cause:** the launchers start `dotnet bridge/AgentHelm.Bridge.dll` and `dotnet web/AgentHelm.Web.dll` from the zip's top-level folder. ASP.NET Core uses the current directory as the content root, so the Bridge does not load `bridge/appsettings.json` (where the agent catalog lives) and the Web UI does not find `web/wwwroot`.

**Workaround:** start each application from its own folder. In the unpacked zip:

=== "macOS / Linux"

    ```bash
    (cd bridge && dotnet AgentHelm.Bridge.dll) &
    (cd web && ASPNETCORE_URLS=http://127.0.0.1:5200 dotnet AgentHelm.Web.dll)
    ```

=== "Windows (PowerShell)"

    ```powershell
    Start-Process dotnet -ArgumentList AgentHelm.Bridge.dll -WorkingDirectory bridge -NoNewWindow
    Set-Location web
    $env:ASPNETCORE_URLS = "http://127.0.0.1:5200"
    dotnet AgentHelm.Web.dll
    ```

This skips the OpenTelemetry variables the launchers export; set them yourself if you use [CopilotScope](../guide/copilotscope.md).

## Installation and startup

### "Aspire Workload has been deprecated"

The tooling tried to resolve the old Aspire workload. This repository uses the SDK-based setup (`<Sdk Name="Aspire.AppHost.Sdk" …/>` in `AgentHelm.AppHost.csproj`), which needs the .NET SDK **8.0.303 or newer**. Update the SDK and optionally remove the stale workload with `dotnet workload uninstall aspire`. When updating Aspire, the `Sdk` element version and the `Aspire.Hosting.*` package versions must change together.

### PostgreSQL "password authentication failed" under Aspire

Aspire generates the PostgreSQL password and keeps it in user secrets (the AppHost has a `UserSecretsId`), so it matches the data volume `agenthelm-pgdata` on the next run. If the volume was initialised with a different password — for example by a version of the AppHost without the `UserSecretsId` — either remove the volume, which deletes the history:

```bash
docker volume rm agenthelm-pgdata
```

…or set the user secret `Parameters:postgres-password` of the AppHost project to the password the volume was created with.

### The UI says "connecting…" and lists no agents

The Web UI could not get the agent list from the Bridge. Check, in order:

1. **Is the Bridge running?** `curl http://127.0.0.1:5199/api/health` should answer.
2. **Can the Web server reach it?** `Bridge:BaseUrl` must point to the Bridge from where the Web server runs (default `http://127.0.0.1:5199`).
3. **Do the tokens match?** With `AgentHelm:ApiToken` set on the Bridge, the Web UI needs the same value as `Bridge:ApiToken`; otherwise every call gets `401`.
4. **Does the Bridge have agents?** Its startup banner lists them. An empty list means it did not load its `appsettings.json` — see the [release zip known issue](#the-release-zip-starts-without-agents-and-without-styles).

### The Bridge answers 401

A token is configured (`AgentHelm:ApiToken`) and the request did not carry it. Send `x-helm-token: <token>` with API calls, and set `Bridge:ApiToken` for the Web UI.

### "Address already in use"

Another program uses port 5199 (Bridge) or 5200 (UI). Move the Bridge with `AgentHelm:Urls` *and* point the UI at it with `Bridge:BaseUrl`; move the UI with `AGENTHELM_WEB_URLS` (release zip) or `ASPNETCORE_URLS`. Under Aspire the Bridge's port 5199 is fixed in `AgentHelm.AppHost/Program.cs`.

### History is empty

History needs PostgreSQL. Without `ConnectionStrings:helmdb` the Bridge runs memory-only; if the database was unreachable at startup, the Bridge logged *Postgres unavailable — running memory-only* and runs without history until restarted. Sessions are saved once something happens in them — a session that was never used is not archived. See [History & resume](../guide/history.md).

## Agents and sessions

### "Working directory does not exist"

The path is checked on the **Bridge's** machine. If the Bridge runs in Docker, mount the directory (`- /home/you/repos:/repos`) and enter the container path (`/repos/myproject`). See [Deployment & topology](../configuration/deployment.md#working-directories).

### "Unknown agent '…'"

No catalog entry has that id. Check `AgentHelm:Agents` in the Bridge's `appsettings.json` and restart the Bridge — the catalog is read only at startup.

### "Could not start agent '…'"

The operating system could not start the command — usually because it is not on the **Bridge's** `PATH`, which can differ from your terminal's (for example when the Bridge is started from an IDE or a service). Use an absolute path in `Command`. On Windows, npm-installed CLIs need their `.cmd` name (`npx.cmd`, `gemini.cmd`). See [Connecting agents](../getting-started/agents.md#when-an-agent-does-not-start).

### A session stays on "Starting…"

The agent process started but never answered the ACP handshake, and the Bridge does not time out. Common causes: the arguments do not switch the CLI into ACP mode (check `gemini --help`, for example), or the CLI is waiting for an interactive login. Enable the agent's standard-error log with `Logging__LogLevel__agent=Debug` and restart the Bridge to see what the agent says.

### "Agent error: …" or "Agent connection closed."

The agent returned an error for the prompt, or its process exited. The agent's standard error, logged at `Debug` under `agent.<id>`, usually says why — often expired authentication or a quota.

### Copilot CLI stopped working after an update

The Copilot CLI updates itself and has removed interfaces without deprecation before ([github/copilot-cli#1606](https://github.com/github/copilot-cli/issues/1606)). Pin a known-good version, run it with `--no-auto-update`, and point the catalog entry at that binary.

### Resume is disabled or fails

- *Disabled, "This archive predates resume support"* — the archive has no agent-side session id.
- *"Resume failed (the agent may not support session/load)"* — the agent does not advertise `loadSession` (no `resume` chip), or it no longer has that session stored.

### "Session is already running a turn."

A prompt was sent while the previous turn was still running. Wait for it to finish, or press **Stop**.

## Features

### The Changes tab says "not a git repository"

The working directory is not inside a git work tree, or `git` is not on the Bridge's `PATH`. Inside the container images there is no `git` at all.

### Full-screen programs, Ctrl+C or colours in the Terminal

Input is sent a line at a time, so full-screen programs and ++ctrl+c++ are not available, and on Windows (pipe mode) many tools disable colours. See [Terminal](../guide/terminal.md).

### "CopilotScope is not reachable"

Nothing answered at `AgentHelm:Scope:BaseUrl` (default `http://localhost:4318`). Start CopilotScope, or correct the URL through **⚙ Pre-config**. After a failure the Bridge waits 60 seconds before trying again.

### A provider login hangs

The login command waits for input or a browser on the Bridge's machine. Run the same command in a terminal there, then click **Refresh** on the Providers page.

## Containers

### Real agents are missing in Docker

By design: agents need their binaries and your logins, which the container does not have. The container images demonstrate the UI with the echo agent; use the release zip or run from source for real agents.

### `docker pull` is denied

Freshly published GHCR packages are private. The maintainer has to make `agenthelm-bridge` and `agenthelm-web` public once.

## FAQ

**Is AgentHelm a hosted service?**
No. It runs on your machine; there is no AgentHelm server and no account.

**Does AgentHelm send my code anywhere?**
AgentHelm itself only talks to the agents it starts, to PostgreSQL if configured, and to CopilotScope if it is running. The agents talk to their own model providers, exactly as they do in a terminal. With the preconfigured Copilot entry, telemetry *including prompt content* goes to the local CopilotScope endpoint — see [CopilotScope integration](../guide/copilotscope.md).

**Which agents work?**
Any agent that speaks ACP over stdio. GitHub Copilot CLI, Claude Code (through Zed's adapter) and Gemini CLI are preconfigured.

**Which operating systems?**
Windows, Linux and macOS — anywhere .NET 8 runs. The terminal has a real PTY on Linux and is a pipe on Windows; it has not been verified on macOS.

**Can a team share one AgentHelm?**
Not safely. AgentHelm is a single-user tool: it has no user accounts, only a shared token, and every agent runs as the user running the Bridge.

**How is it different from an agent's own terminal UI?**
One UI for many agents, an explicit and audited permission layer, a review-and-revert workflow for changes, handoff between agents, and history with resume.

**What is CopilotScope?**
The sibling project that scores sessions from telemetry — *Scope observes, Helm steers*. See [CopilotScope integration](../guide/copilotscope.md).
