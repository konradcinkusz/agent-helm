All automation is GitHub Actions in [`.github/workflows`](https://github.com/konradcinkusz/agent-helm/tree/master/.github/workflows). Every job declares `timeout-minutes`, so a hung job fails in minutes instead of occupying a runner for six hours.

## Workflows

| Workflow | File | Runs on | Does |
|---|---|---|---|
| **ci** | `ci.yml` | every pull request; pushes to `master` | Builds the whole solution in Release — the AppHost included, which doubles as the regression check for the Aspire SDK setup — and runs the tests. A `single-file` job builds the single executable for `linux-x64` with `scripts/build-single-file.sh`, runs it from an empty folder and checks that the UI, its static files, `/api/health` and the built-in echo agent all work, and that the output folder holds nothing but the file. |
| **wiki** | `wiki.yml` | pull requests and pushes to `master` that touch `wiki/**`, `scripts/wiki.py` or the workflow; manual | Checks this wiki with `scripts/wiki.py check` — links, headings, images, sidebar. On `master` it builds the pages and publishes them to the repository's GitHub Wiki ([Editing the wiki](Editing-the-Wiki.md#publishing)). |
| **GitHub Pages** | `pages.yml` | pushes to `master` that touch `docs/**`; manual | Deploys the `docs/` folder — the project's landing page — to GitHub Pages. |
| **release** | `release.yml` | tags `v*` | Builds the single executable for each of the four platforms (`linux-x64`, `win-x64`, `osx-arm64`, `osx-x64`) with `scripts/build-single-file.sh`, writes `SHA256SUMS`, and creates a GitHub Release with generated notes and these files attached. |
| **build containers** | `build-containers.yml` | tags `v*`; pull requests that touch the Dockerfiles, `.dockerignore`, `nuget.config` or the workflow; manual | Builds and pushes `ghcr.io/<owner>/agenthelm-bridge` and `…/agenthelm-web`, tagged with the version, `major.minor` and `latest`. On a pull request it builds both images without pushing, starts the Bridge image with an API token and waits for it to report healthy. |

Workflow files are a [protected path](Development.md#protected-paths): a change to them is not merged past a red run.

## Dependabot

[`.github/dependabot.yml`](https://github.com/konradcinkusz/agent-helm/blob/master/.github/dependabot.yml) checks weekly for updates of:

- **GitHub Actions** — one grouped pull request for all actions (commit prefix `ci`);
- **NuGet** — with an `aspire` group for `Aspire.*` and a `test` group for xUnit and the test SDK (commit prefix `deps`).

Dependabot only opens pull requests; each goes through the same CI as any other change.

> **Important:** The `aspire` group updates `Aspire.*` **package references** only. `AgentHelm.AppHost.csproj` also pins `<Sdk Name="Aspire.AppHost.Sdk" Version="…" />`, which is an MSBuild SDK, not a package. The two must always move together — when an Aspire update arrives, bump the `Sdk` element by hand in the same pull request.

## Releasing

1. Make sure `master` is green.
2. Walk through the [release checklist](#release-checklist).
3. Create the release: push a tag, or create a release in the GitHub UI, which creates the tag.

    ```bash
    git tag v1.2.3
    git push origin v1.2.3
    ```

    The tag must start with a lowercase `v` — the workflows trigger on `v*`.
4. The **release** workflow runs the tests again ("no green, no release"), builds the four single files, and publishes the GitHub Release with `agenthelm-v1.2.3-<rid>` (`.exe` on Windows) and `SHA256SUMS`; **build containers** pushes the images.
5. After the first image publication, make both GHCR packages **public** once (GitHub → Packages → package → Package settings → Change visibility), so `docker pull` works without logging in.
6. Test the published artifacts, then announce.

### What a release contains

| File | Platform |
|---|---|
| `agenthelm-<tag>-linux-x64` | Linux, x64 |
| `agenthelm-<tag>-win-x64.exe` | Windows, x64 |
| `agenthelm-<tag>-osx-arm64` | macOS, Apple silicon |
| `agenthelm-<tag>-osx-x64` | macOS, Intel |
| `SHA256SUMS` | one `<sha256>  <file name>` line per file above |

Each file is self-contained: the .NET runtime, the Bridge, the Web UI and the echo agent are inside it, and nothing has to sit next to it. Users verify downloads with `SHA256SUMS` and find the first-run steps for Gatekeeper and SmartScreen in [Installation](Installation.md#single-file). There is no zip and no launcher script any more.

To build one locally and run the same smoke test the CI runs:

```bash
scripts/build-single-file.sh linux-x64 dist   # exactly one file: dist/agenthelm
scripts/smoke-single-file.sh dist
```

### Release checklist

- [ ] CI green on the release commit.
- [ ] Aspire starts cleanly: `dotnet run --project src/AgentHelm.AppHost`.
- [ ] Cold demo: echo session → a prompt containing "tool" → permission banner → switch to YOLO → the automatic decision is audited → History → Resume.
- [ ] One real agent end to end, e.g. `copilot --acp --stdio` with a pinned CLI.
- [ ] Each release file on a clean machine (at least Linux and Windows): download, check it against `SHA256SUMS`, run it → an echo session works.
- [ ] `docker compose -f docker-compose.ghcr.yml up` pulls the new images and the UI loads.
- [ ] This wiki describes the release — new settings, changed behaviour, removed [known issues](Troubleshooting-and-FAQ.md#known-issues).

A release is *published* when a stranger can find the repository, understand the Scope/Helm pair, download the file for their system, run it, talk to the echo agent within two minutes, and see how to connect a real agent.
