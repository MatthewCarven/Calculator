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

## 2026-11-26, later still: B5 done

Functions and powers: `Calc.Engine/Eval/FunctionTable.cs` and `PowerFloat.cs`, and the rest of `FloatArithmetic`. The ten
functions work (sqrt, abs, sign, floor, ceil, trunc, round with ties to even, max, min, fma), and so do floored mod,
truncated rem, % as ÷100, and the bit operators on whole numbers. `^` is correctly rounded for every integer exponent, and
^0.5 is sqrt. The function table also lists the 25 functions waiting on Natural, each with its W session, so they're
known names that say not available. 445 tests in all.

**Tested against oracles:** sqrt, floor, ceil, trunc, round, abs, sign, max, min and fma bit for bit against `Math` at
binary64, specials included, and sqrt and fma against `MathF` at binary32; sqrt at random precisions in all four modes by an
exact check, squaring in fractions; mod, rem, %, the rounding functions and fma against `RationalOracle` at random
precisions and modes; 2,000 random integer powers, on both routes, negative bases and exponents, against the oracle,
Inexact included; T10's specials, every pair, against `Math.Pow`; bit operators against `BigInteger`.

**The brief was wrong once, and is corrected:** it said `3^(2^62)` overflows at bits. That's 2^(7.3×10¹⁸), and a long holds
exponents to 9.2×10¹⁸, so it's a value, ending E+2200333419485199624. It only gave 1 when the exponent wrapped.
`3^(2^63)` and `(2^(2^62))^2` are the overflows, and are tested.

**Found on the way:** one of my own tests was wrong. It sent `786` as a literal at 7 bits, where it rounds to 784, so the
calculator rightly worked out 438272^784 while the oracle used 786. Natural's directed squarings were checked step by
step against BigInteger before I found it, and were right throughout. CLAUDE.md notes that every literal rounds,
exponents too.

**Mutations:** 31. 29 red; two equivalent, as predicted: both bounds rounded the same way, which only a true value within
2^-64 of a rounding boundary would show, and Settle's extra Inexact, which converting differing ends raises anyway. On the
first run M2's edit didn't build, and the script is fixed; and M21, checking the route guard as a product, survived until
`1.1^(2^62)` at binary64 was added, where that product passes a ulong. The battery is `tests/mutations/b5-functions.py`.

**Choices made here:**
- floor, ceil, trunc and round use Natural's own exact versions, so floor(1.5) raises no Inexact: 1 is its exact answer.
- A zero from mod takes the divisor's sign, as Python's does. `-5 mod ∞` is ∞ at a format, by the same rule.
- Bit operators treat whole numbers as two's complement of unlimited width (~5 is -6), cap them at 2^24 bits as B7's
  integers will be, and refuse a fraction, infinity or NaN as `Domain`, at a format too, since IEEE has none.
- Shifts are floor(x·2^n), so `x << -1` is `x >> 1`, and they allocate nothing however far they go.
- A negative base to a non-integer is `Domain` at bits and NaN at a format, by T10's table, rather than not available.
- A base between ½ and 2 to an exponent past a long is not available, as the brief says, even 1.5, whose answer is
  plainly too large; W2's Pow will settle those.
- At a format, both bounds stop squaring once past the edge, each in the direction its products move. Otherwise the upper
  bound in `1e300^1e16` would square on until its exponent outgrew a long.

**Next:** B6, result text. Its brief now notes the far-out values B5 makes.

## 2026-11-26, last: B6 done

Result text: `Calc.Protocol/Digits.cs`, `Calc.Engine/Text/ResultText.cs` and `TextOptions.cs`. `ResultText.Of(value, target, options)`
gives `Short` (under 40 characters, for list rows), `Main` (for the result area), `Kind`, `Digits` and `Complete`. `Main`
shows every digit the precision guarantees, by G, up to 1,300 digits, so every preset through 4,096 bits is shown whole;
past that it's a 100-digit preview, rounded to 1,024 bits first, and `Complete` is false so the window can say "of 4,931
digits". At a format it's the shortest text that reads back. Five notations, optional grouping. 508 tests in all.

**For Matthew to eyeball, 1/3 at 256 bits:**

```
auto         0.3333333333333333333333333333333333333333333333333333333333333333333333333333
scientific   3.333333333333333333333333333333333333333333333333333333333333333333333333333E-001
engineering  333.3333333333333333333333333333333333333333333333333333333333333333333333333E-003
fixed        0.3333333333333333333333333333333333333333333333333333333333333333333333333333
aligned      3.333333333333333333333333333333333333333333333333333333333333333333333333333E-001
```

All 76 digits each time. Scientific and aligned only differ when there are trailing zeros: 1.5 at 53 bits is `1.5E+000`
scientific and `1.50000000000000E+000` aligned, the fixed width a right-aligned column needs. Grouped with a comma, 1234567.891
is `1,234,567.891`; the fraction is grouped only if asked, here with thin spaces: `0.333 333 333 333 333`.

**A visible change:** `calc-worker --eval` now prints `Main`, so `0.1+0.2` at 53 bits is **0.3**: 53 bits guarantee 15 digits,
and the 17th of 0.30000000000000004 is noise (rule 9). At binary64 it's still 0.30000000000000004, IEEE's shortest
round trip. `--raw` prints every digit that reads back, and `--notation` and `--group` pick the style. The text is rounded to
nearest whatever the rounding mode, which governs the arithmetic, not the display.

**Tested:** `Digits` for every preset and 2,000,000 precisions against the 128-bit constant, and `BitsFor` minimal for 200,000
digit counts; tables for each notation; grouping only ever adds separators, across all notations; engineering exponents are
multiples of 3; −0 folds at bits and stays at a format; text read back is within half a unit of its last digit, for Main at
random precisions to 4,100 bits, the preview at 16,384, and Short; a format's text reads back to the same bits, with no more
digits than .NET's shortest, binary64 subnormals included; timing at 2^20 and 2^22 bits; and the top of the exponent range is
an `Overflow` error, not an exception.

**Mutations:** 19, all predicted red. 18 were. M7, which skips rounding the preview to 1,024 bits, survived: measured again,
straight E99 at 2^20 bits is about 100 ms in Release, not the 330 ms in the research, and fits the brief's 200 ms budget. It
grows with the precision, so a test at 2^22 bits now turns it red. The battery is `tests/mutations/b6-text.py`, and runs only
the Text tests, since nothing else reads this code.

**Choices made here:**
- `FloatValue.ToString` stays the raw text, for tests, logs and `--raw`. People see `ResultText`.
- Short ignores the notation: G at up to 17 digits, fewer if a long exponent needs the room.
- Fixed pads a long whole part with zeros, as a spreadsheet does, and falls back to scientific past 1,400 places (rule 11).
- Exponents keep Natural's style, at least three digits (`E+005`), which keeps an aligned column even.

**Next:** B7, exact values and operators. Its brief and B8's carry notes from B5 and B6.

## 2026-11-27: B7 done

Exact values: `Calc.Engine/Values/IntValue.cs` and `RatioValue.cs`, `Eval/ExactArithmetic.cs`, and `Text/Lossless.cs`.
Integers and fractions stay exact through + − × ÷, negate, % and integer powers, until something irrational or too large
forces a float. `0.1+0.2` is 3/10, `1/3*3` is 1, `2^1000` and `10^6000/10` are exact integers, and `1^(10^30)` is exactly 1.
Exact is on by default in the worker, as it will be in the window; `--exact off` gives floats. 585 tests in all.

**Matthew's answer (2026-11-27): a float at once.** The work budget stays at 2^32, about 1 s per multiply, measured that
day: `3^100000` exact would take 2.36 s and is a float at once, chipped "float, exact too large"; `3^80000` stays exact, 1.5 s.
D3 raises the budget once stage D's multiply is faster.

**The budgets** are checked from bit lengths before anything is computed: integers to 2^24 bits, a fraction's parts to 2^14,
and 2^32 for one multiply, a power on its last squaring. Powers of two are exempt from the work budget, so `2^1000000` is
exact in under a millisecond. Past a budget it's a float, never a refusal, and `2^(10^12)` lights no Inexact lamp.

**Found on the way:** my first size estimate for a power, bitlen(m)·k, counted 3 as 2 bits instead of 1.585. It would have
sent `3^80000`, the case in Matthew's preview, to a float. It's k·log2(m) now, from m's top 64 bits. A budget-boundary test
caught it before anything shipped, and mutation M16 keeps it caught.

**Tested:** 5,000 random trees against an exact fraction oracle, every literal form, with lowest terms checked; each budget
from both sides, with small budgets so the costly side stays quick; the default budgets giving a float within a second;
Muller's sequence staying exact to n = 100 and heading for 5, where floats with Exact off reach the famous 100; exact operands
in floats; an exact exponent kept exact in the float path, so `(-3)^(2^60+1)` stays negative; and `Lossless` round trips
for every kind at eight targets, specials and −0 included, with malformed and crossed tags refused.

**Mutations:** 22, all predicted red. 21 were; M3, which checks the budget after computing, timed out instead, since it
really tries to build `7^(10^9)`, which counts as caught. The battery is `tests/mutations/b7-exact.py`.

**Choices made here:**
- Lossless floats write Natural's `A` text in upper case (`f:256:0X1.8P+0`), matching the integers' hex (`i:-FF`).
- A fraction's size is checked on its unreduced parts, before the GCD whose cost the limit is there to bound.
- mod, rem, the bit operators, comparisons and functions still go to floats, converting exact operands at the target; B8
  makes them exact. `ResultText` shows an exact integer whole up to 4,300 bits, and a fraction as its decimal; B8 adds
  "= n/d" and the chips.
- `Evaluator.Run` defaults to floats, so the float core's tests needn't say so; the worker passes exact.

**Next:** B8, exact functions and display. Its brief carries notes from B6 and B7.

## 2026-11-27, later: B8 done

Exact functions and display: `Calc.Engine/Eval/ExactFunctions.cs` and `RoundedCall.cs`, with the rest in `ExactArithmetic`
and `Text/ResultText.cs`. abs, sign, floor, ceil, trunc, round, max, min, mod, rem, fma, comparisons and the bit operators
stay exact on integers and fractions, and so does the sqrt of a perfect square: `sqrt(16/9)` is 4/3, `floor(-7/2)` is -4,
`(7/2) mod (1/3)` is 1/6, and `max(1/3, 0.2)` is exactly 1/3. 710 tests in all.

**Rounded once, for the exact value.** Where an exact value meets a float or outgrows a budget, the answer is now the exact
one rounded once. B7 rounded the exact value first, which the review measured as wrong in about 30% of cases like
`1/3*sqrt(2)`. Now `sqrt(1/7)` at 53 bits is 0.37796447300922725, where rounding 1/7 first gave ...2720, and `1.1^5000`
at 53 bits is 9.19233389907065E+206, where rounding 1.1 first gets the last 3 digits wrong. `(1/7)^(1/2)` is sqrt now.

**RoundedCall**, the helper the W sessions will use, brackets each exact argument from below and above at
W = p + 64 + max(0, top(x)) bits, doubles W until the ends agree, and stops at the brief's bound, taking the nearest. It's
tested by stand-in functions, since Natural has no ln yet: the widths it tries and its bound exactly, an end that divides by
zero (1/(1-x) at x = 1 - 10^-100, which rounds up to 1 at 320 bits), an end that gives infinity, and a failed end's flags
not kept.

**Found on the way:** brackets can't settle where the answer is itself exact, and for mixed products and remainders that's
common, not rare. 1/3 × 3.0 is exactly 1, 2.5 mod 2/3 is 1/2, and 2·10^20 mod 2/3 is 0; the random over-budget test hit
the last one. So × and ÷ with a float go by one exact division, and mod, rem and fma take the float as the fraction it is.
Over-budget work builds the fraction unreduced and divides once, while the work budget allows. RoundedCall gained an
`unsettled` hook for the rare case none of those cover, so it gives the exact answer instead of the nearest guess. And a
100-digit preview of 1/3^10000 took 130 ms, most of it spent writing the 4,772-digit denominator in decimal to see whether
"= n/d" would fit. Checking bit lengths first brought it to 30 ms.

**Display:** a fraction's digits are worked out from n/d exactly and rounded once, so the last one is right, and its chip
says "exact = 3/10" when the parts are short. `ResultText.Caption` is the new line under the result: "float, 76 digits",
"exact, 302 digits", "exact, about 30,103 digits", "exact = 1/3". `calc-worker --eval` takes `--caption`:

```
calc-worker --eval "0.1+0.2" --caption    0.3 / exact = 3/10
calc-worker --eval "2^100000" --caption   9.990020930143845...E+30102 / exact, about 30,103 digits
```

**Tested:** a table of exact results and errors; 5,000 random fractions through every rounding function, mod, rem, fma,
max, min and compare against BigInteger fractions, lowest terms checked; bit operators and shifts against BigInteger; sqrt
of exact values bracketed by squaring in all four modes at random precisions; mixed + − × ÷ against the exact answer
rounded once, with a count showing that rounding first would have differed in at least a tenth of the cases, so the test
can tell; mixed mod, rem and fma likewise; every over-budget operation under small budgets; exact powers past the budget;
a fraction's digits against BigInteger on both sides of the 1,300-digit limit; m/2^k's text in every notation against the
same float's, which checks the new G layout against Natural's own; and both previews timed.

**Mutations:** 38, all predicted red, and all 38 were, including the brief's five. The battery is
`tests/mutations/b8-exact-functions.py`.

**Choices made here:**
- `RoundedCall` is public, so its tests can drive it, rather than adding `InternalsVisibleTo`. It takes `absolute` (false
  for × ÷ √ ^, which care only about relative error, so a huge integer isn't passed whole), `extraBits` (bits(y) for
  powers) and `unsettled`.
- max and min return the winning argument as it is, and on a tie between an exact value and a float, the exact one.
- A fraction's chip shows n/d while it's at most 20 characters. A long fraction's caption is "exact, 76 digits shown",
  or just "exact" when its decimal ends within them.
- sqrt of a number whose square-root check would cost too much is chipped "float (exact too large)"; a number known not to
  be a square (an odd count of twos, or SqrtRem said so) gives a plain float.

**Next:** B9, the worker process. Its brief carries a note from B8 about `caption`.

## 2026-11-29: B9 done

The worker process: `Calc.Protocol/EvalContext.cs` and `Messages.cs`, `Calc.Worker/Loop.cs`, and `Calc.Engine/Eval/Contexts.cs`.
`calc-worker` with no arguments is now the loop the Client will talk to: one JSON request a line on stdin, one reply a line
on stdout, until stdin ends, which ends the process at once, even mid-evaluation, so a worker whose Client has gone dies
with it. `--eval` stays, for agents. 770 tests in all.

**How it runs.** The reader thread answers `ping` and `stop` itself, so both work during a long evaluation; everything else
runs in order on a BelowNormal compute thread with a 256 MiB stack, which parses there (rule 13). Stop is soft: the
evaluator checks a token on every node, so a sum of 30 big powers stops within one power, and a queued evaluation that's
stopped replies Stopped without starting. A malformed line, an evaluation error, or running out of memory under a heap cap
gets an error reply, and the loop goes on.

**The wire.** `EvalContext` (target, rounding, exact, angle, budgets) and every request and reply in the brief are records
in Calc.Protocol, tagged by `"type"`, with source-generated JSON, camelCase, nulls left out, and `·` and `π` written as
themselves. A result carries ResultText's fields, B8's caption included, the flags, the time, the understood form, and
the value's lossless text. Past 1 MiB of text the worker holds the value instead, under the reply's id. `Ans` and
`fetch` can name a held value, and the oldest go once they pass a quarter of the heap cap. A float `Ans` from another
target is refused (rule 14).

```
(printf '%s\n' '{"type":"eval","id":1,"source":"6/2(1+2)","context":{"target":{"precision":256}}}'; sleep 2) | calc-worker
{"type":"result","id":1,"short":"9","main":"9","kind":"exact","caption":"exact, 1 digit",...,"lossless":"i:9","understood":"(6/2)·(1 + 2)"}
```

**Found on the way:** with required constructor parameters switched on, System.Text.Json requires the nullable ones too,
while the writer leaves nulls out, so a reply without a held id didn't read back. Every reply failed until the nullable
fields got defaults; an error's id, which can be null, is always written. And piping requests in with printf closes stdin
at once, so the worker, rightly, ended before answering; holding stdin open fixed the probe, not the worker.

**Tested, on the real exe:** a round trip; ids echoed in order; 200 evaluations in a row; nine kinds of malformed line,
each answered with its id where it had one; the soft stop, running and queued; ping answered during a long evaluation;
stdin closing ends it within a second, idle and busy; an 830,003-character reply intact; `1/3` then `Ans*3` exactly 1, and
`2^1000000` then `Ans+1` exact; float `Ans` refused at 64 bits and at binary64; `2^5000000` held, named by `Ans`, fetched;
`ValueGone` for a value it never held; eviction under a 128 MiB cap; `TooBig` under a 64 MiB cap; a 199,999-term sum
answered and a 1,000,000-term sum a clean `TooBig`; the context, rounding, Exact, budgets and text style honoured; the
function table; text outside ASCII both ways. Every message to its line and back, the context mapping, and the evaluator's
soft stop on its own.

**Mutations:** 25, all predicted red, and all 25 were. None crashes the worker, which could pop a dialog on Matthew's screen.
The battery is `tests/mutations/b9-worker.py`.

**Choices made here:**
- `stop` names the evaluation to stop and has no reply of its own; the evaluation replies `error` kind `Stopped`.
- Error kinds are `ErrorKind`'s names plus `BadRequest` and `ValueGone`.
- A held value's id is the id of the reply that made it.
- `EvalContext.Angle` defaults to degrees, as the window does; nothing uses it until W3.

**Next:** B10, the worker client, with Stop. Its brief carries notes from B9.

## 2026-11-29, later: B10 done

The worker client: `Calc.Client/IWorker.cs`, `WorkerClient.cs` and `CommitInfo.cs`, with `tests/Calc.Client.Tests/FakeWorker.cs`.
`WorkerClient` owns calc-worker's life, so nothing above it thinks about processes. It starts a worker with the first
request, gives each request the next id and a task, reads replies on a dedicated thread so a big one can't fill the pipe,
and drops any reply nothing waits for. Every task completes with a reply; none throws for a stop, a death or short memory.
810 tests in all.

**Stop.** `StopAsync` sends the soft stop for whatever is outstanding, waits 150 ms, and if that hasn't ended it, kills
the worker's process tree: everything completes as Stopped, and the next request starts a new worker. Measured on the
real exe in Debug: inside a 2,000,000-bit multiply, which no soft stop can reach, Stop to Stopped is about 170 ms, and a
new worker answers about 170 ms later, close to the spike's 153 and 250. A sum of 200 smaller powers stops softly well
inside the 150 ms, and the same warm worker carries on. A worker killed from outside completes what it had as
WorkerDied, with its exit code, and is replaced. Dispose kills the worker and waits until it has gone.

**The heap cap** is `DOTNET_GCHeapHardLimit`, written in hex: the 4 GiB setting, or free commit less 1 GiB if that's
smaller, read from `GlobalMemoryStatusEx` and worked in Int128, so a shortfall is negative instead of wrapping. Below
256 MiB no worker starts, and the request says memory is short. The worker's `pong` now carries the memory it may use,
which is how a test sees the cap: 512 MiB set, 512 MiB reported.

**Tested:** a contract every `IWorker` keeps, run against the real client and the fake alike, so B11 can trust the fake;
on the real exe, Stop inside the multiply, the soft stop with no kill, a worker killed from outside, the cap as the child
reports it, free commit at 0.5, 1.0 and 1.1 GiB (no worker) and at 1.25, 3 and 5 GiB (capped at 256 MiB, 2 GiB, 4 GiB),
out of memory under the floor recovered in the same worker, 20 stop-and-go cycles leaving no process, dispose leaving no
process, big replies whole, and a missing exe. A batch file stands in for a worker to send a line that isn't JSON, a reply
nobody asked for and a progress line before the real result: only the result completes the task. Every wait in these
tests is capped.

**Found on the way:** a test that stopped 150 ms after starting a new worker often caught it before the multiply began,
so the soft stop won and nothing was killed; the expression now reaches its multiply within milliseconds, and each cycle
warms its worker first. And the heredoc backslash trap struck twice more, once leaving a patch half applied, so the
battery's reruns tested the old files until I noticed; the Edit tool did it.

**Mutations:** 20. 17 red, M2 after fixing its own edit, which hadn't built. Three survive, explained in
`tests/mutations/b10-client.py`: the reader clears a killed worker within milliseconds anyway (M5); .NET already
autoflushes the worker's stdin (M11); a killed worker has exited before a test can look (M15).

**Choices made here:**
- `IWorker` has `EvaluateAsync`, `FetchAsync`, `FunctionsAsync`, `PingAsync` and `StopAsync`; Stop stops everything
  outstanding.
- The client's own error kinds are `Stopped`, `WorkerDied` and `MemoryShort`.
- A heap cap setting below the 256 MiB floor starts no worker.

**Next:** B11, the session, the clear rule and the console calculator. Its brief carries a note from B10.
