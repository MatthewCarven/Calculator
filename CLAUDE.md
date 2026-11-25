# Calc

A Windows desktop calculator with Natural (`../Natural`) as its number type. The solution skeleton is built (B1). The engine
starts in B2.

The folder is `Calc`, and its GitHub repo is [MatthewCarven/Calculator](https://github.com/MatthewCarven/Calculator), which is public.

- [PLAN.md](PLAN.md): what we're building and why.
- [TODO.md](TODO.md): the rules, the order of work and a brief for every session. It's the handoff between chats.
- [WORKLOG.md](WORKLOG.md): what happened, newest last.
- [docs/research/](docs/research/README.md): the measurements, spikes and reviews behind the plan.

At the start of a session, read TODO.md's "How a session runs", its rules and the brief named, then the latest WORKLOG entry. If
asked where things stand, the order of work is the map. Sessions run one agent, at "extra" effort, with no workflows.

Natural's sessions (A, D1, D2, T, I18 to I22) belong in a chat opened in the Natural folder, not here.

## Commands

```
dotnet build Calc.slnx
dotnet test Calc.slnx
src/Calc.Worker/bin/Debug/net10.0/calc-worker.exe --eval "1+1"
dotnet run --project src/Calc.Cli
dotnet build Calc.slnx -p:NaturalRoot=<folder>
```

The last one builds against another Natural checkout, such as a worktree, while a Natural session is building its own (rule 17).
Until B4, `--eval` prints 1/3 at 256 bits whatever it's given.

## Layout

| Where | What |
|---|---|
| `Directory.Build.props` | Every project: net10.0, nullable warnings as errors, **checked arithmetic**, and `NaturalRoot` (default `..\Natural\`), from which `NaturalProject` is derived |
| `src/WorkerBeside.targets` | Builds `calc-worker` first and copies its whole output beside a program that mustn't reference it. Imported by Calc.Cli, both test projects that run the worker, and Calc.App from C1 |
| `src/Calc.Syntax` | Empty until B2 |
| `src/Calc.Protocol` | `Wire`: the wire version and the worker's file name |
| `src/Calc.Engine` | `Link`, B1's proof that Natural is linked in, until B4's evaluator replaces it. Errors early, by name, if Natural isn't where `NaturalRoot` says |
| `src/Calc.Worker` | `calc-worker`: `--eval` now, the stdio loop in B9 |
| `src/Calc.Client` | Empty until B10 |
| `src/Calc.Cli` | Says whether the worker landed beside it, until B11 |
| `tests/Directory.Build.props` | The xunit packages, the same versions as Natural's tests |
| `tests/Calc.Engine.Tests` | `LinkTests`, against a BigInteger oracle. B2's Syntax tests go here too |
| `tests/Calc.Worker.Tests` | `EvalFlagTests`, which run the real exe |
| `tests/Calc.Client.Tests` | `LayeringTests` (rule 1), and `WorkerBesideTests`, which runs the worker from beside the tests. References Calc.Cli so it can read it |

## Gotchas

- **Checked arithmetic is on everywhere,** so a wrapped size or exponent throws instead of giving a valid-looking wrong answer
  (rule 5). Write `unchecked` where wrapping is meant. Compiler-made record hashes and System.Text.Json source generation are
  fine under it (probed 2026-11-25).
- **The SDK copies a referenced program's exe and config files by itself,** even through a build-only reference, and
  transitively, but not its DLLs. So `calc-worker.exe` beside a program proves nothing: a test has to run it.
- **A `-p:` property can't be changed inside a project.** That's why `NaturalProject` exists: it adds the slash `NaturalRoot`
  may lack.
- **Building Calc builds Natural's own project,** into Natural's `bin` and `obj`, with whichever SDK Calc gets. There's no
  `global.json`, so that's the .NET 11 SDK, targeting net10.0.
