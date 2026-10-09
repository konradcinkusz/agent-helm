AgentHelm is one program: the **Bridge** (API, sessions, agents) and the **Web UI** run in the same process on one address, `http://127.0.0.1:5199` by default. Persistent history (PostgreSQL) is optional. Pick the option that matches what you want to do:

| Option | You need | Real agents | Persistent history | Best for |
|---|---|:---:|:---:|---|
| [Single file](#single-file) | nothing (the .NET runtime is inside the file) | ✓ | opt-in | Using AgentHelm |
| [From source with Aspire](#from-source-with-aspire) | .NET SDK 8.0.303+, Docker | ✓ | ✓ | Development, the full stack |
| [From source with `dotnet run`](#from-source-with-dotnet-run) | .NET SDK 8 | ✓ | opt-in | Development without Docker |
| [Containers from GHCR](#containers-from-ghcr) | Docker | echo only | ✓ | A quick look at the UI |
| [Containers built from source](#containers-built-from-source) | Docker | echo only | ✓ | Testing the images |

> **Important:** Real agents (Copilot CLI, Claude Code, Gemini CLI) run as **child processes of the Bridge**, with the Bridge's environment, credentials and file system. That is why every option that can drive real agents runs AgentHelm directly on your machine, and why the container options only offer the built-in echo agent. [Deployment & topology](Deployment-and-Topology.md) explains the constraint in full.

## Single file

Each [GitHub release](https://github.com/konradcinkusz/agent-helm/releases/latest) attaches one self-contained executable per platform. It bundles the .NET runtime, the Bridge, the Web UI and the built-in echo agent, so you need no SDK, no runtime, no build and no Docker.

1. Download the file for your system from the latest release. `<tag>` is the version, for example `v1.2.3`:

    | Your system | File |
    |---|---|
    | Linux, x64 | `agenthelm-<tag>-linux-x64` |
    | Windows, x64 | `agenthelm-<tag>-win-x64.exe` |
    | macOS, Apple silicon (M1 and later) | `agenthelm-<tag>-osx-arm64` |
    | macOS, Intel | `agenthelm-<tag>-osx-x64` |

    Each release also has a `SHA256SUMS` file for [checking the download](#verify-the-download).

2. **macOS and Linux:** make the file executable, then run it:

    ```bash
    chmod +x ./agenthelm-v1.2.3-linux-x64
    ./agenthelm-v1.2.3-linux-x64
    ```

    **Windows:** double-click the `.exe`, or run `.\agenthelm-v1.2.3-win-x64.exe` in PowerShell.

3. The console prints the address and opens your browser on it:

    ```console
    AgentHelm is running at http://127.0.0.1:5199/ (Ctrl+C to stop)
    ```

    If the browser does not open, open that address yourself. Stopping the program (<kbd>Ctrl</kbd>+<kbd>C</kbd>, or closing the console window) stops AgentHelm.

The file has no companions: nothing has to sit next to it, and it runs from any folder. The built-in echo agent is the same file started with the sub-command `echo-agent`, so the [first session](Your-First-Session.md) works immediately.

### First run

- **macOS (Gatekeeper).** A file downloaded in a browser is quarantined, and macOS may refuse to open it ("cannot be opened because the developer cannot be verified"). Remove the quarantine flag once, then run it again:

    ```bash
    xattr -d com.apple.quarantine ./agenthelm-v1.2.3-osx-arm64
    ```

    Use the file name you downloaded. Alternatively, right-click the file in Finder and choose **Open**.

- **Windows (SmartScreen).** "Windows protected your PC" appears for a file downloaded from the internet. Click **More info**, then **Run anyway**.

- **Linux.** No extra step. If the file sits on a folder mounted `noexec` (some USB drives and `/tmp` setups), copy it to your home folder first.

### Verify the download

Each release has a `SHA256SUMS` file: one line per file, `<sha256>  <file name>`. Compare the checksum of the file you downloaded with its line:

```bash
# Linux
sha256sum ./agenthelm-v1.2.3-linux-x64
grep agenthelm-v1.2.3-linux-x64 SHA256SUMS

# macOS
shasum -a 256 ./agenthelm-v1.2.3-osx-arm64
grep agenthelm-v1.2.3-osx-arm64 SHA256SUMS
```

```powershell
# Windows (PowerShell)
(Get-FileHash .\agenthelm-v1.2.3-win-x64.exe -Algorithm SHA256).Hash
Select-String agenthelm-v1.2.3-win-x64.exe SHA256SUMS
```

The two hashes must be identical (PowerShell prints upper case; compare case-insensitively).

### Settings and data

- **No setup file is needed, and AgentHelm writes no configuration file of its own.** Settings come from, in increasing order of precedence: the defaults built into the file; an optional `appsettings.json` in the *content root*, which is the folder you start the program from; environment variables; and command-line arguments. The full list is in the [settings reference](Configuration.md).
- **Sessions are kept in memory.** They are lost when AgentHelm stops, unless you give it a PostgreSQL database with `ConnectionStrings__helmdb` (see [History & resume](History-and-Resume.md)).
- **Quick chats** use a scratch folder per session under your system's temporary folder, in `AgentHelm/chats/`.
- **Agents** work in the folders you choose for their sessions. Their own logins (for example `~/.claude.json`) stay where the agent CLI keeps them; AgentHelm only reads them to show which account is signed in.

### Port and token

The default address is `http://127.0.0.1:5199`. The address and the API token are set with environment variables or arguments:

```bash
# Another port (the UI moves with it)
AgentHelm__Urls=http://127.0.0.1:5300 ./agenthelm-v1.2.3-linux-x64
./agenthelm-v1.2.3-linux-x64 --urls http://127.0.0.1:5300

# A shared token: every API request must send it in the x-helm-token header
AgentHelm__ApiToken="$(openssl rand -hex 24)" ./agenthelm-v1.2.3-linux-x64
```

```powershell
# Windows (PowerShell)
$env:AgentHelm__ApiToken = "replace-with-a-long-random-value"
.\agenthelm-v1.2.3-win-x64.exe
```

The UI in the same process receives the token automatically; only direct API calls need the header (`curl -H "x-helm-token: $AgentHelm__ApiToken" http://127.0.0.1:5300/api/health`). Keep the address on loopback. Binding it to another interface (for example `0.0.0.0`) exposes a tool that runs agents on your machine; read [Security](Security.md#hardening-checklist) first.

`--no-browser` stops the program from opening a browser on start.

### Running in the background and at login

AgentHelm has no service mode; it is an ordinary program. To keep it running:

```bash
# macOS / Linux: in the background, output to a log file
nohup ./agenthelm-v1.2.3-linux-x64 --no-browser > agenthelm.log 2>&1 &
```

```powershell
# Windows: in the background
Start-Process -WindowStyle Hidden -FilePath .\agenthelm-v1.2.3-win-x64.exe -ArgumentList '--no-browser'
```

To start it at login, register the same command with the tool your system uses: a systemd user service (Linux), a launchd agent (macOS) or a Task Scheduler task that runs at logon (Windows). Add `--no-browser` so no browser opens at every login.

### Supported platforms

Release files exist for the four platforms in the table above. Other platforms (for example Linux arm64) can be built from source with [`scripts/build-single-file.sh`](Development.md#the-single-file-build).

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
