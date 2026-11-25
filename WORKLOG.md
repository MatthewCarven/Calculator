# WORKLOG

## 2026-11-07: the plan

Matthew asked for a plan for a fully featured calculator on Natural, with selectable precision and setting it clearing the calculator,
plus any extras, broken down small enough to build at "extra" effort. No code was written, and Natural wasn't changed.

**Done:** five read-only investigations against scratch copies of Natural (API gaps, timings, UI stack, transcendentals, extras), then a
first draft, then five critics against that draft (cold start, dependency order, session size, fact check, design holes). They found 111
problems, 4 of them blockers. A rewrite, then two more reviewers against that: a regression check of every serious finding, and a
second cold start. They confirmed 38 of 48 serious findings fixed and found 50 more problems, 22 of them major, none blockers. PLAN.md and
TODO.md include all the fixes. Everything is in docs/research/.

**What the review changed:**
- Sessions went from 84 to 98, and from 20 to 23 before the first release. The draft's B4 and B6 were each the size of Natural's largest
  session, the ruler being real sessions' lines and turns. Some small sessions merged.
- `Ans` became a line reference. Text alone couldn't recompute a keypad chain: `2+3`, `Ans*4` gave "no answer", or silently a wrong number.
- Exact arguments now stay exact into functions, mixed operators and `^` (`RoundedCall`). Rounding them first made `ln(1+1e-80)` 0 at 256
  bits, lost 3 of 15 digits of `1.1^5000`, and made "correctly rounded" false.
- Silent wrong answers closed in the briefs: `3^(2^62)` gave 1 and `(2^(2^62))^2` a tiny number, from wrapped longs; `sin(30°+15°)` at Angle deg
  gave 0.0137; `ln(sqrt(1+1e-80))` was promoted to exact 0; `8 ÷ 2 1/x` on the keypad gave 4.
- New: one `EvalContext` on every line and request; `Calc.Syntax`, so the Client can tokenise; a 256 MiB compute stack, because a flat
  `1+1+…` is a deep tree that kills a default stack at about 3,500 terms; values over 1 MiB stay in the worker.
- Facts corrected: `DOTNET_GCHeapHardLimit` is read as hex; the digits constant was wrong for 6% of p; `3^(3^20)` never ran long enough
  to test Stop; Rump's and Muller's figures only hold with Exact off.

**Found in Natural, for A1 and A2:**
- 9b's code is finished and 682 tests pass, but it's uncommitted and its mutation battery stopped on 14 October with 44 of 59 to run. Its
  script survived only in that chat's transcript, and is recovered at docs/research/natural-api/mutate-9b.py. Checked: all 59 old strings
  still match once, all 59 build in a copy, and S13 goes red in 20 s.
- `1e2776511644261678567` parses and then compares as less than 1. Confirmed separately.
- GitHub lacks 9a and 9b, so the link in the request is behind the local folder.

**Checked by hand, not only by agents:** the `Top` wrap; display digits at 256 bits (G78 already shows noise, so 76 is the right
guarantee); `IeeeContext` overriding operand precision; contamination; hex and word-size quirks; 9b's stopped battery, from its transcript.

**Next:** Matthew answers the six questions in PLAN.md's Decisions section and runs `git init` here. Then A1, in a chat opened in Natural.

## 2026-11-07, later: the answers

Matthew took all six recommendations ("Yes take all six please Claude") and ran `git init`, so the plan's assumptions are now decisions:
the clear keeps history, exact-first is on by default, WPF with a worker process, sin, log and π in Natural, the parallel-prefix adder within
the bitwise rule, and the order D, E, F, G, H, I. PLAN.md and TODO.md say so, and the briefs lost their "on a no" branches. A1 now also carries the
two answers that concern Natural into its TODO, because a chat here doesn't edit Natural.

First commit made. There's no GitHub remote yet, so sessions commit and skip the push until there is.

Later the same evening Matthew made the repo, https://github.com/MatthewCarven/Calculator, public and empty, and `origin` now points there.
Before the first push, the commit was scanned for secrets, hostnames, addresses and other projects' details: nothing but local paths in
scratch headers and one passing mention of Chess Bruteforcer. Sessions push as normal from here.

**Next:** A1, in a fresh chat opened in the Natural folder, at "extra" effort with ultracode off.

## 2026-11-22: A1 done

Run from this chat with the Natural folder added, rather than a chat opened in Natural, at Matthew's asking; Natural's CLAUDE.md
and memory were read by hand. 9b committed (d0a8f41); its battery finished, 59 of 59 red; Natural's docs made true; the T plan
copied into `../Natural/docs/transcendentals/`, the live copy now; Natural's order of work lists A2, D1, D2, T and I with
Matthew's two answers. Natural pushed: origin went from c06ef4c to 9b2dc83, so GitHub has 9a and 9b at last. Details are in
Natural's WORKLOG.

Found on the way: a .NET 11 SDK (11.0.100) is installed beside 10.0.404, and file-based apps without a `TargetFramework` now run on
.NET 11. Calc targets net10.0, so B1 should say so, and any bench or probe here wants `#:property TargetFramework=net10.0`.

**Next:** A2, three bugs a calculator can reach, in Natural.

## 2026-11-25: A2 done

Run from this chat again, with the Natural folder added. All three bugs fixed in Natural, each with a test that failed
first: a value's leading bit is kept inside a long where values are made, and the format code's two raw sums saturate; the
radix digit count can't wrap, and "B" refuses over-long text before building it; and `Ieee754Remainder` takes `%`'s
early-out. 682 → 693 tests; 15 mutations, 14 red, 1 equivalent and predicted. Natural pushed at eecf078. Details are in
Natural's WORKLOG.

For Calc: the print edge stays. Values whose top bit is at 2^63 − 2 or above parse but throw `OverflowException` on decimal
output, so B6's ErrorMap maps a `ToString` overflow to `Overflow`, as its brief already says.

**Next:** B1, the solution skeleton, the first session in Calc itself.

## 2026-11-25: B1 done

The solution skeleton, from `docs/research/ui/skeleton/` with the brief's changes: `Calc.Syntax`, `Calc.Protocol`, `Calc.Engine`,
`Calc.Worker`, `Calc.Client` and `Calc.Cli`, and three test projects, 8 tests. `calc-worker --eval x` prints 1/3 at 256 bits to
76 digits, from Natural. Builds with 0 warnings. CLAUDE.md now has the commands, the layout and the gotchas.

**Choices made here:**
- The copy step is one shared file, `src/WorkerBeside.targets`, rather than three copies. It copies from the worker's reported
  output path, not a hard-coded `bin\Debug\net10.0`.
- Engine references `$(NaturalProject)`, derived from `NaturalRoot`, and fails early, by name, if Natural isn't there.
- Checked arithmetic is on in every project, as a net under rule 5. Records and JSON source generation are fine under it.
- Nullable warnings are errors. The test packages are the same versions as Natural's.
- `Calc.Client.Tests` references `Calc.Cli` so `LayeringTests` can read it.

**Broken on purpose, each red:** a Natural reference and use in `Calc.Client` turned `LayeringTests` red for that assembly
alone (and without the reference, it didn't compile at all); `G77` for `G76` turned both digit tests red; removing the copy
step from `Calc.Client.Tests` turned `WorkerBesideTests` red after a clean. A `NaturalRoot` with no Natural gives the named
error, with or without its trailing slash.

**Found on the way:** the SDK copies `calc-worker.exe` and its config files beside any project that references the worker,
even build-only and transitively, but not its DLLs. So the first version of the copy-step test, which checked the exe was
there, passed with the copy step removed. It runs the worker now. C1's brief says so, and also warns that a net10.0 test
project probably can't reference the net10.0-windows App.

**Next:** B2, the lexer.

## 2026-11-25, later: B2 done

The lexer: `Calc.Syntax/Lexer.cs`, `Token.cs`, `TextSpan.cs` and `CalcError.cs`. Text goes to tokens with spans, and bad input
gives only a `CalcError` of kind Syntax, with a span and a plain message. It loops and never recurses: a million-term sum lexes
on a 256 KB stack in well under a second. 127 tests in all, 119 of them the lexer's.

**Tested against an oracle:** `ReferenceLexer`, a second lexer written from the brief with regular expressions, sharing no code.
The two agree on 100,000 random strings from a weighted alphabet, 57% of them errors. Another 100,000 strings of any
UTF-16, lone surrogates included, give tokens or a `CalcError` and nothing else, with spans in range. The table holds every number
form and every error, with spans.

**Mutations:** 20, all predicted red, all red. The oracle fuzz caught 16 of them alone. The battery is kept at
`tests/mutations/b2-lexer.py`, so it survives this chat.

**Checked against Natural first:** it accepts more than the brief allows: `1.`, `0x1.p3`, and `0x1.8` with no `p`, which it reads
as 1.5. The brief's rule stands, because `0x1.8e2` would otherwise be a hex fraction to someone who meant 180. Binary fractions
need their `p` too, for the same rule. Natural has no octal, and refuses underscores in exponents. B4's brief now says so.

**Choices made here:**
- π, τ and φ are names on their own, so `2πr` is 2·π·r and `rπ` is r·π. Other Greek letters are ordinary letters in names.
- `1.` is a number, as in Natural, so a keypad's `5.` evaluates.
- `0B` and `0O` are prefixes too. Otherwise `0B101` would lex as 0 times a name.
- `mod`, `rem`, `xor` and `to` are their own token kinds. `°` and `#` lex, for B3 to refuse by name. `<` and `>` alone are errors
  until H1.
- `ErrorKind` holds B4's eight kinds already.
- Only the brief's aliases: an en dash or `⋅` is refused, naming the character.

**Found on the way:** xUnit mangles a lone surrogate in `[InlineData]`, so those cases are facts. And I hit the Python-heredoc
backslash trap again, the one in the notebook since B1: for anything with a Windows path, use the Edit tool.

**Next:** B3, the parser. Its brief asks Matthew one question, about `1/2x`, `%` and `^`.

## 2026-11-26: B3 done

The parser: `Calc.Syntax/Ast.cs`, `Parser.cs` and `Printer.cs`. Tokens become a tree by Pratt parsing, and bad input gives
only a `CalcError` with a span. Matthew took all three recommendations on 2026-11-25: `1/2x` is (1/2)·x and `6/2(1+2)` is 9,
`%` is ÷100 and `7 % 3` is an error saying to use mod, and `^` is always a power. `Printer.Print` reads back to the same tree;
`Printer.Understood` is the on-screen form, such as `(1/2)·x`, and is null when nothing was implicit. 277 tests in all, 150 of
them the parser's.

**Tested:** a table of the brief's cases and every error with its span; every pair of binary operators, every prefix and
postfix with every binary, each worked out from a separate copy of the precedence table; 20,000 random trees built directly,
printed and parsed back, with every node's span checked to parse alone to the same subtree; a 100,000-string fuzz throwing
only `CalcError`; and the depth limit on a 256 MiB stack.

**Found on the way:**
- Two printer bugs, before they shipped. One I found reading it back: `(2x)(3)` printed as `2 x (3)`, which reads as 2 times
  the call `x(3)`. The random trees found the other: a right side starting with its own bracket, as in `e (…)^2`, made the name
  before it a call.
- **The stack.** The depth tests overflowed the 256 MiB stack and crashed the test host, and each crash popped a Windows
  dialog on Matthew's screen. Measured in process: 2,456 bytes a level of brackets in Debug, so 200,000 levels needed about
  490 MB. Errors are now built in helpers outside the recursion and tokens are read in place: 645 bytes in Debug, 824 for
  calls, at most 419 in Release, so 200,000 levels need at most 157 MiB. And `RuntimeHelpers.TryEnsureSufficientExecutionStack()`
  on each level turns a low stack into `TooBig`, so the parser can't overflow on any thread. B4's brief now says to do the
  same, because the evaluator walks these trees.
- Deep-stack tests run alone now, in a "Deep stacks" collection. Every GC scans every thread's stack, and two 200,000-frame
  tests side by side took 20 s each where alone they took 2. The Engine suite went from 1.4 minutes to about 20 s.

**Mutations:** 24, all predicted red. 23 were, first time. P11, which stops the depth counter coming back down after each
sub-expression, survived: every wide input in the tests was a flat sum, whose depth equals its length. `WideIsNotDeep`, a call
with 300,000 arguments, turned it red. The battery is `tests/mutations/b3-parser.py`.

**Choices made here:**
- Nodes are classes, not records, because generated Equals, GetHashCode and ToString would recurse. Each stores its depth.
- An implicit product is `Multiply` with `IsImplicit`. `Print` joins one with a space, which is always safe: `2` beside the
  name `e5x` would otherwise read back as `2e5`, `x`.
- A name before `(` is a call, spaces or not, so `π(2)` is a call that B5 will refuse. `to` parses, at the loosest rank, for
  B4 to call not available. `°`, `#` and `=` are refused by name, as not available yet.
- Too deep is `TooBig`. On screen, `Understood` brackets only a division, mod or rem on an implicit product's left.

**Next:** B4, the evaluator and float core. Its brief now carries B3's stack lessons.

## 2026-11-26, later: B4 done

The evaluator and float core: `Calc.Engine/Values/Target.cs` and `Value.cs`, and `Eval/IArithmetic.cs`, `FloatArithmetic.cs`,
`Evaluator.cs`, `Names.cs` and `ErrorMap.cs`. A tree evaluates at a target, bits or an IEEE format, in a rounding mode, each
operation correctly rounded through the evaluation's one `IeeeContext`, returning a value and the flags. + − × ÷ and negate
work; everything else says not available, by name and span, until B5. `calc-worker --eval` really evaluates now, with
`--bits`, `--format` and `--mode`, on a 256 MiB thread. B1's `Link` placeholder is gone. 371 tests in all.

**Done when, checked:** `calc-worker --eval "0.1+0.2" --bits 53` prints 0.30000000000000004.

**Tested against oracles:** 20,000 random trees at binary64, bit for bit against C#'s `double`, and 20,000 at binary32 against
`float`, with extremes for overflow and subnormals and literals in every form; 5,000 at random precisions from 2 to 300 bits
in all four modes against `RationalOracle`, a tests-only BigInteger fraction that rounds by its own code, the Inexact flag
included. All three agreed first time. Also tested: literals in every form, and two values that are rounded wrongly if a
literal is rounded twice (a decimal 10^-90 above a 53-bit midpoint, and a whole number just above one); flags at bits and
at a format; −0; each error kind with its span; left before right; and depth.

**Mutations:** 28, in the brief's categories. 27 red, as predicted. M9, which skips the DivideByZero flag check, is
equivalent, as predicted: at bits only a division can raise it, and division checks for a zero divisor first. The check
stays, for B5's functions. The battery stopped once, printing a `−` on Windows' old code page; its output is UTF-8 now.

**Stack:** the evaluator takes 403 bytes a level in Debug and at most 168 in Release, so 200,000 levels need at most 76 MiB.
It has the parser's stack guard, and a deep tree on a 1 MiB stack is a clean `TooBig`.

**Choices made here:**
- At bits, 0/0 is `DivideByZero`, as the brief says, and an infinite `Ans` minus itself is `Domain`. At a format, ∞ and NaN
  are values.
- `to` is refused before its operands are evaluated, since its right side names a format or unit, not a value.
- An unknown name is `Syntax`. Constants (π, τ, φ, e and their spelled names) and a missing `Ans` are `NotAvailable`. An
  `Ans` from another precision is `Internal`: rule 14's second wall.
- Negation is `ieee.Convert(-x)`, exact and through the context, which keeps −0. `0 − x` would lose it.
- `²` and `³` call `Power`, and `√` calls the `sqrt` function, so B5 fills them in where they belong.
- Until B6, a value prints as `"R"` up to 2,048 bits and `"E99"` past that.

**Next:** B5, functions and powers. Its brief carries B4's notes.
