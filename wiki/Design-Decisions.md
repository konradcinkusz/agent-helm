A record of the choices that shape AgentHelm and why they were made, in the spirit of architecture decision records. Each has the context that forced it, the decision, and what follows from it. The [background & vision](Background-and-Vision.md) page tells the story these decisions come from.

## 1. Multi-agent by protocol, not by plugins

**Context.** Every coding-agent vendor ships its own CLI and UI. Integrating each one separately means one plugin per agent, forever.<br>
**Decision.** Speak the [Agent Client Protocol](https://agentclientprotocol.com). One ACP client reaches every agent in the ACP ecosystem; an agent is a [catalog entry](Agent-Catalog.md), not code.<br>
**Consequences.** New agents cost a configuration line. AgentHelm inherits ACP's limits — above all, that agents are local processes ([decision 3](#3-the-bridge-runs-next-to-the-agents)).

## 2. One seam between sessions and protocols

**Context.** Agent protocols and SDKs are young and change without notice; the Copilot CLI has already removed an interface its SDK depended on.<br>
**Decision.** Everything above the protocol talks to `IAgentAdapter` only ([Agent adapters](Agent-Adapters.md)).<br>
**Consequences.** Protocol churn stays inside one adapter. The Copilot SDK adapter can be finished later without touching sessions, permissions, persistence or the UI.

## 3. The Bridge runs next to the agents

**Context.** ACP's remote transport is still a proposal, so agents are child processes speaking over stdio; they also need the user's logins and repositories.<br>
**Decision.** The Bridge runs on the developer's machine. Aspire orchestrates the Bridge and the Web UI as **local processes** and only PostgreSQL as a container.<br>
**Consequences.** Containers can demonstrate the UI but not drive real agents. Remote use needs deliberate work — see [Deployment & topology](Deployment-and-Topology.md). This is a constraint of the ecosystem today, not a shortcut.

## 4. The security boundary is the Bridge, not the UI

**Context.** A web UI can be bypassed; anything that can send HTTP requests can talk to the Bridge.<br>
**Decision.** Every guard lives in the Bridge: the working-directory path guard for ACP file requests and git, the permission gateway, the loopback default and the API token. The UI adds convenience (confirmations), never protection.<br>
**Consequences.** The [HTTP API](Bridge-HTTP-API.md) is as safe as the UI — and exactly as powerful.

## 5. Loopback by default, a token on request

**Context.** A local web tool that executes agent actions is reachable by every web page in the user's browser, because pages can send requests to `localhost`.<br>
**Decision.** Listen on `127.0.0.1` unless configured otherwise, and support a shared `x-helm-token` secret that is worth enabling even on loopback.<br>
**Consequences.** Exposing the Bridge is always an explicit configuration change, never a default.

## 6. Policies can only allow

**Context.** Automating permission decisions saves clicks; automating rejections silently changes what an agent is able to do.<br>
**Decision.** `ask`, `auto_read` and `yolo` can only auto-*allow*. Rejection is always a human decision. Automatic grants use *allow once*, so the agent never stores a broader grant. YOLO needs an explicit confirmation per session. Every decision is audited.<br>
**Consequences.** The transcript is a complete record of who allowed what. `auto_read` excludes `fetch`, because a network call can exfiltrate what a read loaded.

## 7. Handoff context is prefilled, never sent

**Context.** Moving a conversation to another agent means sending one agent's context to another, possibly another vendor.<br>
**Decision.** Build a compact, attributed summary and put it in the new session's composer. The user reads it and presses Send.<br>
**Consequences.** Nothing crosses agents without the user seeing it. The summary is lossy by design (last 4,000 characters, recent first).

## 8. Minimal dependencies

**Context.** Every dependency is something to restore, update, audit and trust — and the Bridge runs with the user's privileges.<br>
**Decision.** The Bridge's only NuGet package is Npgsql. Git is driven through the `git` CLI (present wherever agents run) rather than LibGit2Sharp; the terminal uses `script(1)` rather than a native PTY library.<br>
**Consequences.** Some capabilities are simpler than they could be — notably the terminal ([decision 9](#9-terminal-a-pipe-first-a-pty-where-it-is-cheap)).

## 9. Terminal: a pipe first, a PTY where it is cheap

**Context.** A real cross-platform PTY in .NET means ConPTY/forkpty interop or a native package.<br>
**Decision.** Start with a shell pipe; where util-linux `script` is available (Linux), get a real PTY from `script -qfe -c bash /dev/null`. The binary must identify itself as util-linux, because the `script` of macOS and BusyBox takes other options. ConPTY for Windows is deferred until it can be built and tested on Windows.<br>
**Consequences.** The main use — run commands next to the session and feed their output to the agent — works everywhere; interactive full-screen programs do not.

## 10. Persistence as snapshots, with graceful degradation

**Context.** History matters, but a missing database must never stop someone from trying the tool.<br>
**Decision.** Store one `jsonb` snapshot per session, written behind by a background service once a second; without a database, run memory-only and say so. Same pattern as CopilotScope.<br>
**Consequences.** No schema migrations, no ORM. Live agent processes cannot be rehydrated, so a restarted Bridge offers read-only history and resume through `session/load`.

## 11. An honest skeleton instead of speculative code

**Context.** The GitHub Copilot SDK is in preview, could not be restored where the adapter was written, and its CLI has broken compatibility once already.<br>
**Decision.** Ship the SDK adapter as a skeleton behind the `COPILOT_SDK` symbol, with its TODOs and activation steps written down, and serve Copilot through ACP meanwhile.<br>
**Consequences.** No code pretends to be finished. Starting a `copilot-sdk` agent without the symbol fails with instructions, not silently.

## 12. Say what the Scope correlation is

**Context.** CopilotScope identifies sessions by telemetry ids AgentHelm cannot see.<br>
**Decision.** Match by time window, show at most three candidates, and state in the UI that the match is best-effort.<br>
**Consequences.** Useful today, honest about its limits; exact correlation waits for telemetry tagging on both sides.

## 13. A demo agent in the box

**Context.** Evaluating an agent cockpit should not require installing, paying for and logging in to an agent first.<br>
**Decision.** Ship a tiny ACP agent — the echo agent — that streams, asks for a permission and supports resume.<br>
**Consequences.** The whole loop can be seen in a minute, and the echo agent doubles as a manual test double.

## 14. Documentation makes checkable claims

**Context.** A hand-maintained number in prose — a test count, for instance — decays silently; nothing fails when it becomes false.<br>
**Decision.** Describe coverage and behaviour rather than tallies, and check the wiki with `scripts/wiki.py check` so broken links and anchors fail CI.<br>
**Consequences.** This wiki avoids figures that go stale on the next commit; see [Editing the wiki](Editing-the-Wiki.md).

## 15. The wiki is written in the repository

**Context.** Documentation belongs where people look for it — the repository's Wiki tab — but a wiki edited in the browser has no review, no checks and no tie to the code it describes, so it drifts.<br>
**Decision.** Write the pages in the repository's `wiki/` folder and let a workflow publish them to the GitHub Wiki from `master`. Pages link to each other by file name, which `scripts/wiki.py` checks on every pull request and turns into wiki URLs when it publishes.<br>
**Consequences.** Documentation changes are reviewed with the code they describe, and a broken link or heading fails CI. Edits made in the wiki's web editor are overwritten by the next publish, so the wiki's footer sends editors to the folder; see [Editing the wiki](Editing-the-Wiki.md).
