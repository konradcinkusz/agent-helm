---
description: How this wiki is built and published, how to preview it locally, and the conventions for writing pages.
---

# Editing the docs

This wiki is plain Markdown in the repository's [`docs/`](https://github.com/konradcinkusz/agent-helm/tree/master/docs) directory, built with [MkDocs](https://www.mkdocs.org/) and the [Material for MkDocs](https://squidfunk.github.io/mkdocs-material/) theme and published to GitHub Pages. Documentation changes are reviewed like code: in a pull request, with a build check.

## The quick way

Every page has an **edit** button (the pencil next to the title). It opens the page's source on GitHub, where you can edit it and propose the change as a pull request.

## Preview locally

You need Python 3.

```bash
python3 -m venv .venv
source .venv/bin/activate            # Windows: .venv\Scripts\activate
pip install -r docs/requirements.txt
mkdocs serve                         # http://127.0.0.1:8000, reloads on save
```

Before opening a pull request, run the same build CI runs:

```bash
mkdocs build --strict
```

`--strict` fails on any warning: a link to a missing page, a link to a missing heading, or a page that exists but is not in the navigation.

> [!IMPORTANT]
> Keep MkDocs on the 1.x line. MkDocs 2.0 removes the plugin and theme systems that Material for MkDocs is built on, so an unpinned install would eventually break the site — which is why `docs/requirements.txt` pins exact versions. Bump them deliberately and check the strict build.

## How it is organised

| Path | What |
|---|---|
| `mkdocs.yml` | Site settings, theme, Markdown extensions and the **navigation** (`nav`) |
| `docs/requirements.txt` | The pinned toolchain |
| `docs/index.md` | The home page |
| `docs/<section>/` | One folder per section; `index.md` is the section's landing page |
| `docs/assets/` | Logo, stylesheet (`extra.css`) and screenshots |

The Polish working notes in `docs/` — `ANALYSIS.md`, `ANNOUNCE-POST.md`, `RELEASE-CHECKLIST.md` — are kept in the repository but excluded from the site (`exclude_docs` in `mkdocs.yml`).

To **add a page**, create the Markdown file in the right section folder and add it to `nav` in `mkdocs.yml`; the strict build fails if you forget.

## Publishing

`.github/workflows/pages.yml` builds the site for every pull request that touches `docs/**` or `mkdocs.yml`, and deploys it when such a change reaches `master` (or when the workflow is started by hand). The repository's Pages source must be set to **GitHub Actions** (Settings → Pages) — a one-time setting.

## Writing conventions

- **English**, sentence-case headings, one `#` title per page, and a `description` in the front matter.
- **Link with relative paths to `.md` files** — `[History](../guide/history.md#resuming-a-session)`. The build checks them. Link to source files on GitHub with `https://github.com/konradcinkusz/agent-helm/blob/master/…`.
- **Callouts** use GitHub's syntax, so they render both here and on GitHub:

    ```markdown
    > [!NOTE]
    > Useful background.
    ```

    Available: `NOTE`, `TIP`, `IMPORTANT`, `WARNING`, `CAUTION`.
- **Diagrams** are [Mermaid](https://mermaid.js.org/) code blocks (` ```mermaid `), which GitHub renders too.
- **Keyboard keys** are written as `++ctrl+enter++`; content tabs use `=== "Tab"`.
- **Describe what the code does**, and check it against the code. Where the product has a known defect, say so on the page and list it under [Known issues](../project/troubleshooting.md#known-issues) until it is fixed.
- **Do not hard-code numbers that decay** — test counts, line counts, "N agents". Describe coverage and behaviour instead.
- **Screenshots** go in `docs/assets/screenshots/` as PNG, taken at 1280 px wide from a session with the echo agent.

## Keeping it true

When a pull request changes behaviour, configuration, the API or the workflows, it should update the matching page in the same pull request. The [release checklist](ci-cd.md#release-checklist) ends with a documentation check for the same reason.
