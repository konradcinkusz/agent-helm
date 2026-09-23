This wiki is plain Markdown in the repository's [`wiki/`](https://github.com/konradcinkusz/agent-helm/tree/master/wiki) folder. A workflow publishes it to the repository's [GitHub Wiki](https://github.com/konradcinkusz/agent-helm/wiki) whenever a change reaches `master`, so documentation changes are reviewed like code: in a pull request, with a check.

> **Important:** Do not edit pages in the wiki's web editor. The next publish replaces the wiki with the contents of `wiki/`, and the edit is lost. Propose the change as a pull request instead.

## The quick way

Open the page's file in the [`wiki/` folder](https://github.com/konradcinkusz/agent-helm/tree/master/wiki) on GitHub, click the pencil (**Edit this file**) and propose the change. GitHub creates the branch and the pull request for you.

## How it is organised

| Path | What |
|---|---|
| `wiki/Home.md` | The home page |
| `wiki/<Page-Name>.md` | One file per page. The file name is the page's name and title: `Sessions-and-Chat.md` becomes *Sessions and Chat*. |
| `wiki/_Sidebar.md` | The navigation shown next to every page |
| `wiki/_Footer.md` | The note under every page |
| `wiki/images/` | Screenshots and the logo |
| `scripts/wiki.py` | Checks the pages and builds the published copy |
| `.github/workflows/wiki.yml` | Runs the check on pull requests and publishes from `master` |

The wiki is flat: every page is a file directly in `wiki/`, and page names are unique. The `docs/` folder is not part of the wiki — it holds the [GitHub Pages](https://konradcinkusz.github.io/agent-helm/) landing page and the Polish working notes (`ANALYSIS.md`, `ANNOUNCE-POST.md`, `RELEASE-CHECKLIST.md`).

To **add a page**, create `wiki/Page-Name.md` and link it from `_Sidebar.md`. The check fails if a page is missing from the sidebar.

## Check before you push

The check needs only Python 3:

```bash
python3 scripts/wiki.py check
```

It fails on a link to a page or a heading that does not exist, a missing image, a page left out of the sidebar, and on syntax GitHub does not render (see [Writing conventions](#writing-conventions)). CI runs the same check on every pull request that touches `wiki/`.

There is nothing to build for a preview: GitHub renders the files in `wiki/` — open one on GitHub, or look at the pull request's rich diff. Links between pages and images work there too.

## Publishing

When a change to `wiki/` reaches `master` — or when the workflow is started by hand — `.github/workflows/wiki.yml` runs `scripts/wiki.py build` and pushes the result to the wiki's own Git repository, `agent-helm.wiki.git`. The build turns links between pages into wiki URLs and points images at the commit being published, so the wiki always matches one commit of the repository. Pages that no longer exist in `wiki/` are removed from the wiki.

The wiki has to exist before the first publish. This is a one-time setup in the repository:

1. **Settings → General → Features:** enable **Wikis**. Tick **Restrict editing to collaborators only** as well, since the pages are generated.
2. Open the **Wiki** tab, click **Create the first page** and save it with any content. GitHub creates the wiki's Git repository only then; the first publish replaces that page.

Until then the publish job fails with a message that points here.

## Writing conventions

- **English**, sentence-case headings. Do not add a `#` title — GitHub shows the page name as the title — so sections start at `##`.
- **Link to other pages by file name**, for example `[History](History-and-Resume.md#resuming-a-session)`. The check verifies both the page and the heading. Link to source files with `https://github.com/konradcinkusz/agent-helm/blob/master/…`.
- **Images** go in `wiki/images/` as PNG, taken at 1280 px wide from a session with the echo agent. Embed them with `![Alt text](images/name.png)`, or with `<img src="images/name.png" alt="Alt text" width="480">` to set a width.
- **Notes** are blockquotes that start with a bold label: `> **Note:** …`, `> **Important:** …` or `> **Warning:** …`.
- **Keyboard keys** are written as `<kbd>Ctrl</kbd>+<kbd>Enter</kbd>`.
- **Diagrams** are [Mermaid](https://mermaid.js.org/) code blocks (` ```mermaid `), which GitHub renders.
- **Only Markdown GitHub renders:** no front matter, definition lists, content tabs or `{ attribute }` lists. The check rejects them.
- **Describe what the code does**, and check it against the code. Where the product has a known defect, say so on the page and list it under [Known issues](Troubleshooting-and-FAQ.md#known-issues) until it is fixed.
- **Do not hard-code numbers that decay** — test counts, line counts, "N agents". Describe coverage and behaviour instead.

## Keeping it true

When a pull request changes behaviour, configuration, the API or the workflows, it should update the matching page in the same pull request. The [release checklist](CI-and-Releases.md#release-checklist) ends with a documentation check for the same reason.
