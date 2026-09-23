With PostgreSQL configured, AgentHelm keeps every session's transcript after the session — or the whole Bridge — is gone. Archived sessions can be read, and resumed with agents that support it.

## Turning history on

History needs a PostgreSQL connection string named `helmdb` (`ConnectionStrings:helmdb`):

| How you run AgentHelm | History |
|---|---|
| Aspire (`src/AgentHelm.AppHost`) | on — Aspire starts PostgreSQL and injects the connection string |
| Docker Compose | on — the compose files include PostgreSQL |
| Release zip, `dotnet run` | off unless you set `ConnectionStrings__helmdb` yourself |

Without it the Bridge runs **memory-only**: everything works, but sessions are lost when the Bridge stops, and the History view explains that history requires PostgreSQL. If the database is configured but unreachable at startup, the Bridge logs *Postgres unavailable — running memory-only* and carries on without history for that run.

## What is saved, and when

The Bridge saves a snapshot of the session — id, agent, working directory, title, timestamps, the full transcript and the agent's own session id — about **one second** after:

- a turn ends (successfully or with an error),
- you answer a permission request,
- you rename the session or change its policy,
- you accept or reject a change in the Changes tab,
- you hand the session off.

A session you started but never used is not saved. The agent's live "thinking" stream, tool-status updates, the terminal and pending permission requests are not part of the snapshot. See [Persistence](Persistence.md) for the table layout.

## Browsing history

Click **History** in the rail. It lists the **200 most recently active** archived sessions with agent and time. Select one to read its transcript — the view is read-only and marked *archived · read-only*. **Live** takes you back to the running sessions.

Deleting a live session also deletes its history record. Archived sessions whose live session is gone cannot be deleted from the UI.

## Resuming a session

**Resume** continues an archived conversation:

1. AgentHelm starts a **new** session with the same agent in the same working directory, titled `<title> (resumed)`, with the default `ask` policy.
2. Instead of `session/new`, it asks the agent to load its own stored session through ACP `session/load`, using the agent-side session id saved in the archive.
3. The agent replays the conversation, which appears in the new session's transcript; then you continue as usual.

The archived record itself is not changed.

Resume needs two things:

- **An agent-side session id in the archive.** Sessions archived before resume support existed have none; for them the button is disabled with the tooltip *This archive predates resume support*.
- **An agent that supports `session/load`** — it advertises the `loadSession` capability, shown as the `resume` chip in the session header. The built-in echo agent does; whether a real agent does depends on the agent and its version. If it does not, resuming fails with *Resume failed (the agent may not support session/load)*.

How much of the conversation the agent restores is up to the agent: the echo agent, for example, replays a single line — *[resumed session echo-1234 — echo agent remembers you]*.

## Where the data lives

One row per session in the `helm_sessions` table of the `helmdb` database, with the snapshot as JSON. With Aspire the data is kept in the Docker volume `agenthelm-pgdata`, which survives restarts; removing the volume removes the history. [Persistence](Persistence.md) has the details.
