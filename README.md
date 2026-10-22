# OpenLoop

English | [简体中文](README.zh-CN.md)

OpenLoop is a self-evolving agent facility: a **Worker** does the actual work for the user, while a **Loop** observes, collects evidence, and iterates on the next version of the Worker. They share one process and one lifetime, but run on different rhythms — the Worker follows tasks, the Loop is event-driven.

OpenLoop is **not an agent itself**; it is the facility agents run inside. The "Open" in the name means the loop never closes: the world (and the user) always stays part of it.

## Features

- Tool-using agent loop with a single-line `tool-call:` protocol
- Built-in tools: `read_file`, `write_file`, `list_dir`, `grep`, `run_command`
- Guardrails: repeat-call detection, exploration budget, claim/promise checks, argument repair, malformed-call retry, context compression
- Append-only ledger for features, activations, verdicts, and run evidence
- A/B gate: candidate versions are adopted only when they are strictly better; ties keep the old version
- Rollback to any historical version without rewriting old evidence
- Streaming output, persistent chat history, and full traces in `.openloop/`
- WinForms desktop host with an OpenAI-compatible backend (default: OpenRouter free models)

## Repository layout

```
OpenLoop.slnx
├── src/OpenLoop.Core/        agent loop, tools, ledger, gate, reasoning (no UI)
├── src/OpenLoop.App/         WinForms host
├── tests/OpenLoop.Core.Tests/
├── docs/IMPLEMENTATION.md    implementation plan and status
└── CHARTER.md                the normative specification (Chinese)
```

Dependencies:

```
OpenLoop.App ──▶ OpenLoop.Core
      └───────▶ ChatFlow (shared chat control)
      └───────▶ Puppet.Core / Puppet.Core.WinForms (UI automation)
```

## Requirements

- Windows 10/11
- .NET 10 SDK
- `ChatFlow` and `PuppetCore` checked out as sibling directories of this repository (both are referenced by `ProjectReference`):
  - `../ChatFlow/ChatFlow/ChatFlow.csproj`
  - `../PuppetCore/Puppet.Core/Puppet.Core.csproj`
  - `../PuppetCore/Puppet.Core.WinForms/Puppet.Core.WinForms.csproj`

## Build and test

```powershell
dotnet build OpenLoop.slnx
dotnet test tests\OpenLoop.Core.Tests
```

## Run

```powershell
Stop-Process -Name OpenLoop -Force -ErrorAction SilentlyContinue
Start-Process bin\Debug\net10.0-windows\OpenLoop.exe -WorkingDirectory .
```

Configuration lives in `%APPDATA%\OpenLoop\settings.json` (`baseUrl`, `model`, encrypted key, workspace). When the key field is empty, `OPENROUTER_API_KEY` or `OPENAI_API_KEY` is used. Runtime state, traces, and the ledger go to `.openloop/` in the workspace.

Defaults: `https://openrouter.ai/api/v1` with `openrouter/free`.

## Documentation

- [CHARTER.md](CHARTER.md) — the specification: invariants, ledger contract, conformance (Chinese)
- [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md) — architecture and milestones (Chinese)

## License

[MIT](LICENSE)
