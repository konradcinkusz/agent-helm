---
description: AgentHelm's security model — the boundaries it holds, the powerful behaviour that is deliberate, hardening, and how to report a vulnerability.
---

# Security model

AgentHelm drives AI coding agents that execute tools on your machine. That is the product, and it is also the whole of its risk: the Bridge is a local process with your files, your credentials and a shell. This page describes the boundaries AgentHelm holds, what is powerful by design, and how to run it safely. The formal policy — supported versions and disclosure — is [`SECURITY.md`](https://github.com/konradcinkusz/agent-helm/blob/master/SECURITY.md).

## Boundaries AgentHelm holds

### Loopback by default

The Bridge listens on `127.0.0.1:5199` unless `AgentHelm:Urls` says otherwise. Exposing it to a network is always an explicit configuration change, never a default.

### The shared API token

With `AgentHelm:ApiToken` set, every request to the Bridge must carry the same value in the `x-helm-token` header, or it is refused with `401`. This matters **even on loopback**: any web page open in your browser can send requests to `localhost`, and the token is what distinguishes your AgentHelm UI from such a page. The Web UI sends the token it is given as `Bridge:ApiToken`.

### The permission gateway

Every permission request an agent makes passes the session's policy and is recorded:

- `ask` — the default — puts every request to you;
- policies can only *allow* automatically, never reject; automatic grants are one-off (*allow once*);
- YOLO requires an explicit confirmation per session;
- a request with no decision-maker attached is rejected;
- requests, answers, automatic decisions and policy changes are all transcript entries.

Details: [Permissions & policies](../guide/permissions.md).

### The working-directory path guard

When an agent asks the Bridge to read or write a file (ACP `fs/read_text_file`, `fs/write_text_file`), the path is resolved against the session's working directory and refused if it falls outside — checked both as written and after following symbolic links, so a link inside the directory cannot lead out of it. The Changes tab's git actions apply the same rule, and decide between *revert* and *delete* on the server, never from the request.

### Where the guard does not reach

An agent's own tools run **inside the agent's process, as your user**. If you allow an agent to run a shell command, that command can reach anything you can; the path guard only covers requests the agent sends *to the Bridge*. What protects you from an agent's tools is the permission gateway — which is why `ask` is the default and YOLO needs a confirmation.

## Powerful by design

These are deliberate, documented, and not vulnerabilities:

| Behaviour | Why it is fine — and what to keep in mind |
|---|---|
| The **Terminal** tab runs shell commands as you | It *is* your terminal. Anyone who can reach the Bridge can use it, so keep the Bridge on loopback and set the token. |
| **YOLO** auto-allows every tool call | Explicit per-session opt-in; every decision still audited. |
| Agents read and write inside the working directory | That is the job. |
| The **directory browser** lists directories anywhere on the Bridge's machine | Names only, no file contents; same token and bind as everything else. |
| The **Providers** page runs login and logout commands and shows account e-mails | Commands are fixed per provider; same token and bind. |
| Agent **telemetry** includes prompt content | `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true` in the Copilot entry and the launchers, meant for a local CopilotScope. Remove it before pointing telemetry anywhere else. |

## Hardening checklist

- [x] Keep `AgentHelm:Urls` on `127.0.0.1`.
- [ ] Set `AgentHelm:ApiToken` to a long random value, and the same value as `Bridge:ApiToken` for the Web UI — [how](../configuration/index.md#enabling-the-api-token).
- [x] Leave new sessions on `ask`; use `auto_read` only with agents whose tool kinds you trust, and YOLO only for sessions you are watching.
- [ ] Review the agent's changes in the [Changes](../guide/changes.md) tab before committing them.
- [ ] **Containers:** the compose files publish ports on all interfaces and ship a sample token (`dev-secret-123`). Bind the ports to `127.0.0.1` and replace the token before running them anywhere reachable.
- [ ] **Never** expose the Bridge to the internet. If you must reach it over a network, put it behind a reverse proxy with TLS and real authentication — see [Exposing the Bridge](../configuration/deployment.md#exposing-the-bridge).
- [ ] Pin agent CLIs to known-good versions where they update themselves.

The boxes that are ticked are the defaults.

## Secrets

AgentHelm stores no agent credentials: each agent CLI keeps its own login where it always does. The Providers page reads account names and e-mail addresses from those files for display. Transcripts contain whatever you and the agent wrote — including anything secret an agent printed — and are persisted to PostgreSQL when history is on; treat the database accordingly.

## Reporting a vulnerability

**Please do not open a public issue for a security problem.** Use GitHub's private vulnerability reporting: [**Report a vulnerability**](https://github.com/konradcinkusz/agent-helm/security/advisories/new). If that link does not work, contact the maintainer through [their GitHub profile](https://github.com/konradcinkusz) and ask for a private channel.

In scope: ways through the boundaries above — escaping the working-directory guard, getting a tool call executed without a recorded decision, anything a hostile web page can do against a default install, secrets leaking into transcripts or logs, and escalation beyond the user running AgentHelm. [`SECURITY.md`](https://github.com/konradcinkusz/agent-helm/blob/master/SECURITY.md) has the full scope, what a useful report contains, and what to expect.
