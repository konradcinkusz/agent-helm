---
description: The AgentHelm test suite — what each test class covers, the test doubles, and how to run and extend it.
---

# Testing

The tests live in [`tests/AgentHelm.Tests`](https://github.com/konradcinkusz/agent-helm/tree/master/tests/AgentHelm.Tests) (xUnit, one file: `CoreTests.cs`) and exercise the Bridge. They are fast, run in CI on every pull request and every push to `master`, and must pass before a release is built.

## Running

```bash
dotnet test                                             # everything
dotnet test --filter "FullyQualifiedName~PolicyEngine"  # one area
dotnet test --logger "console;verbosity=normal"         # what CI prints
```

A few tests start real processes — `git`, a shell, `script(1)` — so they need `git` and a POSIX shell; the PTY test only runs its PTY assertions where `script` is available.

## What the suite covers

| Test class | Covers |
|---|---|
| `AcpClientTests` | The ACP client over a scripted transport: `initialize` parameters, `session/new`, streaming order and stop reason, permission round-trips, safe rejection without a handler, the path guard for reads, error propagation, `session/cancel` as a notification. |
| `AcpLoadSessionTests` | Reading the `loadSession` capability, `session/load` replaying history before it completes, refusing to load without the capability. |
| `CapabilityTests` | Parsing prompt capabilities from `initialize`. |
| `AttachmentTests` | Image and resource content blocks in `session/prompt`; attachment names in the user entry. |
| `HelmSessionTests` | User and assistant entries, the blocking permission flow and its audit entries, live event fan-out and unsubscribe. |
| `PolicyEngineTests` | The policy taxonomy, `auto_read` kinds, `fetch` never auto-allowed, *allow once* preferred, no allow option → ask, validation. |
| `HelmSessionPolicyTests` | YOLO auto-allowing without a pending request but with an audit entry; `ask` still surfacing requests; policy validation and de-duplication. |
| `GitServiceTests` | Porcelain parsing including rename tokens, diff counting, the path guard, reject semantics (checkout vs delete), and an end-to-end run against a real git repository. |
| `TerminalServiceTests`, `PtyTerminalTests` | Real shell round-trips in pipe and PTY mode. |
| `AgentFactoryTests` | Clear errors for unknown adapter types and for `copilot-sdk` without the build flag. |
| `HandoffTests` | Handoff context attribution and front-trimming of long transcripts. |
| `ScopeIntegrationTests` | Tolerant parsing of CopilotScope's JSON shapes; time-window correlation. |
| `SessionTitleTests` | Title validation, publication and persistence in the session. |
| `SpecExpansionTests` | `${AGENTHELM_DIR}` expansion in commands and arguments. |

The suite once caught a real bug: the configuration binder silently dropped agents declared without `Args`; `AgentSpec.Args` became an init property with a default as a result.

> [!NOTE]
> This page lists what the suite covers rather than how many tests it has — a count in prose goes stale with the next test and nothing would notice.

## Test doubles

- **`FakeTransport`** — an in-memory `IAcpTransport`. Tests script what the "agent" says and assert on what the client wrote. This is how the ACP client is tested without any agent binary.
- **`FakeAdapter`, `ToolAdapter`** — `IAgentAdapter` implementations that emit chosen events and permission requests, to test sessions and policies without ACP.
- **`FakeGitRunner`** — an `IGitRunner` returning canned `git` output, for parsing and reject logic.

Prefer these seams over mocking frameworks; the suite has none.

## Writing tests

- Test behaviour at the seam that owns it: protocol details against `AcpClient` with `FakeTransport`, session behaviour with a fake adapter, policy with `PolicyEngine.Decide` directly.
- Security invariants — the path guard, *policies only allow*, server-side reject decisions — deserve a test for every new code path.
- Keep tests independent of the machine: create temporary directories, clean them up, and guard platform-specific assertions (see the PTY test).

## Beyond unit tests

Before a release, walk through the manual smoke test in [CI/CD & releases](ci-cd.md#release-checklist): an echo session from a cold start, a permission request, YOLO, history and resume, and one real agent.
