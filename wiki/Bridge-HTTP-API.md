The Web UI is one client of the Bridge; the API is equally usable from scripts and other tools. Everything the UI can do — start sessions, prompt, answer permission requests, review changes, drive the terminal — is an HTTP call.

> **Warning:** The API is as powerful as the UI: it can run agent tools, execute shell commands and change files as the user running the Bridge. UI-only safeguards — such as the YOLO confirmation or the reject confirmation in the Changes tab — do not apply to API clients. See the [security model](Security.md).

## Conventions

- **Base URL:** `http://127.0.0.1:5199/api` (see [configuration](Configuration.md#bridge-settings)).
- **Authentication:** when `AgentHelm:ApiToken` is set, every request must send `x-helm-token: <token>`; otherwise the Bridge answers `401` with an empty body. Without a token configured, there is no authentication.
- **Request bodies** are JSON (`Content-Type: application/json`); property names are case-insensitive.
- **Responses** are JSON with **camelCase** names. Errors are `400`/`404`/`409` with `{"error": "…"}`, or `500` with an RFC 9110 problem document whose `detail` carries the message.
- **Event streams** (`…/stream`) are `text/event-stream`; each event is one `data:` line of JSON. Session and terminal events use **PascalCase** names — see [below](#event-streams).
- **Ids** are 12-character hex strings.

## Quick example

```bash
B=http://127.0.0.1:5199/api
curl -s $B/agents
# [{"id":"copilot","name":"GitHub Copilot CLI"},…,{"id":"echo","name":"Echo (built-in demo agent)"}]

curl -s -X POST $B/sessions -H 'content-type: application/json' \
     -d '{"agentId":"echo","cwd":"/home/dev/projects/acme-api"}'
# {"id":"da9ae20420d3","agentId":"echo",…,"status":"idle","policy":"ask",…}

curl -sN $B/sessions/da9ae20420d3/stream &      # follow the live events
curl -s -X POST $B/sessions/da9ae20420d3/prompt -H 'content-type: application/json' \
     -d '{"text":"please use a tool"}'           # → 202 Accepted
```

## Endpoints

### Health and configuration

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | `{"status":"ok","sessions":<live session count>}` |
| `GET` | `/config` | `{"scopeUrl":"http://localhost:4318"}` |
| `POST` | `/config/scope-url` | Body `{"url":"…"}`. Changes the CopilotScope URL until restart. `400` if empty. |

### Agents and providers

| Method | Path | Description |
|---|---|---|
| `GET` | `/agents` | The catalog: `[{"id","name"}]`. |
| `GET` | `/providers` | Account status for catalog agents with id `copilot`, `claude` or `gemini`: `[{"id","name","account","status","loginCommand","logoutCommand"}]`; `status` is `logged_in` or `logged_out`. |
| `GET` | `/providers/{id}/models` | `[{"id","name","isDefault"}]` — Copilot's models via `gh`; `[]` for others. `404` for unknown agents. |
| `POST` | `/providers/{id}/login/start` | Runs the login command. `{"started":true}`; `400` if there is none. |
| `GET` | `/providers/{id}/login/stream` | Event stream of the login's output — see [below](#event-streams). `404` if no login was started. |
| `POST` | `/providers/{id}/logout` | Runs the logout command and waits: `{"success":true}`, or `500` with the exit code. |

### Sessions

| Method | Path | Description |
|---|---|---|
| `GET` | `/sessions` | Live sessions, most recently active first, as session summaries. |
| `POST` | `/sessions` | Start a session — see below. |
| `GET` | `/sessions/{id}` | Session detail with transcript — see below. |
| `POST` | `/sessions/{id}/prompt` | Body `{"text":"…","attachments":[…]}`. `202` — the turn runs in the background; follow the stream. `409` while a turn is running, `413` if attachments exceed 8,000,000 characters in total. |
| `POST` | `/sessions/{id}/cancel` | Sends ACP `session/cancel` for the current turn. `200`. |
| `POST` | `/sessions/{id}/permission` | Answer the pending request — see below. |
| `POST` | `/sessions/{id}/policy` | Body `{"policy":"auto_read"}` — `ask`, `auto_read` or `yolo` → `{"policy"}`. `400` for other values. Audited. |
| `POST` | `/sessions/{id}/title` | Body `{"title":"…"}` → `{"title"}`. `400` unless 1–120 characters. |
| `DELETE` | `/sessions/{id}` | Stop the agent and terminal, delete the session and its history row. |
| `GET` | `/sessions/{id}/stream` | Live events — see [below](#event-streams). |
| `POST` | `/sessions/{id}/handoff` | Body `{"agentId":"…","title":null}` → `{"session": <summary>, "context": "…"}`. The context is for the new session's composer; the Bridge never sends it. |
| `GET` | `/sessions/{id}/scope` | `{"available":false,"matches":[]}` or `{"available":true,"matches":[{"id","title","model","score","grade","confidence","lastActivity"}]}` |

**Start a session** — `POST /sessions`:

```json
{ "agentId": "echo", "cwd": "/home/dev/projects/acme-api", "title": null, "policy": "ask", "model": null }
```

`agentId` and `cwd` are required; `title`, `policy` and `model` are optional. The response is a **session summary**:

```json
{
  "id": "da9ae20420d3", "agentId": "echo", "cwd": "/home/dev/projects/acme-api",
  "title": "Echo (built-in demo agent) · acme-api", "status": "idle", "policy": "ask",
  "createdAt": "2026-09-22T19:13:39.1089865+00:00", "lastActivity": "2026-09-22T19:13:39.1089866+00:00",
  "hasPendingPermission": false
}
```

Errors: `400` *Unknown agent '…'* or *Working directory does not exist: …*; `500` *Could not start agent '…': …*. The call returns once the agent has completed the ACP handshake.

**Session detail** — `GET /sessions/{id}` adds `model`, `caps`, `pending` and `transcript`:

```json
{
  "id": "da9ae20420d3", "agentId": "echo", "cwd": "/home/dev/projects/acme-api",
  "title": "…", "status": "running", "policy": "ask", "model": null,
  "createdAt": "…", "lastActivity": "…",
  "caps": { "loadSession": true, "image": true, "audio": false, "embeddedContext": true },
  "pending": {
    "requestKey": "144be8b428d34f34ad703e9d0b8263e3",
    "toolTitle": "write_demo_file", "toolKind": "edit",
    "options": [
      { "optionId": "allow", "name": "Allow", "kind": "allow_once" },
      { "optionId": "reject", "name": "Reject", "kind": "reject_once" }
    ]
  },
  "transcript": [
    { "time": "…", "role": "user", "text": "please use a tool", "kind": "message" }
  ]
}
```

**Attachments** in a prompt are objects `{"kind","name","mimeType","data"}` where `kind` is `image` (with base64 `data`) or `text` (with the file's text as `data`).

**Answer a permission request** — `POST /sessions/{id}/permission`:

```json
{ "requestKey": "144be8b428d34f34ad703e9d0b8263e3", "allow": true, "optionId": "allow" }
```

With `allow: true`, `optionId` is passed to the agent and must be one of the pending request's options. With `allow: false` the request is denied (the agent gets its reject option or *cancelled*). `404` if the request key is not pending. Both answers are audited.

### History

| Method | Path | Description |
|---|---|---|
| `GET` | `/history` | Up to 200 archived sessions, most recent first: `[{"id","agentId","cwd","title","createdAt","lastActivity","transcript":[…],"nativeSessionId"}]`. `[]` without persistence. |
| `POST` | `/history/{id}/resume` | Body `{"title":null}` (a body is required). Starts a new session through ACP `session/load` → session summary. `400` without persistence or without a native session id, `404` for unknown ids, `500` if the agent cannot load it. |

### Git (Changes tab)

Paths are relative to the session's working directory; paths that escape it are refused with `400`.

| Method | Path | Description |
|---|---|---|
| `GET` | `/sessions/{id}/git/changes` | `{"isRepo":true,"files":[{"path","status","untracked"}]}`, or `{"isRepo":false,"files":[]}` |
| `GET` | `/sessions/{id}/git/diff?path=…` | `{"path","status","diffText","additions","deletions"}` — untracked files as all-added |
| `POST` | `/sessions/{id}/git/accept` | Body `{"path":"…"}` — `git add`. Audited. |
| `POST` | `/sessions/{id}/git/reject` | Body `{"path":"…"}` — revert (tracked) or delete (untracked). Audited. **No confirmation.** |

### Terminal

| Method | Path | Description |
|---|---|---|
| `POST` | `/sessions/{id}/terminal/start` | Start (or reuse) the session's shell → `{"pty":true}` in PTY mode, `{"pty":false}` in pipe mode |
| `POST` | `/sessions/{id}/terminal/input` | Body `{"text":"…"}` — written to the shell followed by a newline. `404` if not started. |
| `GET` | `/sessions/{id}/terminal/buffer` | `{"text":"…"}` — the last 64,000 characters of output |
| `GET` | `/sessions/{id}/terminal/stream` | Output as it arrives — see [below](#event-streams) |

### File system

| Method | Path | Description |
|---|---|---|
| `GET` | `/fs/dirs?path=…` | Sub-directory names of `path` on the Bridge's machine (default: the Bridge user's home): `{"path","parent","dirs":[…]}`. `400` if the directory does not exist. Used by the directory browser. |

## Event streams

All three streams send `data: <json>` lines separated by blank lines, and stay open until the client disconnects.

**Session stream** — `GET /sessions/{id}/stream`. Each event is `{"Kind","Text","Data"}` in PascalCase, with `Data` (when present) also in PascalCase:

```text
data: {"Kind":"status","Text":"running","Data":null}

data: {"Kind":"chunk","Text":"Echo agent here. ","Data":null}

data: {"Kind":"entry","Text":"write_demo_file","Data":{"Time":"2026-09-22T19:13:47.1779328+00:00","Role":"tool","Text":"write_demo_file","Kind":"tool_call"}}

data: {"Kind":"permission","Text":"write_demo_file","Data":{"RequestKey":"144be8b4…","ToolTitle":"write_demo_file","ToolKind":"edit","Options":[{"OptionId":"allow","Name":"Allow","Kind":"allow_once"},{"OptionId":"reject","Name":"Reject","Kind":"reject_once"}]}}
```

The event kinds are listed in [Events & transcript](Events-and-Transcript.md#live-events). A stream only carries what happens after you connect — load `GET /sessions/{id}` first for the transcript so far.

**Terminal stream** — `GET /sessions/{id}/terminal/stream`: `{"Kind":"out","Text":"<output chunk>","Data":null}`. Output can contain ANSI escape sequences.

**Login stream** — `GET /providers/{id}/login/stream`: camelCase `{"kind":"output","text":"<line>"}` for each output line (replayed from the start for late subscribers), then `{"kind":"done","exitCode":0}`.
