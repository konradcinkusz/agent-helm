AgentHelm was built in four milestones, followed by a hardening pass on everything around the code. Both are complete. What comes next is the **Beyond** list — each item with the blocker that keeps it out of scope today.

The detailed plan, with sequencing, protected paths and execution policy, is [`ROADMAP.md`](https://github.com/konradcinkusz/agent-helm/blob/master/ROADMAP.md); the hardening work was tracked in [#17](https://github.com/konradcinkusz/agent-helm/issues/17).

## Milestones

| Milestone | Scope | Status |
|---|---|:---:|
| **M0 — skeleton** | ACP adapter, sessions, streamed chat, permission gateway with audit, PostgreSQL history, built-in echo agent | ✓ |
| **M1 — control** | Permission policies (`ask` / `auto_read` / `yolo` with explicit opt-in), session resume through `session/load`, history browser with read-only viewer, Copilot SDK adapter skeleton behind `COPILOT_SDK` | ✓ |
| **M2 — workbench** | Git diff viewer with audited accept/reject, integrated terminal (xterm.js over a shell pipe), image and text attachments as ACP content blocks, per-session model plumbing | ✓ |
| **M3 — multi-agent polish** | Agent handoff with a prefilled, user-sent summary; capability hints from `initialize`; CopilotScope scores inline; adapter factory by `Type`; PTY terminal on Unix through `script(1)` | ✓ |

Since M3 the UI has also gained the directory browser, the Providers page with login and model listing, and the model selector for new sessions.

## Post-M3 hardening

The milestones delivered the features; this pass fixed the pipeline around them — problems that produced no error, because a workflow that never fires looks exactly like one with nothing to do.

| Phase | Issues | Result |
|---|---|:---:|
| **1 — Pipeline integrity** | [#11](https://github.com/konradcinkusz/agent-helm/issues/11) CI never ran on `master` · [#12](https://github.com/konradcinkusz/agent-helm/issues/12) Pages never auto-deployed · [#13](https://github.com/konradcinkusz/agent-helm/issues/13) no job timeouts | ✓ |
| **2 — Security and supply chain** | [#14](https://github.com/konradcinkusz/agent-helm/issues/14) `SECURITY.md` · [#15](https://github.com/konradcinkusz/agent-helm/issues/15) Dependabot | ✓ |
| **3 — Documentation truth** | [#16](https://github.com/konradcinkusz/agent-helm/issues/16) a hard-coded test count that had drifted | ✓ |

Two follow-ups are repository **settings** rather than code: enabling private vulnerability reporting, and setting the repository's *Website* field to this site.

## Beyond

Deliberately out of scope for now. Each item is the right next step once its blocker lifts.

| Item | Blocker |
|---|---|
| **Finish the Copilot SDK adapter** — the four `TODO`s in `CopilotSdkAdapter.cs` | Needs the GitHub Copilot SDK preview package and a machine that can build against it; writing it blind would produce speculative code. See [Agent adapters](Agent-Adapters.md#the-copilot-sdk-adapter). |
| **Exact CopilotScope correlation** through telemetry tagging | Needs a matching change in [CopilotScope](https://github.com/konradcinkusz/copilotscope). The time-based match is best-effort and says so. |
| **ConPTY terminal for Windows** | Needs a Windows machine to build and verify; Unix already has a PTY. |
| **Git worktrees**, system-message overrides, plugins, a canvas | Feature work, not yet planned. |
| **Renaming `master` → `main`** | Would break existing clones, the `raw.githubusercontent.com/…/master/…` URL the install instructions use, and external links. A maintainer decision, not a side effect. |
| **A `dotnet format` gate in CI** | Needs a one-off formatting pass over the codebase first, as its own change. |

The [known issues](Troubleshooting-and-FAQ.md#known-issues) are bugs rather than roadmap items and should be fixed first.
