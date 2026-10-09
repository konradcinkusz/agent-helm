## Known issues

No defects are recorded against the current release. An entry is added when a confirmed defect has no fix yet, and removed by the change that fixes it.

## Starting the single file

### macOS or Windows refuses to open the file

macOS Gatekeeper and Windows SmartScreen block files downloaded from the internet. Run `xattr -d com.apple.quarantine <file>` on macOS, or choose **More info → Run anyway** on Windows. The steps are in [Installation → First run](Installation.md#first-run).

### "Permission denied" on Linux or macOS

The file is not executable. Run `chmod +x <file>`. If it still will not start, check that it is not on a folder mounted `noexec`; copy it to your home folder.

### Check that the file is the one you downloaded

Compare its SHA-256 with the line in the release's `SHA256SUMS` ([Installation → Verify the download](Installation.md#verify-the-download)). A mismatch means a damaged or altered download; download it again.

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
4. **Does the Bridge have agents?** Its startup banner lists them. An empty list means the agent catalog did not load: from source, check `src/AgentHelm.Bridge/appsettings.json`; with the single file, download the release again.

### The Bridge answers 401

A token is configured (`AgentHelm:ApiToken`) and the request did not carry it. Send `x-helm-token: <token>` with API calls, and set `Bridge:ApiToken` for the Web UI.

### "Address already in use"

Another program uses port 5199. The single file has one address: start it with `--urls http://127.0.0.1:5300` or `AgentHelm__Urls=http://127.0.0.1:5300`. From source, move the Bridge with `AgentHelm:Urls` *and* point the UI at it with `Bridge:BaseUrl`, and move the UI with `ASPNETCORE_URLS`. Under Aspire the Bridge's port 5199 is fixed in `AgentHelm.AppHost/Program.cs`.

### History is empty

History needs PostgreSQL. Without `ConnectionStrings:helmdb` the Bridge runs memory-only; if the database was unreachable at startup, the Bridge logged *Postgres unavailable — running memory-only* and runs without history until restarted. Sessions are saved once something happens in them — a session that was never used is not archived. See [History & resume](History-and-Resume.md).

## Agents and sessions

### "Working directory does not exist"

The path is checked on the **Bridge's** machine. If the Bridge runs in Docker, mount the directory (`- /home/you/repos:/repos`) and enter the container path (`/repos/myproject`). See [Deployment & topology](Deployment-and-Topology.md#working-directories).

### "Unknown agent '…'"

No catalog entry has that id. Check `AgentHelm:Agents` in the Bridge's `appsettings.json` and restart the Bridge — the catalog is read only at startup.

### "Could not start agent '…'"

The operating system could not start the command — usually because it is not on the **Bridge's** `PATH`, which can differ from your terminal's (for example when the Bridge is started from an IDE or a service). Use an absolute path in `Command`. On Windows, npm-installed CLIs need their `.cmd` name (`npx.cmd`, `gemini.cmd`). See [Connecting agents](Connecting-Agents.md#when-an-agent-does-not-start).

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

### The Changes panel says "not a git repository"

The working directory is not inside a git work tree, or `git` is not on the Bridge's `PATH`. Inside the container images there is no `git` at all.

### Full-screen programs, Ctrl+C or colours in the Terminal

Input is sent a line at a time, so full-screen programs and <kbd>Ctrl</kbd>+<kbd>C</kbd> are not available, and in pipe mode (Windows, macOS) many tools disable colours. See [Terminal](Terminal.md).

### "CopilotScope is not reachable"

Nothing answered at `AgentHelm:Scope:BaseUrl` (default `http://localhost:4318`). Start CopilotScope, or correct the URL through **⚙ Pre-config**. After a failure the Bridge waits 60 seconds before trying again.

### A provider login hangs

The login command waits for input or a browser on the Bridge's machine. Run the same command in a terminal there, then click **Refresh** on the Providers page.

## Containers

### Real agents are missing in Docker

By design: agents need their binaries and your logins, which the container does not have. The container images demonstrate the UI with the echo agent; use the single file or run from source for real agents.

### `docker pull` is denied

Freshly published GHCR packages are private. The maintainer has to make `agenthelm-bridge` and `agenthelm-web` public once.

## FAQ

**Is AgentHelm a hosted service?**
No. It runs on your machine; there is no AgentHelm server and no account.

**Do I need .NET installed?**
No for the [single file](Installation.md#single-file): it contains the runtime. You need the .NET 8 SDK only to build from source.

**Does AgentHelm send my code anywhere?**
AgentHelm itself only talks to the agents it starts, to PostgreSQL if configured, and to CopilotScope if it is running. The agents talk to their own model providers, exactly as they do in a terminal. With the preconfigured Copilot entry, telemetry *including prompt content* goes to the local CopilotScope endpoint — see [CopilotScope integration](CopilotScope-Integration.md).

**Which agents work?**
Any agent that speaks ACP over stdio. GitHub Copilot CLI, Claude Code (through Zed's adapter) and Gemini CLI are preconfigured.

**Which operating systems?**
The single file is published for Linux x64, Windows x64 and macOS (Apple silicon and Intel). From source, anywhere .NET 8 runs: Windows, Linux and macOS. The terminal has a real PTY on Linux and is a pipe on Windows and macOS.

**Can a team share one AgentHelm?**
Not safely. AgentHelm is a single-user tool: it has no user accounts, only a shared token, and every agent runs as the user running the Bridge.

**How is it different from an agent's own terminal UI?**
One UI for many agents, an explicit and audited permission layer, a review-and-revert workflow for changes, handoff between agents, and history with resume.

**What is CopilotScope?**
The sibling project that scores sessions from telemetry — *Scope observes, Helm steers*. See [CopilotScope integration](CopilotScope-Integration.md).
