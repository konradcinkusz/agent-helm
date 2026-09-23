All automation is GitHub Actions in [`.github/workflows`](https://github.com/konradcinkusz/agent-helm/tree/master/.github/workflows). Every job declares `timeout-minutes`, so a hung job fails in minutes instead of occupying a runner for six hours.

## Workflows

| Workflow | File | Runs on | Does |
|---|---|---|---|
| **ci** | `ci.yml` | every pull request; pushes to `master` | Builds the whole solution in Release — the AppHost included, which doubles as the regression check for the Aspire SDK setup — and runs the tests. A second job, `release-layout`, builds the release zip's layout and smoke-tests it with its own launcher: the Bridge must have its agents, the UI its assets, and the echo agent must finish a turn. |
| **wiki** | `wiki.yml` | pull requests and pushes to `master` that touch `wiki/**`, `scripts/wiki.py` or the workflow; manual | Checks this wiki with `scripts/wiki.py check` — links, headings, images, sidebar. On `master` it builds the pages and publishes them to the repository's GitHub Wiki ([Editing the wiki](Editing-the-Wiki.md#publishing)). |
| **GitHub Pages** | `pages.yml` | pushes to `master` that touch `docs/**`; manual | Deploys the `docs/` folder — the project's landing page — to GitHub Pages. |
| **release** | `release.yml` | tags `v*` | Builds and tests, builds the release layout with `scripts/build-release-layout.sh` (Bridge, Web and echo agent published, the echo agent's path rewritten for the layout, the launchers added), zips it, and creates a GitHub Release with generated notes. |
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
4. The **release** workflow runs the tests again ("no green, no release"), builds `agenthelm-v1.2.3.zip` and publishes the GitHub Release; **build containers** pushes the images.
5. After the first image publication, make both GHCR packages **public** once (GitHub → Packages → package → Package settings → Change visibility), so `docker pull` works without logging in.
6. Test the published artifacts, then announce.

### What the zip contains

```text
bridge/         AgentHelm.Bridge (framework-dependent publish)
web/            AgentHelm.Web
echo-agent/     AgentHelm.EchoAgent
run.sh, run.ps1 launchers
README.md, LICENSE, CONTRIBUTING.md, docker-compose.ghcr.yml
```

It runs anywhere the .NET 8 ASP.NET Core runtime is installed; the echo agent's catalog entry points at `${AGENTHELM_DIR}../echo-agent/AgentHelm.EchoAgent.dll`. To build and try the same layout locally:

```bash
scripts/build-release-layout.sh dist
scripts/smoke-release-layout.sh dist     # or: cd dist && ./run.sh
```

### Release checklist

- [ ] CI green on the release commit.
- [ ] Aspire starts cleanly: `dotnet run --project src/AgentHelm.AppHost`.
- [ ] Cold demo: echo session → a prompt containing "tool" → permission banner → switch to YOLO → the automatic decision is audited → History → Resume.
- [ ] One real agent end to end, e.g. `copilot --acp --stdio` with a pinned CLI.
- [ ] The release zip on a clean machine: unpack → `./run.sh` → an echo session works.
- [ ] `docker compose -f docker-compose.ghcr.yml up` pulls the new images and the UI loads.
- [ ] This wiki describes the release — new settings, changed behaviour, removed [known issues](Troubleshooting-and-FAQ.md#known-issues).

A release is *published* when a stranger can find the repository, understand the Scope/Helm pair, download the zip, run it, talk to the echo agent within two minutes, and see how to connect a real agent.
