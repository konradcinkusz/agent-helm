# Plan: AgentHelm UI modelled on the GitHub Copilot app

## Reference (from public docs/blog posts, June–Sept 2026)

The GitHub Copilot desktop app has:

- **Left sidebar** with: *My work*, *Automations*, *Search*, and *Sessions*. Sessions are grouped by repository/project, a **+** next to the *Sessions* heading starts a new session, *Chats* (quick chats – no branch/worktree) live under it. Feedback icon bottom-left.
- **My work**: dashboard of issues/PRs/sessions with sections *All / Active / Review requests / Done*, shown as cards or as a table.
- **Session view**: chat/transcript in the centre; prompt box with dropdowns underneath: *where to run* (new worktree / local / cloud), **mode** (*Interactive*, *Plan*, *Autopilot*), **model** (+ reasoning effort).
- **Side-by-side built-in panels**: *diff* (green/red lines, accept / comment / ask for changes), *terminal* (several terminals, switchable), *browser*. Plus a right-hand **canvas** pane for plans/dashboards.
- Each parallel session has its own git worktree and branch.

Sources: docs.github.com/en/copilot/how-tos/github-copilot-app/{getting-started,agent-sessions,managing-issues-and-pull-requests}, github.blog "GitHub Copilot app for beginners" series, github.blog changelog 2026-05-14.

## Target layout

```
┌───────────────┬──────────────────────────────────────────┬─────────────────┐
│ ◆ AgentHelm   │ Session title · repo · branch · [mode]   │  Dock tabs:     │
│ My work       │ ──────────────────────────────────────── │  Changes│Term…  │
│ Search        │                                          │                 │
│ Sessions  [+] │          Chat / transcript               │  diff / terminal│
│  ▸ repo-a     │                                          │  (side by side  │
│     • sess 1  │ ──────────────────────────────────────── │   with chat)    │
│  ▸ repo-b     │ [prompt box]                             │                 │
│ Chats     [+] │ [folder▾] [mode▾] [model▾]        [Send] │                 │
│ ⚙ Providers   │                                          │                 │
│ ☺ Feedback    │                                          │                 │
└───────────────┴──────────────────────────────────────────┴─────────────────┘
```

## Rules for every task

- One task = one small PR against `master`, branch `ui/<task-id>-<slug>`.
- Blazor Server (.NET 8). Prefer **new Razor components** in `src/AgentHelm.Web/Components/` with **scoped CSS** (`Foo.razor.css`). Touch `Home.razor` / `app.css` only minimally to wire your component in, to keep merge conflicts small.
- Keep the existing behaviour and `data-testid`/ids working (permission banner, yolo confirm, handoff, scope panel, resume, attachments, terminal JS interop).
- Follow `CONTRIBUTING.md` (nullable refs, no needless comments). If behaviour/UI changes, update the matching page in `wiki/` and run `python3 scripts/wiki.py check`.
- There is no `dotnet` in some sandboxes – if you cannot build locally, be extra careful and let CI (`ci.yml`: build + test + release-layout) be the gate.
- Merge only when all CI checks are green. If `master` moved, merge `master` into your branch and resolve conflicts; do not rewrite shared history.

## Tasks

### T0 – Foundation (must merge first)
Split `Pages/Home.razor` (+ `.razor.cs`) into components with clear parameters/callbacks, **no visual or behavioural change**: `AppShell`/layout (CSS grid with regions `sidebar | main | dock`), `SessionSidebar`, `SessionHeader`, `ChatView`, `Composer`, `ChangesPanel`, `TerminalPanel`, `ScopePanel`. Introduce CSS isolation files and move relevant rules out of `app.css`. Acceptance: app behaves exactly as before; CI green.

### Parallel (all depend only on T0)

- **T1 – Design tokens & theme**: replace the topbar with an app-like frame; define CSS variables (surfaces, borders, accent, radius, spacing, font scale) close to the Copilot app's look (dark neutral, subtle borders, rounded cards); keep copper accent only as brand colour. Files: `wwwroot/app.css` tokens, `AppShell`.
- **T2 – Sidebar navigation**: sidebar sections *My work*, *Search*, *Sessions*, *Chats*, footer with *Providers* link and a *Feedback* icon (links to GitHub issues). Collapsible sidebar (icon-only mode). `SessionSidebar` only.
- **T3 – Sessions grouped by repository**: group sessions by repo (last segment of `Cwd`), collapsible groups, status dot (running/idle/error), ⏳ badge for pending permission, **+** button next to the *Sessions* heading opens new-session flow. `SessionSidebar` + small `SessionGroup` component.
- **T4 – New-session dialog**: replace the inline form with a modal/centred "new session" view: agent, folder (existing browse), title, model, pre-config; "Start session". New component `NewSessionDialog`.
- **T5 – Composer controls**: under the prompt box add dropdowns *mode* (Interactive = Ask every tool, Plan = read-only/auto-read, Autopilot = YOLO with the existing confirm) and *model*; move the policy select from the header into the composer. `Composer` only.
- **T6 – Session header**: compact header (title, repo · branch · cwd, status pill, mode chip, overflow menu with Scope / Handoff / Rename / Delete) plus toggle buttons for the dock panels. `SessionHeader` only.
- **T7 – Dock layout**: show Changes and Terminal as a right-hand dock **side by side with chat** (tab strip at top of the dock, resizable splitter, remembered open/closed state) instead of replacing the chat tab. `AppShell` + `Dock` component.
- **T8 – Diff panel polish**: file list with +/− counts, per-file collapsible unified diff with green/red line backgrounds and line numbers, accept/reject buttons per file, "Ask agent to change this" button that inserts the file path into the composer. `ChangesPanel` only.
- **T9 – Multiple terminals**: terminal tab strip supporting several terminal instances per session, "+" to add, close button. Needs `terminal.js` + `TerminalService` tweak. `TerminalPanel`, `Bridge/Workbench/TerminalService.cs`, tests.
- **T10 – My work view**: new route `/` default dashboard (sessions can still be opened from sidebar) with sections *All / Active / Needs attention / Done* and a cards ⇄ table toggle, built from live + archived sessions. New page `Pages/MyWork.razor`.
- **T11 – Chats (quick chats)**: "New chat" under *Chats* starts a session flagged `IsChat` with no required repo path (uses a scratch directory on the Bridge), listed in the *Chats* section. Needs small Bridge + persistence change + tests.
- **T12 – Search**: *Search* entry opens a view that filters live and archived sessions by title, agent and transcript text. New page `Pages/Search.razor` + Bridge endpoint if required.
- **T13 – Chat transcript polish**: Copilot-app style messages (user bubbles right/plain, agent plain with avatar), collapsible tool-call cards, markdown rendering of assistant text, auto-scroll. `ChatView` only.
- **T14 – Docs & screenshots**: update the wiki pages describing the UI and the README screenshots section once the above land (run last / after merges, may be skipped if not all merged).

Conflicts are expected to be small because every task owns one component. T14 should run after the others.
