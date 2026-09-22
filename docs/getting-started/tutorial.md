---
description: A guided tour of AgentHelm, from a fresh install to a real working session with a coding agent.
---

# Tutorial

This tutorial walks you through AgentHelm from a fresh install to a real working session with a coding agent. No prior experience with ACP or .NET Aspire is needed.

**Time to complete:** about 20 minutes<br>
**What you will learn:** install, a first echo session, permission policies, connecting a real agent, git diff review, the terminal, attachments, handoff, session history

---

## 1. Install and start

### Prerequisites

- For the release zip: the .NET 8 ASP.NET Core runtime.
- For running from source: the .NET SDK 8.0.303 or newer — check with `dotnet --version`.
- Docker (optional) — only needed for persistent session history.

### Quickest start: release zip

Download the latest zip from [Releases](https://github.com/konradcinkusz/agent-helm/releases), unpack it, and run:

```bash
# macOS / Linux
./run.sh

# Windows
.\run.ps1
```

The Bridge starts on `http://127.0.0.1:5199` and the UI on `http://127.0.0.1:5200`. Open `http://127.0.0.1:5200` in your browser. (Using release v1.0.5 or older? Read the [known issue](../project/troubleshooting.md#known-issues) about the launchers first.)

### From source (recommended for development)

```bash
git clone https://github.com/konradcinkusz/agent-helm.git
cd agent-helm

# With Aspire — starts Postgres + Bridge + Web
dotnet run --project src/AgentHelm.AppHost
```

Aspire prints a link to its dashboard; open it and follow the URL listed for the `web` resource.

> [!NOTE]
> If you see "Aspire Workload has been deprecated", update your .NET SDK to 8.0.303+ and optionally run `dotnet workload uninstall aspire`.

All options, including containers, are described in [Installation](installation.md).

---

## 2. Your first session (no real agent needed)

AgentHelm ships with a built-in **echo agent** — it speaks the full ACP protocol and is perfect for learning the interface before you connect a real agent.

1. Click **＋ New** in the left rail.
2. Pick **Echo (built-in demo agent)** from the agent drop-down.
3. Set any existing directory as the working directory (for example your home folder) — type it, or pick it with the 📁 directory browser.
4. Click **Start session**.

The session opens with the transcript in the main area.

**Try a basic prompt:**

- Type `Hello!` and press ++ctrl+enter++.
- The echo agent streams the response back. Notice the chunk-by-chunk rendering in the transcript.

**Try a tool call:**

- Type any message containing the word **tool** (for example `Please use a tool here`).
- The echo agent asks for permission to run a tool called `write_demo_file`.
- An **amber banner** appears above the tabs. Its buttons are the options the agent offered — for the echo agent, **Allow** and **Reject**:
    - **Allow** — the tool runs; *Permission granted by user* is added to the transcript.
    - **Reject** — the tool is denied; *Permission denied by user* is added to the transcript.

This is the core of AgentHelm's permission gateway. Every decision — human or automatic — lands in the transcript as a permanent record. [Your first session](first-session.md) shows each step with screenshots.

---

## 3. Permission policies

Approving every tool call manually can become tedious for long sessions. AgentHelm offers three policies you can switch per session with the **policy selector** in the session header:

| Policy | Label in the UI | Behaviour |
|---|---|---|
| `ask` (default) | Ask every tool | Every tool call shows an approval prompt |
| `auto_read` | Auto-allow reads | Read-only tool kinds (`read`, `search`, `think`) are auto-allowed; everything else — including network `fetch` — still asks |
| `yolo` | YOLO | Everything is auto-allowed — requires an explicit confirmation per session |

> [!NOTE]
> Auto-read deliberately excludes `fetch` even though it does not modify anything: a fetch can exfiltrate what a file read just loaded.

YOLO is designed for trusted sessions where you want the agent to run uninterrupted. It always requires an explicit confirmation click, and it can never be a global default. Every automatic decision is still audited. More in [Permissions & policies](../guide/permissions.md).

---

## 4. Connecting a real agent

Three real agents are preconfigured in `src/AgentHelm.Bridge/appsettings.json` under `AgentHelm:Agents` — install the CLI, authenticate it, and it appears in the **＋ New** drop-down (restart the Bridge after changing the catalog).

### Claude Code

Make sure Node.js is installed. Claude Code runs through Zed's ACP adapter, which `npx` downloads on first use:

```json
{
  "Id": "claude",
  "Name": "Claude Code",
  "Command": "npx",
  "Args": ["@zed-industries/claude-code-acp"]
}
```

The first session takes a moment while `npx` downloads the adapter.

### GitHub Copilot CLI

Prerequisites: `copilot` on the `PATH`, logged in (`copilot login`), and a Copilot subscription.

```json
{
  "Id": "copilot",
  "Name": "GitHub Copilot CLI",
  "Command": "copilot",
  "Args": ["--acp", "--stdio"]
}
```

The preconfigured entry also carries an `Environment` block that exports Copilot's telemetry to a local [CopilotScope](../guide/copilotscope.md) collector.

### Gemini CLI

Prerequisites: `gemini` on the `PATH` and authenticated.

```json
{
  "Id": "gemini",
  "Name": "Gemini CLI",
  "Command": "gemini",
  "Args": ["--acp"]
}
```

Any other agent that speaks ACP over stdio can be added the same way — see [Connecting agents](agents.md) and the [agent catalog reference](../configuration/agent-catalog.md).

---

## 5. The git diff viewer (Changes tab)

When an agent modifies files in the working directory, you can review the changes in the **Changes** tab of the session.

1. Open a session whose working directory is a git repository.
2. Ask the agent to make a change (for example `Create a file called hello.txt with the text "hello world"`).
3. Switch to the **Changes** tab.
4. You will see the changed files with their status; **View** shows the diff against `HEAD` with `+` / `−` counts.

For each file you can:

- **Accept** — stages the file (`git add`). The action is audited in the transcript.
- **Reject** — asks you to confirm, then reverts the change: `git checkout HEAD --` for tracked files, deletion for untracked ones. Also audited.

All paths are guarded: nothing outside the session's working directory can be accepted or rejected through this tab. More in [Reviewing changes](../guide/changes.md).

---

## 6. The integrated terminal (Terminal tab)

The **Terminal** tab gives you a shell in the session's working directory, rendered with xterm.js.

1. Switch to the **Terminal** tab.
2. Type a command in the input box under the terminal (for example `ls -la` or `git log --oneline`) and press ++enter++.
3. Click **→ Prompt** to append the most recent terminal output to the chat composer. This lets you hand a test failure, a build error or any command output to the agent without copy-pasting.

> [!NOTE]
> On Linux, where util-linux `script` is available, the terminal runs inside a real PTY — prompts and colours work. On Windows and macOS it is a plain pipe: commands run and their output streams, but interactive full-screen programs do not render. See [Terminal](../guide/terminal.md).

---

## 7. Image and file attachments

You can attach files to any prompt:

1. Click the **paperclip** in the composer.
2. Select up to 4 files, at most 2 MB each.
    - Images are sent as native ACP `image` content blocks — the agent sees the actual image.
    - Text files are embedded as `resource` blocks — the agent reads their content.
    - Other binary files are refused in the browser.

The capability chips in the session header (`images`, `files`) show what the agent advertised; if it advertised neither, the paperclip's tooltip warns that the agent may reject attachments. More in [Sessions & chat](../guide/sessions.md#attachments).

---

## 8. Agent handoff

To continue a conversation with a *different* agent (for example from Copilot to Claude Code):

1. Click **Handoff** in the session header.
2. Pick the target agent and click **Create**. The new session opens in the **same working directory** with the same permission policy.
3. The new session's composer is prefilled with a compact, attributed summary of the conversation so far.
4. **Review the summary**, edit it if you like, then press **Send**. The summary is never sent automatically.

The source session gets an audit entry recording the handoff. More in [Agent handoff](../guide/handoff.md).

---

## 9. Session history and resume

With PostgreSQL configured (Aspire and the compose files do this for you), every session is saved in the background about a second after each change.

- Click **History** in the left rail to see past sessions (the 200 most recent).
- Click one to open its transcript in a read-only viewer.
- Click **Resume** to continue it: AgentHelm starts a new session with the same agent in the same directory and asks the agent to replay the conversation through ACP `session/load`. This works when the agent supports session loading — the echo agent does.

Without PostgreSQL the Bridge runs memory-only — sessions are lost when the Bridge restarts, but every other feature works the same. More in [History & resume](../guide/history.md).

---

## 10. Security notes before you go further

AgentHelm is designed to run on your own machine. A few things to be aware of:

- The Bridge binds to `127.0.0.1` by default. If you need remote access, set `AgentHelm:Urls` explicitly — it is never widened automatically.
- Enable the shared token (`AgentHelm:ApiToken` on the Bridge, `Bridge:ApiToken` on the UI) even on loopback: any web page open in your browser can send requests to localhost.
- The **Terminal** tab executes shell commands as you, on your machine — it is exactly as powerful as your own terminal. Treat it accordingly.
- YOLO requires an explicit per-session opt-in and is never a global default. Every automatic decision is still audited.

The [security model](../security/index.md) covers each boundary in depth.

---

## What's next?

- Add your own ACP-compatible agent to the [agent catalog](../configuration/agent-catalog.md).
- Browse the [roadmap](../project/roadmap.md) to see what is planned beyond M3.
- Run the test suite with `dotnet test` — see [Testing](../development/testing.md).
- Open an issue or a pull request if you find a bug or have an idea — see the [development guide](../development/index.md).
