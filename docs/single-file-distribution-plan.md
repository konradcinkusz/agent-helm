# Plan: AgentHelm as ONE installable file

Requirement: the distribution of AgentHelm is a single file per OS that the user downloads and runs — no zip, no .NET runtime, no second process to start.

Today: the release is a zip with `bridge/`, `web/`, `echo-agent/` and launcher scripts (`scripts/run.sh`, `scripts/run.ps1`) that start two ASP.NET apps and need the .NET 8 runtime.

## Target

- `agenthelm-<version>-<rid>` (`.exe` on Windows) for `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`: **self-contained, single-file** executable (`PublishSingleFile`, `SelfContained`, no trimming of the ASP.NET/Blazor assemblies).
- ONE process: the Bridge API (`/api/*`) and the Blazor UI (`/`) are served from the same host on one loopback URL (default `http://127.0.0.1:5199`, overridable with `AgentHelm__Urls`/`--urls`). The UI's `BridgeClient` talks to its own host.
- The built-in echo demo agent is the same binary started with a sub-command (`agenthelm echo-agent`), so nothing else has to ship next to it. The default agent catalog entry for `echo` launches `<this executable> echo-agent`.
- The UI's static files (`wwwroot`, scoped CSS bundle, `*.js`) are inside the single file (embedded or self-extracted to a per-user cache dir on first run); no files next to the executable are required. A user `appsettings.json` / env vars next to it or in the user profile remain optional overrides.
- First run prints the URL and (where possible) opens the browser; `--no-browser` disables it. Persistence stays optional (Postgres) — memory-only by default, as today.
- Docker images (GHCR) keep working; the existing multi-project layout (AppHost, separate Bridge/Web) must keep building because Aspire/Docker use it.

## Tasks

- **T15 – One process (+ single-file publish).** New project `src/AgentHelm.App` (Sdk.Web) referencing `AgentHelm.Bridge` and `AgentHelm.Web`. Move each project's `Program.cs` composition into reusable extension methods (`AddAgentHelmBridge`/`MapAgentHelmBridge` in Bridge, `AddAgentHelmWeb`/`MapAgentHelmWeb` in Web) that the existing `Program.cs` files keep calling, so AppHost/Docker behave exactly as before. `AgentHelm.App` calls both, configures `BridgeClient` to the own address, supports `echo-agent` sub-command (move/reference the EchoAgent code), `--no-browser`, and is added to `AgentHelm.sln`. Add `scripts/build-single-file.sh <rid> <outdir>` that runs `dotnet publish src/AgentHelm.App -c Release -r <rid> --self-contained -p:PublishSingleFile=true …` and yields exactly one file `<outdir>/agenthelm[.exe]`; make static web assets and appsettings work from inside the single file. Add tests where practical. Keep `ci.yml` green.
- **T16 – CI smoke test of the single file** (depends on T15): in `ci.yml` build `linux-x64` with the script, run it from an empty directory, assert `/` serves the UI with its CSS/JS (HTTP 200), `/api/health` is OK, and the echo agent completes a turn through the API; assert the output directory contains only that one file. Replace/extend the old `release-layout` job accordingly (keep its name stable if it is a required check).
- **T17 – Release workflow** (depends on T15): `release.yml` builds the four RIDs in a matrix with the script, names them `agenthelm-<tag>-<rid>[.exe]`, adds `SHA256SUMS`, attaches them to the GitHub Release; the old zip is no longer produced. macOS binaries ad-hoc signed if feasible; document the Gatekeeper/SmartScreen first-run steps.
- **T18 – Docs** (depends on T15–T17): README + wiki: "Install" = download one file, `chmod +x` (Unix), run; where config/data live; how to override URL/token; remove the zip/`run.sh` instructions; update Deployment/Development pages and `scripts/` references. Run `python3 scripts/wiki.py check`.
