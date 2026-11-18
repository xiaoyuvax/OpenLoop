# OpenLoop

English | [简体中文](README.zh-CN.md)

**A self-iterating open agent.**

OpenLoop splits "doing the work" from "improving itself" into two roles: the **Worker (A, foreground)** completes real tasks for the user, while the **Loop (I, background)** observes the Worker, user feedback and runtime evidence, and ships the next version of the Worker and shell. They are separate processes, launched, supervised and swapped by one **shell**.

The "Open" in the name means the loop never closes: the world (and the user) always stays part of it. The Loop's judgement comes from real use, not from a sealed self-assessment.

## What it is today

### Roles

| Role | Executable | Responsibility |
|---|---|---|
| Shell | `OpenLoop.exe` (WinForms) / `OpenLoop.Shell` (cross-platform CLI) | The only process orchestrator: launches A/I, hosts the UI, performs stage / restart / rollback |
| Worker (A) | `OpenLoop.A` | Sessions, tool calls, approvals, streaming output, traces; also serves the web UI and `/api/*` |
| Loop (I) | `OpenLoop.I` | Per-project observation, reading evidence, recording findings, drafting eval tasks, running iterations (`/api/loop/*`) |

- The desktop and web shells stay feature-aligned. The web UI is served by A on its bus port (`http://<bus>/?key=...`); the desktop shell embeds WebView2 in its side panel.
- The Loop's write scope is limited to the config directory (`%APPDATA%\OpenLoop`), the OpenLoop source worktree, and the binary version directory. Its read scope is as wide as the Worker's.
- The Loop does not modify its own source. The I binary is replaced by A (I cannot restart itself or replace a running shell).

### Capabilities

- **Worker**: multiple sessions (closing a tab does not stop it), streaming body and reasoning, pending queue with "send now", `/btw` read-only side work, `/fork` to clone context, `/new` sessions, model and reasoning-effort selection, context usage, cost and step count, full traces, and surveys (AskUser).
- **Tools**: built-ins such as `ReadFile`, `WriteFile`, `ListDir`, `Grep`, `Shell`; `ReadEvidence`, `RecordFinding`, `ProposeTask` for the evidence channel; skills (built-in and project-level); external `OpenCodeTask` / `DshTask`; read-only `WebFetch`; Puppet for probing the peer process and its UI.
- **Loop**: one Loop session per project; reads the ledger, traces and findings; turns conclusions into findings or eval-task drafts; produces and executes iteration plans.
- **Models and external agents**:
  - **opencode**: the local OpenCode v2 service is discovered automatically via `~/.local/state/opencode/service.json` and shows up as an OpenCode provider once ready. Model ids look like `opencode/<model>@<provider>`; use one as the session model, or hand a whole task to its agent with `OpenCodeTask` (tools, session and context stay on its side, permissions still come back to you). Free models are marked.
  - **dsh**: with dsh installed it appears as a provider, model ids look like `dsh/<provider>/<model>`; `DshTask` hands a task to a dsh agent, which you want for its plugin ecosystem (e.g. `our-free-model`'s free models) or dsh-only tools. The model list is probed in the background, so reopen the dropdown a beat later.
  - **Anything else**: any OpenAI-compatible endpoint (`baseUrl` + `model`, OpenRouter by default).
- **Guardrails**: repeat-call detection, exploration budget, argument repair, malformed-call retry, context compression, approval for sensitive actions.
- **Puppet**: A, I and the shell each expose a Puppet service, so agents can probe state and call the operation surface (named actions on the B face). UI debugging does not rely on OS-level automation.

### Iteration and versioning

- Two parallel tracks: **evolution** changes the version definition (prompts, skills, policies, model parameters, active models and tool sets); **iteration** swaps executables (Worker `OpenLoop.A`, Loop `OpenLoop.I`, shell `OpenLoop.App`).
- One role per iteration, with matching protocol numbers; version definitions and binaries are archived separately.
- Version graph: `PuppetCore → Service → {A, I, shell}`, `ChatFlow → shell`, `Core → Service`. When an upstream changes, downstream must be bumped, cascading along dependency edges.
- Entry point: the Loop page's **Iterate** button → a plan first (who is bumped, current → target, change summary) → user confirms → build → swap A → A updates I → finally publish the new shell and let it restart itself (the UI briefly exits).
- Failures roll back: the whole chain is restored from its snapshot; a failed build stays in the source tree with the diff preserved, never producing a half-built version.
- Before swapping the shell it checks for running sessions (Worker / Loop / side work) and asks first.
- Version directories: `%APPDATA%\OpenLoop\versions\<role>\<ver>-<sha8>`; each role keeps a limited number of old versions (A/I keep current + previous, the shell also keeps rollback backups).

## Repository layout

```
OpenLoop.slnx
├── src/OpenLoop.Core/        agent loop, tools, guardrails, ledger, skills, traces (no UI)
├── src/OpenLoop.Service/     everything around orchestration: worker host, Loop, Web/API, versions, storage
├── src/OpenLoop.App/         Windows shell (WinForms)
├── src/OpenLoop.Shell/       cross-platform shell (CLI)
├── src/OpenLoop.A/           Worker executable
├── src/OpenLoop.I/           Loop executable
├── tests/OpenLoop.Core.Tests/
├── tests/OpenLoop.Service.Tests/
├── docs/                     implementation, dual-process, versioning, task sourcing
└── CHARTER.md                specification (invariants, permissions, swap order)
```

Compile-time dependencies:

```
PuppetCore ──▶ Service ──▶ { A, I, App/Shell }
Core       ──▶ Service
ChatFlow   ──▶ App（desktop shell chat control）
```

## Requirements

- Windows 10/11 (desktop shell); the cross-platform shell can run elsewhere
- .NET 10 SDK
- The development layout needs sibling checkouts (all referenced by `ProjectReference`):
  - `../ChatFlow/ChatFlow/ChatFlow.csproj`
  - `../PuppetCore/Puppet.Core/Puppet.Core.csproj`
  - `../PuppetCore/Puppet.Core.WinForms/Puppet.Core.WinForms.csproj`
- The install layout has no siblings; sources are cloned by the startup sync (next section)

### Source sync

- The development layout compiles the sibling checkouts directly and copies nothing into the install directory; component repos (`PuppetCore`, `ChatFlow`) that are already checked out only report their commit, with no network call.
- The install layout shallow-clones `OpenLoop`, `PuppetCore` and `ChatFlow` (compiled on Windows only) into `<install root>/src/` (`src\openloop`, `src\PuppetCore`, `src\ChatFlow`).
- An existing clone is only `fetch --depth 1`-ed, never `reset` / `checkout`-ed over the worktree — that tree is what the Loop edits.
- The components that take part in iteration live in `components.json` at the repo root (url, checkout dir, assembly, version file, who references it); a missing file, or a missing built-in entry, falls back to the built-in `PuppetCore` / `ChatFlow` defaults (official URLs). Adding a third-party fork means adding one entry and naming its consumer.
- A component whose origin differs from its catalog URL has been switched to a fork: pushes go to the fork, and the Loop updates that component's `url` in `components.json` to match.
- After syncing, the source version is compared with the running one: newer source → "ready to iterate"; newer upstream (origin) → asks you to update the source first, without blocking iteration on the local code.

## Build and test

```powershell
dotnet build OpenLoop.slnx
dotnet test tests\OpenLoop.Core.Tests
dotnet test tests\OpenLoop.Service.Tests
```

Tests for the chat control live in the sibling repo: `dotnet test ..\ChatFlow\ChatFlow.Tests`.

A Debug build copies the A/I outputs into `backends\` under the shell output directory. Release/published artifacts contain only the shell; `backends\` is filled by the iteration pipeline.

## Run

```powershell
Stop-Process -Name OpenLoop -Force -ErrorAction SilentlyContinue
Start-Process src\OpenLoop.App\bin\Debug\net10.0-windows\OpenLoop.exe -WorkingDirectory <repo root>
```

Configuration lives in `%APPDATA%\OpenLoop\settings.json` (`baseUrl`, `model`, encrypted key, workspace, projects). When the key is empty, `OPENROUTER_API_KEY` or `OPENAI_API_KEY` is used. Runtime state, traces and the ledger go to `.openloop/` in the workspace; binary versions plus Puppet endpoint/key live in `%APPDATA%\OpenLoop\`.

Defaults: `https://openrouter.ai/api/v1` with `openrouter/free`.

## Documentation

- [CHARTER.md](CHARTER.md) — roles, permission boundaries, versioning and swap order (Chinese)
- [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md) — architecture and milestones (Chinese)
- [docs/PLAN-DUAL-PROCESS.md](docs/PLAN-DUAL-PROCESS.md) — dual-process status and acceptance (Chinese)
- [docs/PLAN-VERSION-MANAGEMENT.md](docs/PLAN-VERSION-MANAGEMENT.md) — version graph, fingerprint cascade, iteration flow (Chinese)
- [docs/DESIGN-TASK-SOURCING.md](docs/DESIGN-TASK-SOURCING.md) — where eval tasks come from (Chinese)
- [docs/CONFORMANCE.md](docs/CONFORMANCE.md) — language-neutral contracts and test vectors

## License

[MIT](LICENSE)
