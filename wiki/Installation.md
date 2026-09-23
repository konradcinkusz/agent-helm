AgentHelm is two .NET 8 processes — the **Bridge** (API, sessions, agents) and the **Web** UI — plus an optional PostgreSQL database for session history. Pick the option that matches what you want to do:

| Option | You need | Real agents | Persistent history | Best for |
|---|---|:---:|:---:|---|
| [Release zip](#release-zip) | .NET 8 ASP.NET Core runtime | ✓ | opt-in | Using AgentHelm |
| [From source with Aspire](#from-source-with-aspire) | .NET SDK 8.0.303+, Docker | ✓ | ✓ | Development, the full stack |
| [From source with `dotnet run`](#from-source-with-dotnet-run) | .NET SDK 8 | ✓ | opt-in | Development without Docker |
| [Containers from GHCR](#containers-from-ghcr) | Docker | echo only | ✓ | A quick look at the UI |
| [Containers built from source](#containers-built-from-source) | Docker | echo only | ✓ | Testing the images |

> **Important:** Real agents (Copilot CLI, Claude Code, Gemini CLI) run as **child processes of the Bridge**, with the Bridge's environment, credentials and file system. That is why every option that can drive real agents runs the Bridge directly on your machine, and why the container options only offer the built-in echo agent. [Deployment & topology](Deployment-and-Topology.md) explains the constraint in full.

## Release zip

Each [GitHub release](https://github.com/konradcinkusz/agent-helm/releases) ships a portable, framework-dependent zip (`agenthelm-vX.Y.Z.zip`) that runs on any OS with the **.NET 8 ASP.NET Core runtime** — no SDK, no build, no Docker.

1. Download the latest `agenthelm-*.zip` from the [releases page](https://github.com/konradcinkusz/agent-helm/releases/latest) and unpack it.
2. Start both processes with the launcher:

    - macOS / Linux: `./run.sh`
    - Windows (PowerShell): `.\run.ps1`

3. Open **<http://127.0.0.1:5200>**. The Bridge listens on `http://127.0.0.1:5199`.

The zip contains `bridge/`, `web/` and `echo-agent/` (the published applications), the two launchers, and `docker-compose.ghcr.yml`. The launchers start the Bridge in the background and the Web UI in the foreground; stopping the UI (<kbd>Ctrl</kbd>+<kbd>C</kbd>) stops the Bridge too.

| Launcher variable | Default | Effect |
|---|---|---|
| `AGENTHELM_WEB_URLS` | `http://127.0.0.1:5200` | Where the Web UI listens. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4318` | Where agents export OpenTelemetry (the [CopilotScope](CopilotScope-Integration.md) collector by default). |
| `OTEL_EXPORTER_OTLP_HEADERS` | `x-api-key=dev-secret-123` | Headers sent with that telemetry. |

The launchers also set `COPILOT_OTEL_ENABLED=true` and the OTLP protocol variables, so every agent the Bridge starts inherits them. Without a database the zip runs **memory-only**: sessions work normally but are gone when the Bridge stops. To keep history, point the Bridge at a PostgreSQL database with `ConnectionStrings__helmdb` (see [configuration](Configuration.md#bridge-settings)).

> **Warning:** **Releases v1.0.0 to v1.0.5:** their launchers start both applications from the zip's top-level folder, so the Bridge does not find its `appsettings.json` (the UI shows *connecting…* and no agents) and the UI does not find its styles. This is fixed for the next release; with one of these zips, start the two processes yourself — see [Troubleshooting → Known issues](Troubleshooting-and-FAQ.md#the-release-zip-starts-without-agents-and-without-styles).

## From source with Aspire

The recommended way to develop. [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) starts PostgreSQL in a container and runs the Bridge and the Web UI as local processes.

**Prerequisites:** .NET SDK **8.0.303 or newer** (Aspire ships as an MSBuild SDK from NuGet — no `aspire` workload), and Docker (or another container runtime Aspire supports) for PostgreSQL.

```bash
git clone https://github.com/konradcinkusz/agent-helm.git
cd agent-helm
dotnet run --project src/AgentHelm.AppHost
```

The console prints a login link to the **Aspire dashboard**. Its *Resources* page lists:

| Resource | What it is |
|---|---|
| `postgres` | PostgreSQL container with the persistent volume `agenthelm-pgdata`, plus pgAdmin. |
| `helmdb` | The database the Bridge uses; its connection string is injected into the Bridge. |
| `bridge` | The Bridge, a local process on the fixed port **5199**. |
| `web` | The Web UI, a local process — open the URL shown next to it. |

> **Tip:** The default launch profile uses HTTPS and the ASP.NET Core development certificate. If your browser complains about the certificate, run `dotnet dev-certs https --trust` once, or start with `dotnet run --project src/AgentHelm.AppHost --launch-profile http`.

If `dotnet run` fails with *"Aspire Workload has been deprecated"* or the Bridge cannot log in to PostgreSQL, see [Troubleshooting](Troubleshooting-and-FAQ.md#aspire-workload-has-been-deprecated).

## From source with `dotnet run`

No Docker, no Aspire — two terminals:

```bash
dotnet run --project src/AgentHelm.Bridge   # API on http://127.0.0.1:5199
dotnet run --project src/AgentHelm.Web      # UI on the launch-profile URL
```

The Web project's launch profile listens on `https://localhost:53168` and `http://localhost:53171` (the console prints *Now listening on…*). The Web UI finds the Bridge at `http://127.0.0.1:5199` by default. History is memory-only unless you give the Bridge a `ConnectionStrings:helmdb` connection string.

## Containers from GHCR

Every release publishes two images to the GitHub Container Registry: `ghcr.io/konradcinkusz/agenthelm-bridge` and `ghcr.io/konradcinkusz/agenthelm-web` (tags: the version, `major.minor` and `latest`). You need only one file from the repository:

**macOS / Linux / Git Bash**

```bash
curl -O https://raw.githubusercontent.com/konradcinkusz/agent-helm/master/docker-compose.ghcr.yml
docker compose -f docker-compose.ghcr.yml up
```

**Windows (PowerShell)**

```powershell
curl.exe -O https://raw.githubusercontent.com/konradcinkusz/agent-helm/master/docker-compose.ghcr.yml
docker compose -f docker-compose.ghcr.yml up
```

The UI is at **<http://localhost:5300>**, the Bridge API at `http://localhost:5299` (token `dev-secret-123`), and PostgreSQL keeps history in the `agenthelm-pgdata` volume. The Bridge image includes the echo agent; real agents are not available inside the container.

To use your own repositories as working directories, mount them into the Bridge container and use the **container** path in the UI:

```yaml
  bridge:
    volumes:
      - /home/you/repos:/repos      # Linux / macOS
      # - C:\Repos:/repos           # Windows
```

> **Caution:** The compose files publish their ports on **all** network interfaces and use a sample API token. On any machine that other devices can reach, bind the ports to loopback (`"127.0.0.1:5299:5199"`, `"127.0.0.1:5300:8080"`) and replace `dev-secret-123` in both `AgentHelm__ApiToken` and `Bridge__ApiToken`. The Bridge includes a terminal endpoint; see the [security model](Security.md#hardening-checklist).

## Containers built from source

The repository's own `docker-compose.yml` builds the same two images locally:

```bash
docker compose up --build
```

Ports, token, volumes and limitations are the same as for the [GHCR images](#containers-from-ghcr).

## Check that it works

The Bridge answers a health probe and logs a banner with the configured agents when it starts:

```console
$ curl -s http://127.0.0.1:5199/api/health
{"status":"ok","sessions":0}
```

If you enabled an API token, send it with the request: `curl -H "x-helm-token: <token>" …`. Then [run your first session](Your-First-Session.md).
