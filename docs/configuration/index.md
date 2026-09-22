---
description: Every AgentHelm setting — Bridge, Web UI, launchers, containers and Aspire — with defaults, and the ports in use.
---

# Configuration reference

AgentHelm uses standard ASP.NET Core configuration. Each process reads, in increasing order of precedence:

1. `appsettings.json` in its **content root** — by default the directory the process is started *from* (the project directory with `dotnet run`). Start a published build from its own folder, or pass `--contentRoot .` (a relative content root is resolved against the application's folder),
2. `appsettings.<Environment>.json`,
3. environment variables — `:` becomes `__`, so `AgentHelm:ApiToken` is `AgentHelm__ApiToken`,
4. command-line arguments — `--AgentHelm:ApiToken=…`.

## Bridge settings

Source: [`src/AgentHelm.Bridge/appsettings.json`](https://github.com/konradcinkusz/agent-helm/blob/master/src/AgentHelm.Bridge/appsettings.json).

| Key | Default | Description |
|---|---|---|
| `AgentHelm:Urls` | `http://127.0.0.1:5199` | Address(es) the Bridge listens on. Loopback only by default; widening it is a deliberate decision — read [Deployment & topology](deployment.md#exposing-the-bridge) first. |
| `AgentHelm:ApiToken` | empty (off) | Shared secret. When set, every request must carry it in the `x-helm-token` header, or gets `401`. The Web UI must be given the same value as `Bridge:ApiToken`. |
| `AgentHelm:Scope:BaseUrl` | `http://localhost:4318` | Base URL of the [CopilotScope](../guide/copilotscope.md) API. `disabled` switches the integration off. |
| `AgentHelm:Agents` | four entries | The [agent catalog](agent-catalog.md). Read once at startup. |
| `ConnectionStrings:helmdb` | none | PostgreSQL connection string for [history](../guide/history.md). Without it the Bridge is memory-only. Aspire and the compose files set it for you. |
| `Logging:LogLevel:<category>` | `Default: Information`, `Microsoft.AspNetCore: Warning` | Standard logging levels. Agents' standard error is logged at `Debug` under `agent.<id>`, so `Logging:LogLevel:agent = Debug` shows it for all agents. |

The Bridge logs a banner at startup listing the configured agents and whether the token is required.

### Enabling the API token

Give both processes the same secret — for example from one shell:

```bash
export AgentHelm__ApiToken="$(openssl rand -hex 24)"   # read by the Bridge
export Bridge__ApiToken="$AgentHelm__ApiToken"         # read by the Web UI
dotnet run --project src/AgentHelm.Bridge &
dotnet run --project src/AgentHelm.Web
```

Direct API calls then need the header: `curl -H "x-helm-token: $AgentHelm__ApiToken" http://127.0.0.1:5199/api/health`.

## Web UI settings

Source: [`src/AgentHelm.Web/appsettings.json`](https://github.com/konradcinkusz/agent-helm/blob/master/src/AgentHelm.Web/appsettings.json).

| Key | Default | Description |
|---|---|---|
| `Bridge:BaseUrl` | empty → `http://127.0.0.1:5199` | Where the Web server finds the Bridge. Under Aspire, service discovery (`services:bridge:http:0`) takes precedence. |
| `Bridge:ApiToken` | empty | Sent as `x-helm-token` on every Bridge request. Must match `AgentHelm:ApiToken`. |
| `ASPNETCORE_URLS` | per launch profile | Where the UI listens (`https://localhost:53168;http://localhost:53171` with `dotnet run`, `http://127.0.0.1:5200` from the release launchers, `http://0.0.0.0:8080` in the container image). |

The Web UI is a Blazor Server application: the **Web server** calls the Bridge, the browser never does. `Bridge:BaseUrl` must therefore be reachable from wherever the Web server runs.

## Release launchers

`run.sh` and `run.ps1` in the release zip read:

| Variable | Default | Description |
|---|---|---|
| `AGENTHELM_WEB_URLS` | `http://127.0.0.1:5200` | Passed to the Web UI as `ASPNETCORE_URLS`. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4318` | OpenTelemetry endpoint exported to agents. |
| `OTEL_EXPORTER_OTLP_HEADERS` | `x-api-key=dev-secret-123` | OpenTelemetry headers exported to agents. |

They also export `COPILOT_OTEL_ENABLED=true`, `COPILOT_OTEL_EXPORTER_TYPE=otlp-http`, the `OTEL_EXPORTER_OTLP_*_PROTOCOL` variables (`http/protobuf`) and `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true` — see [CopilotScope integration](../guide/copilotscope.md).

## Containers

| Image | Built-in settings |
|---|---|
| `agenthelm-bridge` ([`Dockerfile`](https://github.com/konradcinkusz/agent-helm/blob/master/Dockerfile)) | `AgentHelm__Urls=http://0.0.0.0:5199`, `ASPNETCORE_ENVIRONMENT=Production`; the echo agent's path is rewritten to `/app/echo-agent/AgentHelm.EchoAgent.dll`. |
| `agenthelm-web` ([`Dockerfile.web`](https://github.com/konradcinkusz/agent-helm/blob/master/Dockerfile.web)) | `ASPNETCORE_URLS=http://0.0.0.0:8080`, `ASPNETCORE_ENVIRONMENT=Production`. |

The compose files add `ConnectionStrings__helmdb`, `AgentHelm__ApiToken` and, for the UI, `Bridge__BaseUrl=http://bridge:5199` and `Bridge__ApiToken`. PostgreSQL runs as `postgres:16` with database `helmdb`, user `helm` and the password `helm-dev`, and is not published to the host.

## Aspire

[`src/AgentHelm.AppHost/Program.cs`](https://github.com/konradcinkusz/agent-helm/blob/master/src/AgentHelm.AppHost/Program.cs) wires:

- `postgres` — a PostgreSQL container with the data volume `agenthelm-pgdata` and pgAdmin, and the database `helmdb`;
- `bridge` — the Bridge project, referencing `helmdb`, on the fixed, unproxied port `5199`;
- `web` — the Web project, referencing `bridge` through service discovery.

The AppHost project has a `UserSecretsId` so that Aspire keeps the generated PostgreSQL password between runs. The `Aspire.AppHost.Sdk` version in `AgentHelm.AppHost.csproj` and the `Aspire.Hosting.*` package versions must always be changed together.

## Runtime changes

One setting can be changed without a restart: the CopilotScope URL, through the **⚙ Pre-config** field or `POST /api/config/scope-url`. It is held in memory and reverts to `AgentHelm:Scope:BaseUrl` when the Bridge restarts. Everything else, including the agent catalog, needs a restart.

## Ports at a glance

| Port | What | When |
|---|---|---|
| 5199 | Bridge API | every local option (fixed under Aspire) |
| 5200 | Web UI | release launchers |
| 53168 / 53171 | Web UI (HTTPS / HTTP) | `dotnet run` launch profile |
| 5299 → 5199 | Bridge API, host → container | Docker Compose |
| 5300 → 8080 | Web UI, host → container | Docker Compose |
| 19178 / 17187 | Aspire dashboard (HTTPS / HTTP profile) | Aspire |
| 4318 | CopilotScope collector and API | when you run CopilotScope |
