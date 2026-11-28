# Calc

A Windows desktop calculator with Natural (`../Natural`) as its number type. The solution skeleton (B1), the lexer (B2), the
parser (B3), the evaluator with its float core (B4), functions and powers (B5), result text (B6), exact values (B7), exact
functions and display (B8), the worker process (B9) and the worker client with Stop (B10) are built. The session, the clear
rule and the console calculator are next.

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
src/Calc.Worker/bin/Debug/net10.0/calc-worker.exe
src/Calc.Worker/bin/Debug/net10.0/calc-worker.exe --eval "0.1+0.2" --bits 53
src/Calc.Worker/bin/Debug/net10.0/calc-worker.exe --eval "1/0" --format binary64 --mode up
dotnet run --project src/Calc.Cli
dotnet build Calc.slnx -p:NaturalRoot=<folder>
```

With no arguments the worker is the stdio loop the Client talks to: one JSON request a line, such as
`{"type":"eval","id":1,"source":"1/3","context":{"target":{"precision":53}}}`, one reply a line, until stdin ends (which
ends it, even mid-evaluation). Piping lines in with `printf` closes stdin at once, so hold it open, `(printf ...; sleep 3) |`,
or the evaluations die unanswered. `--eval` takes `--bits` (default 256) or `--format binary16|32|64|128|256`, and `--mode nearest|zero|up|down`. It prints the
result as the window will, `ResultText.Main`, the digits the precision guarantees (so `0.1+0.2` at 53 bits is 0.3), to
stdout; `--raw` prints every digit that reads back instead, and `--notation auto|scientific|engineering|fixed|aligned` and
`--group <separator>` pick the style, and `--caption` adds the line under the result, such as `exact = 3/10` or `float, 76
digits`. Exact is on by default, as in the window, so `0.1+0.2 --raw` is 3/10; `--exact off` gives floats. Flags go to stderr; an error goes to stderr with its kind and span, exit code 1. The last command builds
against another Natural checkout, such as a worktree, while a Natural session is building its own (rule 17).

## Layout

| Where | What |
|---|---|
| `Directory.Build.props` | Every project: net10.0, nullable warnings as errors, **checked arithmetic**, and `NaturalRoot` (default `..\Natural\`), from which `NaturalProject` is derived |
| `src/WorkerBeside.targets` | Builds `calc-worker` first and copies its whole output beside a program that mustn't reference it. Imported by Calc.Cli, both test projects that run the worker, and Calc.App from C1 |
| `src/Calc.Syntax` | `Lexer` (text to `Token`s, by loops, never recursion); `Parser` (tokens to a tree of `Node`s, in `Ast.cs`, by Pratt parsing, refusing past 200,000 levels or a low stack); `Printer` (`Print` reads back to the same tree, `Understood` is the on-screen form, as in `(1/2)·x`); `TextSpan`; `CalcError` with its `ErrorKind` |
| `src/Calc.Protocol` | `Wire`: the wire version, the worker's file name and `LosslessLimit` (1 MiB). `EvalContext` (with `TargetSpec`, `Rounding`, `AngleUnit`, `BudgetSpec`): everything an evaluation depends on besides its text. `Messages.cs`: every request (`eval`, `digits`, `reformat`, `fetch`, `functions`, `stop`, `ping`) and reply (`result`, `digits`, `value`, `error`, `progress`, `functions`, `pong`) as records, tagged by `"type"`, with source-generated JSON; `Messages.Write` and `ReadRequest`/`ReadReply`. `Digits`: `For(bits)`, the digits a precision guarantees (rule 9), and `BitsFor(digits)` |
| `src/Calc.Engine` | `Values/`: `Target` (bits or an `IeeeFormat`) and `Value`: `FloatValue` (with `ExactTooLarge` for the chip), `IntValue`, and `RatioValue` (lowest terms, d > 1, made by `RatioValue.Of`). `ExactArithmetic` keeps them exact through + − × ÷, negate, %, mod, rem, integer powers, the bit operators, comparisons, and abs, sign, floor, ceil, trunc, round, max, min, fma and the sqrt of a perfect square (`ExactFunctions` has the ones with no budget), within `ExactBudgets`, checked from bit lengths before computing. Where an exact value meets a float or outgrows a budget, the answer is the exact one rounded once, never the exact value rounded first: × ÷ by one exact division, + − sqrt ^ and functions through `RoundedCall` (it brackets an exact argument from below and above, doubling the width until the ends agree; public, for the W sessions), mod rem fma by taking the float as the fraction it is. `Text/Lossless` is the tagged text form for every kind, for the wire. `Eval/`: `Evaluator` (walks the tree, left before right, knowing no number types), `IArithmetic` and `FloatArithmetic` (every operation through one `IeeeContext`; Mathematical policy at bits, IEEE at a format; floored mod, truncated rem, % as ÷100, bit operators on whole numbers), `FunctionTable` (every function's name, arity and the session it waits on), `PowerFloat` (integer powers: T10's specials, an exact route and a bounds route), `Names` (constants, `Ans`), `ErrorMap` (rule 12). `Text/`: `ResultText.Of(value, target, options)` gives `Short` (under 40 characters), `Main` (every guaranteed digit by G to 1,300 digits, then a 100-digit preview; at a format, the shortest text that reads back; a fraction's digits worked out from n/d exactly), `Kind` (the chip: exact, exact = n/d, float, float (exact too large)), `Caption` (the line under the result), `Digits` and `Complete`; `TextOptions` picks the notation and grouping. `Eval/Contexts` maps Protocol's records to the Engine's types. `Evaluator` checks a stop token on every node (B9's soft stop). Errors early, by name, if Natural isn't where `NaturalRoot` says |
| `src/Calc.Worker` | `calc-worker`. `Loop`: the stdio loop. The reader thread answers ping and stop at once; everything else runs in order on a BelowNormal compute thread with a 256 MiB stack (rule 13). A malformed line, an error or an `OutOfMemoryException` gets an error reply and the loop goes on. Lossless text past 1 MiB is held (`HeldValues`) by the reply's id, named by `Ans` or `fetch`, the oldest dropped past a quarter of the heap cap; a float `Ans` from another target is refused (rule 14). `--eval` too, for agents |
| `src/Calc.Client` | `IWorker`: the worker as tasks, never throwing for a stop, a death or short memory, each an `ErrorReply`. `WorkerClient`: the real one. It starts calc-worker with the first request, gives each request the next id, reads replies on a dedicated thread, and drops any nothing waits for. `StopAsync` sends the soft stop, waits 150 ms, then kills the process tree, and the next request starts a new worker; one that dies by itself completes as `WorkerDied`; `Dispose` kills and waits. `CommitInfo` (free commit, `GlobalMemoryStatusEx`) and `HeapCap`: the 4 GiB setting or free commit less 1 GiB, in Int128, no worker below 256 MiB, written in hex. No Natural |
| `src/Calc.Cli` | Says whether the worker landed beside it, until B11 |
| `tests/Directory.Build.props` | The xunit packages, the same versions as Natural's tests |
| `tests/Calc.Engine.Tests` | `Eval/`: random arithmetic trees from `ArithmeticTreeGenerator`, bit for bit against `double` and `float`, and at random precisions and modes against `RationalOracle`, a BigInteger fraction that rounds by its own code; functions against `Math` and `MathF`, sqrt by an exact bracket check, powers, mod and the rest against the rational oracle, bit operators against `BigInteger`; exact values against an exact fraction oracle, each budget from both sides, Muller's sequence; B8's exact functions, bit operators and comparisons against BigInteger fractions, `RoundedCall` by stand-in functions (its widths, its bound, an erroring end), and every mixed or over-budget operation against the exact answer rounded once (`ExactHelpers` builds the fractions); a table of literals, flags and every error kind; depth tests. `Text/`: `Digits` for every preset and 2,000,000 precisions against a 128-bit constant; each notation; grouping; text read back within half a unit of its last digit; formats' shortest text against .NET's; timing at 2^20 bits; a fraction's digits against BigInteger, and m/2^k's text against the same float's. `Syntax/`: the lexer's table, and fuzzes against `ReferenceLexer`, a second lexer written with regular expressions; the parser's table, every operator pair, round trips of `TreeGenerator`'s random trees (`TreeText` shows a tree as an S-expression), a fuzz, and the depth tests, which run alone in the "Deep stacks" collection |
| `tests/mutations/` | Each session's mutation battery, so it can be run again. Each matches its own session's code: later edits can leave an old mutation's text unmatched, which `check` reports |
| `tests/Calc.Worker.Tests` | `LoopTests`, which drive the real exe's loop through `WorkerProcess` (every wait capped): ids, 200 evaluations, malformed lines, the soft stop, ping while busy, stdin closing, an 830,000-character reply, `Ans` chains, held values and their eviction under a heap cap, out of memory, the depth limits; `MessagesTests`, every message to its line and back; `EvalFlagTests`, which run `--eval` |
| `tests/Calc.Client.Tests` | `WorkerContract`: what every `IWorker` promises, run against the real client and `FakeWorker` (replies from a function, held until released or stopped; B11's Session tests use it). `WorkerClientTests`, on the real exe: Stop inside a 2,000,000-bit multiply (about 170 ms to Stopped, and 170 ms more to a new worker's answer), the soft stop with no kill, a worker killed from outside, the heap cap as the child reports it, free commit at 0.5, 1.0, 1.1, 1.25, 3 and 5 GiB, out of memory recovered, 20 stop-and-go cycles leaving nothing, dispose leaving nothing, and a scripted batch-file worker for stale and progress replies. `HeapCapTests`. `LayeringTests` (rule 1), and `WorkerBesideTests`. Every wait is capped with `Soon()`. References Calc.Cli so it can read it |

## Gotchas

- **Checked arithmetic is on everywhere,** so a wrapped size or exponent throws instead of giving a valid-looking wrong answer
  (rule 5). Write `unchecked` where wrapping is meant. Compiler-made record hashes and System.Text.Json source generation are
  fine under it (probed 2026-11-25).
- **The SDK copies a referenced program's exe and config files by itself,** even through a build-only reference, and
  transitively, but not its DLLs. So `calc-worker.exe` beside a program proves nothing: a test has to run it.
- **A `-p:` property can't be changed inside a project.** That's why `NaturalProject` exists: it adds the slash `NaturalRoot`
  may lack.
- **Anything recursive must survive 200,000 levels on 256 MiB in Debug,** where every local gets its own stack slot. Keep
  recursive methods lean (errors built in `NoInlining` helpers), and check `RuntimeHelpers.TryEnsureSufficientExecutionStack()`
  on each level so a low stack is a `TooBig` error, never an overflow. **A stack overflow pops up a Windows dialog on
  Matthew's screen** ("A new guard page for the stack cannot be created"), so never probe a limit by crashing: binary-search
  in process, with the guard in place.
- **Deep-stack tests go in the "Deep stacks" collection,** which runs alone. Every GC scans every thread's stack, so deep
  stacks side by side, or beside the fuzzes, ran ten times slower.
- **Every literal rounds to the target, exponents too.** At 7 bits, `x^786` is x^784. A test that wants an exact operand
  builds the `FloatValue` itself.
- **xUnit mangles a lone surrogate in `[InlineData]`,** so a test case that needs one is a `[Fact]`.
- **Mutation batteries** follow `../Natural/CLAUDE.md`'s recipe, running `dotnet test tests/<project>`. Don't build while one runs.
- **The wire's JSON requires every constructor parameter without a default,** nullable ones too, and leaves nulls out when
  writing. So a nullable field a reply may omit needs a default (`string? Lossless = null`), or `[JsonIgnore(Condition =
  Never)]` to be written as null; otherwise its own reply won't read back.
- **`DOTNET_GCHeapHardLimit` is read as hex** (`0x8000000` is 128 MiB). Tests use it to make the worker run out of memory or
  evict held values quickly.
- **Building Calc builds Natural's own project,** into Natural's `bin` and `obj`, with whichever SDK Calc gets. There's no
  `global.json`, so that's the .NET 11 SDK, targeting net10.0.
