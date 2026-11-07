# TODO: the build plan

[PLAN.md](PLAN.md) says what and why. This file says what next. Run each session in a fresh chat at "extra" effort with
ultracode off: one agent, no workflows, no subagents. Say which one: "start B4 please Claude". Two small ones in one chat is fine.

**Where each runs.** Natural's sessions (A, D1, D2, T, I18 to I22) run in a chat opened in the Natural folder, so its own
CLAUDE.md, TODO and memory load. Everything else runs in a chat opened in Calc. No session runs half in each.

**How ready the briefs are.** Stages A to F are written to be started cold. G, H and I are shorter, because the code will
have moved on by then: the session before writes the next brief out in full (step 8 below).

## How a session runs

1. **Start cold.** Read this brief, the latest WORKLOG entry and the code it touches. `git status`. Before a Natural
   session, also `git fetch` and `git branch -a`, because other agents have worked branches there.
2. **Check the brief against the code.** A brief is a hypothesis. If the code disagrees, the code wins: fix the brief and say so.
3. **Ask once, if you must.** Anything Matthew will see or feel gets one short question with a concrete preview, the
   recommendation first, and measured numbers where they help. Decide internals yourself and say what you chose. Write
   the answer here at once.
4. **Build, and test against an oracle.** Break the code on purpose once, and watch the test go red, before trusting it.
5. **Maths sessions** run a mutation battery, using the recipe in `../Natural/CLAUDE.md`, about one mutation per 15
   production lines. List them before running.
6. **Window sessions** run `--smoke`, take dark and light `--screenshot`s, look at them, and end with a three-line "try
   this" list for Matthew. If one is still untried at the next session, raise it first, and keep raising it.
7. **Finish.** Tick the order of work, add a WORKLOG entry, fix any README or CLAUDE.md drift, note anything surprising in
   the notebook, commit, push. Natural sessions tick Natural's TODO and never edit Calc. The next Calc session checks Natural's
   TODO, then ticks any finished Natural sessions here.
8. **Hand over.** If the next session's brief is under 100 words, write it out in stage B's format (Goal, Needs, Files, How,
   Traps, Tests, Mutations, Done when) against the code as it now stands, and commit it. After a Natural session, the next
   Calc session writes its own brief as its first step, and a following Natural session's brief goes in Natural's TODO. If
   stages were swapped and a session starts on a short brief, writing it out is that session's first step.
9. **If it runs long,** stop at the last green commit, split the brief here, and hand over. Don't brute-force it.

## Rules every session keeps

These cost real time to find. The evidence is in `docs/research/natural-api/notes.txt` and `docs/research/reports/`.

1. **Natural lives only in `Calc.Engine` and `Calc.Worker`.** `Calc.Syntax`, `Calc.Protocol`, `Calc.Client`, `Calc.Cli` and
   `Calc.App` never reference it. `LayeringTests` proves it.
2. **Natural's public API only.** No `InternalsVisibleTo`. The recipes in `docs/research/natural-api/OnTop.cs` cover the gaps.
3. **No `BigInteger`, `double` or `decimal` for values in `src/`.** Tests may use them as oracles. The one exception is the
   plot's pan, in stage I.
4. **Never `+ - * /` on `ApFloat` in `src/`.** The operators widen to the larger operand's precision, so they hide
   contamination. Every float operation goes through the evaluation's one `IeeeContext`, whose target overrides its operands.
5. **Never a plain cast on `ApInt`, and `checked` on every sum of sizes or exponents.** `(int)(ApInt)5000000000` wraps to
   705032704, and `(long)` throws. Use `int.CreateChecked` and `long.CreateChecked`. C# longs wrap silently, and a wrapped
   exponent is still a valid exponent, so Natural can't catch it: `(2^(2^62))^2` came out as a tiny number.
6. **Never raw `ApInt.ToString("X")` or `"B"` for display.** They're two's complement: 255 is `0FF`, -255 is `F01`. Use
   `ApInt.Abs(x).ToString("X").TrimStart('0')`, handling 0 and the sign yourself.
7. **Never `PopCount`, `RotateLeft`, `RotateRight` or `LeadingZeroCount` for word-size work.** They copy BigInteger's 32-bit
   quirks: `PopCount(-1)` is 32, `RotateLeft(1, 40)` is 256. Hold masked non-negatives and use the recipes.
8. **Invariant culture in the engine.** de-DE `Parse("1.5")` throws. Culture touches display only, if at all.
9. **Digits shown: `Digits.For(p)`,** which is max(1, floor((p−1)·log10 2)): 76 at 256 bits. Not `DecimalDigitsFor`, whose 79
   shows rounding noise. It lives in `Calc.Protocol` (B6), so the Client needs no Natural.
10. **`"R"` only up to 2,048 bits.** It costs 32 s at 65,536 bits, where `G19728` costs under 1 s.
11. **Cap any user's digit count before it reaches a format string.** `"F999999999"` hangs, and `"E1234567890"` prints `E1234567892`.
12. **Natural's exceptions are mapped in one place,** `ErrorMap`, using the table in report 1.
13. **The parser and everything that walks a tree run only on the worker's compute thread,** which has a 256 MiB stack. The
    Client uses the Lexer, never the Parser. A flat `1+1+1+…` is a deep tree: at about 3,500 terms a default stack dies, and
    that can't be caught.
14. **A value can't leave its target.** A float `Ans` is sent only with a matching target, and the worker refuses a mismatch
    too. Anything that runs one expression at several targets replays the line, never reuses a number.
15. **Bench with fresh operands from a pool.** Repeating one operand flatters small sizes threefold. Compare A B A B: this
    machine has two speeds.
16. **Cap every `while` loop in a test.** A hang should fail, not stall the suite.
17. **Don't build Calc while a Natural build is running.** Two builds of one project collide.

## Order of work

**Done 2026-11-07:** `git init`, and all six questions in PLAN.md's Decisions section, answered as recommended. **Still Matthew's:** Calc's
GitHub remote. Until `origin` exists, Calc sessions commit and skip the push.

**A. Ground (Natural)**
- [ ] A1 Finish session 9b: its battery, its docs, commit, push
- [ ] A2 Three bugs a calculator can reach

**B. Engine**
- [ ] B1 Solution skeleton
- [ ] B2 Lexer
- [ ] B3 Parser
- [ ] B4 Evaluator and float core
- [ ] B5 Functions and powers
- [ ] B6 Result text
- [ ] B7 Exact values and operators
- [ ] B8 Exact functions and display
- [ ] B9 Worker process
- [ ] B10 Worker client, with Stop
- [ ] B11 Session, the clear rule, console calculator
- [ ] B12 Cost model and preflight

**C. Window: the first release**
- [ ] C1 WPF shell and themes
- [ ] C2 Entry and result
- [ ] C3 Standard keypad
- [ ] C4 Precision selector and clear
- [ ] C5 Busy, progress, Stop
- [ ] C6 History panel
- [ ] C7 Full digits from the worker
- [ ] C8 Digit viewer
- [ ] C9 Settings, publish, v0.1

**D. Speed**
- [ ] D1 Adder and short division (Natural)
- [ ] D2 Karatsuba and squaring (Natural)
- [ ] D3 Calc: raise the ceiling

**E. Science: Natural's T sessions, each group then wired in**
- [ ] T1 Ball · T2 Ziv's loop · T3 Splitting and π · T4 e, ln 2, ln 10
- [ ] W0 Function table, constants, Scientific tab
- [ ] T5 Exp · T6 Log
- [ ] W1 exp and ln
- [ ] T7 Exp family · T8 Log family · T9 Cube root · T9b RootN and Hypot · T10 Pow
- [ ] W2 Logs, powers and roots
- [ ] T11 Sin, Cos, Tan · T12 Angle units
- [ ] W3 Trig and Angle
- [ ] W4 Huge-argument warnings
- [ ] T13 Atan, Atan2 · T14 Asin, Acos · T15 Hyperbolics
- [ ] W5 Inverse trig and hyperbolics

**F. Programmer**
- [ ] F1 Radix text
- [ ] F2 Word-size arithmetic
- [ ] F3 Programmer tab
- [ ] F4 Integer functions
- [ ] F5 Primes and factoring

**G. Truth tools**
- [ ] G1 Format mode, engine
- [ ] G2 Format mode, window and flag lamps
- [ ] G3 Bit inspector
- [ ] G4 Intervals, engine
- [ ] G5 Proven digits in the window
- [ ] G6 Intervals through monotone functions (needs E)
- [ ] G7 Intervals through the rest (needs E)
- [ ] G8 N proven digits on demand
- [ ] G9 Precision sweep
- [ ] G10 Digit-loss x-ray
- [ ] G11 Format lab
- [ ] G12 Bit-exact receipts (comes after H6)

**H. Comfort**
- [ ] H1 Comparisons and `if`
- [ ] H2 Variables and functions
- [ ] H3 Variables panel
- [ ] H4 Memory keys
- [ ] H5 Autocomplete and shortcuts
- [ ] H6 Saved sessions and export
- [ ] H7 Display settings
- [ ] H8 Live preview
- [ ] H9 RPN mode
- [ ] H10 Unit values
- [ ] H11 Unit table

**I. Depth (optional)**
- [ ] I1 Sums, products, sequences
- [ ] I2 Statistics
- [ ] I3 Complex arithmetic
- [ ] I4 Complex functions (needs E)
- [ ] I5 Matrix values
- [ ] I6 Linear algebra
- [ ] I7 Matrix entry
- [ ] I8 Solver
- [ ] I9 Numeric integration (needs E)
- [ ] I10 Plot sampling (needs E)
- [ ] I11 Plot canvas
- [ ] I12 Deep-zoom plot
- [ ] I13 Gamma family (needs E)
- [ ] I14 Error functions (needs E)
- [ ] I15 Identify a fraction, uncertainty input
- [ ] I16 Line references
- [ ] I17 Live recalculation
- [ ] I18 Newton division (Natural)
- [ ] I19 Newton square root (Natural)
- [ ] I20 Divide-and-conquer radix text (Natural)
- [ ] I21 Bit-burst, T21 (Natural)
- [ ] I22 Streaming digits, T22 (Natural)

---

# Briefs

## A. Ground

### A1. Finish session 9b (Natural)
**Goal:** Natural clean, pushed and true to its docs, so Calc builds on something you can name.
**State on 2026-11-07:** master is 3 commits ahead of origin (9a and two TODO notes). 9b's code is finished but uncommitted,
and all 682 tests pass. Its mutation battery is not finished. That chat stopped on 14 October with batch 1 done (14 of 15 red,
and S13 a real hole since closed in `StyledTextFuzz.cs`) and 44 mutations on hold, waiting for a match to end. TODO line 523
says the mutations are in the WORKLOG, but the WORKLOG stops at 9a. Nothing in the repo records this.
**Do:**
1. `git fetch`, `git status -sb`, `git branch -a`. Run the suite.
2. Commit 9b locally, without pushing, so `git status` is clean and the battery has something to diff against.
   `StyledNumber.cs` and `ApFloat.GenericMath.cs` are untracked, so until then a killed run could lose them.
3. `../Calc/docs/research/natural-api/mutate-9b.py` is that battery, recovered: 59 mutations, about 22 minutes of builds for the 45
   still to run. Its old strings all still match, and S13 goes red in 20 s. Copy it to this chat's scratchpad. Rerun S13, then the
   other 44, in batches under ten minutes. Check the machine isn't busy first. Close any real hole in a second commit.
4. Write 9b's WORKLOG entry, and add its notebook and memory notes. The 9b chat's transcript is the only record of batch 1:
   `~/.claude/projects/C--Users-Admin-Desktop---Programming---Natural/36ea4d6b-3562-4b2d-9ac5-e7068000cee2.jsonl`.
5. Fix the drift in report 1 section 4: README's 666 (lines 325, 348) is 682; README 253's `-FF` is `F01`; `ApFloat.cs` 449 says "Exact"
   but it rounds past 2^30 bits; TODO 120 and CLAUDE.md 94 say .NET rounds ties away, but `double` on 10.0.13 gives 0.12 and 2, and only
   `decimal` goes away. Leave README 228 and 295 for A2.
6. Commit, push.
7. Copy `../Calc/docs/research/transcendentals/` into Natural as `docs/transcendentals/`, which is the live copy from then on. Add A2, D1, D2,
   T1 to T22 and I18 to I20 to Natural's order of work, so its own TODO stays the map for its own sessions. Record Matthew's two answers of
   2026-11-07 there: the transcendentals live in Natural, correctly rounded; and the parallel-prefix adder is within the bitwise rule, which
   answers session 10's "Ask Matthew first". How far to take speed for now is D1 and D2.
**Done when:** `git status` is clean, origin matches, `dotnet test Natural.slnx` is green, and Natural's TODO lists the sessions.

### A2. Three bugs a calculator can reach (Natural)
**Goal:** the three defects in report 1 fixed, each with a test that fails first.
**Needs:** A1.
1. **`Top` isn't checked: a silent wrong answer.** `_exp + BitLength - 1` runs unchecked, so a value whose top bit lies past 2^63 wraps
   negative, and everything reading `Top` thinks it's tiny. `ApFloat.Parse("1e2776511644261678567")` succeeds, `x > 1` is false, and `(double)x`
   is 0. Check `Top` where a value is made, and throw `OverflowException`, which is what `Parse` already promises. Two more copies of the sum in
   `ApFloat.Ieee.cs` (lines 178 and 236, in `RoundToFormat` and `RangeOf`) need Int128 or saturating arithmetic:
   `Multiply(3·2^(2^62), 3·2^(2^62−1), Binary64)` returns 0 with Underflow, where ∞ with Overflow is right. Doubling up to the edge reaches the
   same wrap: see `notes.txt`, "The Top wrap".
2. **`ToRadixDigits`'s `int count`.** `"B"` of a 4,294,967,329-digit value returns 33 zeros. Throw `OverflowException` when the digits don't fit a
   string. Only ApFloat's `"B"` reaches it.
3. **`Ieee754Remainder` lacks `%`'s early-out.** `Ieee754Remainder(3, 2^(10^12))` throws `OverflowException`; it should be 3, as README 228 claims.
   When top(x) < top(y) − 1 the answer is x.
**The print edge:** `1e2776511644261678566` is the last with a true `Top`; `…565` is the last that prints. Values with Top ≥ 2^63−2 parse but throw
`OverflowException` in most formats (`ApFloat.Text.cs` line 472); only `A` prints them. Recommended: leave that, pin it with a test, and let Calc's
ErrorMap show Overflow.
**Tests:** values either side of each edge, every mode, several precisions; `Add`, `Multiply`, `Parse`; that format product; the remainder against
exact rationals.
**Mutations:** the check off by one; the check only in `Parse`; the Ieee sums unchecked again; the early-out's −1 dropped; early-out returning y.
**Done when:** all three are red before and green after, and README 228 and 295 are true.

## B. Engine

### B1. Solution skeleton
**Goal:** the solution builds, and one number travels from Natural to the console.
**Needs:** A1.
**Files:** `Calc.slnx`, `Directory.Build.props`, `.gitignore`, `src/Calc.Syntax`, `Calc.Protocol`, `Calc.Engine`, `Calc.Worker`, `Calc.Client`, `Calc.Cli`,
`tests/Calc.Engine.Tests`, `Calc.Worker.Tests`, `Calc.Client.Tests`. `Calc.App` arrives in C1.
**How:** start from `docs/research/ui/skeleton/`, which built. Change it: `NaturalRoot` defaults to `$(MSBuildThisFileDirectory)..\Natural\`; Engine
references `$(NaturalRoot)src\Natural\Natural.csproj`; `Calc.ViewModels` becomes `Calc.Client`; add `Calc.Syntax`, with no references, used by Engine and
Client; add `Calc.Cli`. `Calc.Cli` and `Calc.Client.Tests` both need the worker beside them, so give each the skeleton App's `ReferenceOutputAssembly="false"`
reference and copy step. `calc-worker --eval "x"` prints `ApFloat.Divide(1, 3, 256).ToString("G76")`, hard-coded, to prove the link.
**Traps:** reference only `Natural.csproj`, never `Natural.slnx`, which holds WinForms.
**Tests:** one real test per project. `LayeringTests`, in `Calc.Client.Tests`, reads each assembly's references with `System.Reflection.Metadata`, and fails
if `Natural` appears in Syntax, Protocol, Client or Cli.
**Done when:** `dotnet build Calc.slnx` and `dotnet test Calc.slnx` are green from a fresh clone beside Natural, and `calc-worker --eval x` prints `0.33333…` to 76 digits.

### B2. Lexer
**Goal:** text to tokens, with spans, and nothing but a `CalcError` for bad input.
**Needs:** B1.
**Files:** `Calc.Syntax/Token.cs`, `Lexer.cs`, `CalcError.cs`, tests in `tests/Calc.Engine.Tests/Syntax`.
**Tokens:** numbers, kept as raw text; identifiers; operators; `(`, `)`, `,`, `=`, end.
**Number forms:** `12`, `1.5`, `.5`, `1e5`, `1E-5`, `1_000`, `0xFF`, `0b101`, `0o17`, `0x1.8p+1`, `0b101.01p-3`, plus `0X` and `P`, because Natural's `A` format writes
them and copied text must paste back. `1e` is 1 then identifier `e`, and `1e+` is 1, `e`, `+`. Underscores only between digits. A comma never groups
digits, because `max(1,000)` has to mean two arguments.
**Aliases:** `×` and `·` for `*`, `÷` for `/`, `−` (U+2212) for `-`, `**` for `^`. `π`, `τ`, `φ` are identifiers. `√` is a prefix operator. `²` and `³` are postfix.
Reserve `°` and `#`.
**Traps:** `0x1.8` with no `p` is an error, not hex 1 then `.8`. Hex digits include `e`, so `0x1e5` is 485.
**Tests:** a table of each form and each error, with spans. A fuzz of 100,000 random strings, of capped length: only `CalcError` may escape, and spans stay in range.
**Done when:** the table and fuzz pass. The Lexer does no recursion, so the Client can use it on any thread.

### B3. Parser
**Goal:** tokens to a tree, by Pratt parsing, with a printer that round-trips.
**Needs:** B2.
**Files:** `Calc.Syntax/Ast.cs`, `Parser.cs`, `Printer.cs`, tests.
**Precedence, loosest first:** `to` (reserved, for F1 and H10); `|`; `xor`; `&`; `<<` `>>`; `+` `-`; `*` `/` `mod` `rem` and implicit multiplication; unary `-` `+` `~` `√`; `^`, right associative;
postfix `!` `%` `²` `³`. Leave a gap between `&` and `<<` for comparisons (H1).
**So:** `-2^2` is -4. `2^3^2` is 512. `2^-3` is 1/8. `2^3!` is 64. `-3!` is -6. `√2^2` is 2.
**Identifiers and calls:** `name`, `name(a, b)`. `2π`, `2(3)`, `(2)(3)`, `2x` and `2√3` are implicit multiplication, marked in the tree. **A number token never starts an implicit
product:** `1 000 000` and `1.2.3` are syntax errors. `%` directly followed by anything that could start an operand is one too, with the hint "use mod", so `7 % 3` and `7 % x` can't silently mean 0.21 and 0.07x.
**Depth:** one counter over every recursion, covering parens, unary chains and `^` chains, plus the length of a left spine. Refuse past 200,000 with a `CalcError` and a span. Tests run the parser on a
thread with a 256 MiB stack (rule 13).
**Ask once, with a preview of these three:** `1/2x` (recommended: `(1/2)·x`, same rank as `*`, left to right, with the understood form shown on screen); `%` (recommended: postfix ÷100, so `50%` is
0.5; `mod` is floored, `rem` truncates; and `200 + 10%` is 200.1, not the pocket-calculator 220); `^` never means xor.
**Tests:** a table with the cases above and every pair of operators; a generator of random trees where `parse(print(t)) == t`; errors with spans for `1+`, `(1`, `1)`, `f(`, `,`.
**Done when:** those pass, a 100,000-string fuzz throws only `CalcError`, and on the big stack 199,999 terms of `1+1+…` parse, and 1,000,000 give a `CalcError` with a span.

### B4. Evaluator and float core
**Goal:** evaluate a tree at a target and mode, each operation correctly rounded, returning a value and flags.
**Needs:** B3.
**Files:** `Calc.Engine/Values/Value.cs`, `Values/Target.cs`, `Eval/IArithmetic.cs`, `Eval/FloatArithmetic.cs`, `Eval/Evaluator.cs`, `Eval/Names.cs`, `Eval/ErrorMap.cs`, tests.
**Shape:** `Target` is bits or an `IeeeFormat`. `Value` is an abstract record, starting with `FloatValue(ApFloat)`, so later kinds slot in. `IArithmetic` holds literal, add, subtract, multiply, divide, negate, power, mod, rem, percent,
factorial, the bit operators, compare and call. `Evaluator` walks the tree and knows no number types. `Names` resolves bare names: constants, `Ans`, and later definitions. An unknown name is a `CalcError`.
**FloatArithmetic:** one `new IeeeContext(target, mode)` per evaluation (rule 4). Decimal and hex-float literals go through `ieee.Parse`, invariant culture, with `_` stripped first, because it refuses them in an exponent; integer forms through `ApInt.Parse` then `ieee.Convert`; `0o` by Horner on `ApInt`.
+ − × ÷ and negate here. The rest report `NotAvailable` until B5, and `!` until F4.
**Policy follows the target.** At bits, `Mathematical`: the DivideByZero and Invalid flags become `CalcError`s with the operator's span, read from the flag change after each operation. Division or mod by zero is always `DivideByZero`, even `0/0`. At a format,
`Ieee`: ∞ and NaN stand as values.
**ErrorMap:** report 1's exception table to `Syntax`, `Domain`, `DivideByZero`, `Overflow`, `TooBig`, `NotAvailable`, `Stopped`, `Internal`.
**Also:** `calc-worker --eval "1+1" [--bits 256] [--mode nearest]` now really evaluates, for agents and humans.
**Traps:** `0 * -1` is -0, which is legal; display folds it in B6. At bits, exponent overflow throws: `Overflow`. At a format it's ∞ with the Overflow flag, and only operands far outside the format would throw. Trees are deep, so walk them
on a big stack (rule 13), and test on one.
**Tests:** random expression trees at binary64, bit for bit against C# `double` (nearest mode, all NaNs equal), and at binary32 against `float`; random precisions in all four modes against `RationalOracle`, a tests-only BigInteger rational that rounds
independently. `0.1+0.2` at 53 bits. One test per error kind. Keep the tree generator: later backends reuse it.
**Mutations, about 25:** an operator used instead of the context; a mode ignored; evaluation order; a flag not checked; the wrong error kind; a literal parsed then converted.
**Done when:** the oracles agree, the battery is red, and `calc-worker --eval "0.1+0.2" --bits 53` prints 0.30000000000000004.

### B5. Functions and powers
**Goal:** the function table, the ten plain functions, and correctly rounded integer powers.
**Needs:** B4.
**Files:** `Eval/FunctionTable.cs`, `Eval/PowerFloat.cs`, tests.
**FunctionTable:** name to arity and implementation, plus a `NotAvailable` state for functions waiting on Natural. Functions now: `sqrt`, `abs`, `sign`, `floor`, `ceil`, `trunc`, `round` (ties even), `max`, `min`, `fma`. `mod` is floored, `rem` is `ieee.Remainder`, `%` is ÷100.
**Bit operators:** on an integer-valued float, by exact conversion with a size check. A non-integer is `Domain`.
**Integer powers:** specials first, by the IEEE table in T10's brief, and base 0 with negative `y` is `DivideByZero`. Then, for `y` that fits a long:
- *Exact route,* when `|y| ≤ min(4(p + 64), 2^30) / bitlen(m)`, written as a division so it can't wrap: `ieee.Convert(new ApFloat(ApInt.Pow(m, int.CreateChecked(n)), checked(e * n), ApFloat.MaxPrecision))` rounds once, into a format too, and raises flags. Negative `y`: `ieee.Divide(1, thatExactValue)`. The public constructor alone is not enough: it has no flags and ignores a format's range.
- *Bounds route,* otherwise: square-and-multiply twice, in `TowardNegative` and `TowardPositive`, at W = p + 64 + 2·bitlen(y), taking both ends through `ieee.Convert` and doubling W until they agree. Negative base: work on the magnitude and restore the sign. Negative `y`: invert the bounds. At a format, leave the loop once the low bound's top passes emax + 1, or the high bound's top falls 2 below the smallest subnormal, and take ∞ or 0 through `ieee.Convert`: hardware gives a value there, where the W-bit squarings would run out of exponent.
- `y` past a long: |x| ≥ 2 or |x| ≤ 1/2 is `Overflow` at bits, and ∞ or 0 at a format. Otherwise `NotAvailable` until W2.
- Non-integer `y`: `NotAvailable`, except `y = 1/2`, which is `sqrt`. T10 replaces all this (W2).
**Tests:** every function against `Math` at binary64 where correctly rounded (`sqrt`, `floor`, the rounds, `fma`), and the rational oracle elsewhere; powers on both routes in all modes, negative base and exponent; `3^1000` at binary64 is ∞ with Overflow; `3^200` at 256 bits raises Inexact; `3^(2^62)` and `(2^(2^62))^2` are `Overflow`; `1e300^1e16` at binary64 is ∞.
**Mutations:** floored `mod` as truncated; `fma` as two operations; the bounds swapped; sign restored wrongly; parity; the route guard; `checked` dropped; a flag dropped.
**Done when:** those pass and the battery is red.

### B6. Result text
**Goal:** a value to the text people see, cheaply, at any size.
**Needs:** B5.
**Files:** `Calc.Protocol/Digits.cs`, `Calc.Engine/Text/ResultText.cs`, `Text/TextOptions.cs`, tests.
**Digits:** `Digits.For(p)` is `Math.Max(1, (int)Math.BigMul((ulong)(p - 1), 0x4D104D427DE7FBCCUL, out _))`, the high 64 bits of (p−1)·floor(2^64·log10 2). It's exact for every p to 2^30; shorter constants fail on about 6% of them. Also `Digits.BitsFor(d)`: ceil(d·log2 10) + 1, by the same method, checked so `For(BitsFor(d)) ≥ d`, and for d ≥ 2 that `BitsFor(d)` is the smallest such.
**Output:** `Short`, under 40 characters, for list rows. `Main`, for the result area. `Kind` and `Digits`. Anything longer waits for C7.
**Rules:** `Main` is `G<d>` by rule 9 while d is at most 1,300, so every preset through 4,096 bits shows all its digits. Past that, and always for `Short`, go through `x.WithPrecision(1024).ToString("E99")` or shorter, which is about 3 ms; straight `E99` costs about 35 ms per Mbit, and 330 ms on a full significand. Report "of 4,931 digits". Fold -0 to 0 at bits; keep it at a format. ∞ and NaN print as words at a format.
**Format targets:** `"R"`, within rule 10. It isn't shortest for subnormals: `double.Epsilon` prints `4.9406564584124654E-324`, so for those try `G1` upward until `ieee.Parse` gives the same bits. Tests compare bits, not text.
**Notation options:** auto, scientific, engineering, fixed, aligned. Engineering isn't in ApFloat: shift the point in `E` text, by string work only. Grouping: insert separators into the digit string, by string work, never through custom patterns. Aligned is `E<d-1>`, with d from rule 9: always scientific, fixed width and no noise, so a right-aligned column reads like a bar graph, the way Matthew likes them.
**Traps:** rule 11 for fixed notation: `"F100000"` of 1.5 takes 7.9 s, and `F0` of 1e100000 about 2 s. `ToString` can throw `OverflowException` at the very top of the exponent range: through ErrorMap, to `Overflow`.
**Tests:** tables per notation; `Digits` for every preset and 2,000,000 random p against the 128-bit fraction `0x4D104D427DE7FBCC_47C4ACD605BE48BC`; preview digits parse back within half a unit of the last digit; removing separators gives the ungrouped text; engineering exponents divisible by 3; -0; `Short` and `Main` under 200 ms on random full significands at 2^20 bits.
**Done when:** those pass. Print 1/3 at 256 bits in each notation in the WORKLOG for Matthew to eyeball.

### B7. Exact values and operators
**Goal:** integers and fractions stay exact until something irrational or too large forces a float.
**Needs:** B6.
**Files:** `Values/IntValue.cs`, `Values/RatioValue.cs`, `Eval/ExactArithmetic.cs`, `Text/Lossless.cs`, tests.
**Values:** `IntValue(ApInt)`; `RatioValue(n, d)` with d > 1, lowest terms, sign on n. A ratio that normalises to d = 1 becomes an `IntValue`.
**Lossless:** one tagged text form for every kind, used on the wire and in saved sessions: `i:<sign><hex>`, `r:<sign><hex>/<hex>`, `f:<target>:<A text>`, hex by rule 6. `A` text round-tripped 640 of 640 values, specials too. A round-trip test covers every kind.
**Literals:** a decimal becomes digits/10^k, unless the budgets say no, and then it's a float by `ieee.Parse`: `1e1000000` goes float (2 ms, against about 8 minutes to build it exactly), and `1e1000000000000000000` would need 3.3×10^18 bits.
**Two budgets, both settings, both checked from bit lengths before computing:**
- *Size:* no integer past 2^24 bits (about 5 million digits), no ratio part past 2^14 bits, since GCD takes 64 ms there.
- *Work:* `bits(a)·bits(b)` at most 2^32 for one multiply, about 1 s today. `^` and a decimal literal's 10^k are tested on their last squaring. Powers of two and shifts are exempt: `2^1000000` stays exact, in under 1 ms.
- Over either: it carries on as a float, and the chip says "float, exact too large". Flags come from the conversion and the operation, so `2^(10^12)` lights no Inexact lamp. Nothing here is a refusal.
- **Ask once, with this preview:** `3^100000` exact in 2.3 s, against a float at once.
**Operations:** + − × ÷ on ints and ratios, normalised with `GreatestCommonDivisor`. Integer ÷ integer tries `DivRem` first, and remainder 0 gives an `IntValue`, so the 2^14 limit applies only to a ratio that remains: `10^6000/10` stays exact. `^` with an integer exponent stays exact within budget; negative exponents give a ratio. Base 0 and ±1 are settled for any integer `y` before the budgets, so `1^(10^30)` is exact.
**To float:** `ieee.Divide((ApFloat)n, (ApFloat)d)`. The implicit conversion is exact up to 2^30 bits, so this rounds once. `FloatArithmetic` must accept exact values too, converting them at the target, because `Ans` can be exact with Exact off.
**Off switch:** Exact is on by default (Matthew, 2026-11-07). With it off, the evaluator uses `FloatArithmetic` directly. Format targets always do.
**Traps:** `1/0` and `0/0` are `DivideByZero` on both paths, and `(-1)^(1/2)` is `Domain`. `0^0` is 1. Mixed exact and float converts at the target for now; B8 makes that honest.
**Tests:** random trees against a BigInteger rational oracle, reusing B4's generator; budget crossings, either side; `0.1+0.2` is 3/10; `1/3*3` is 1; `2^1000` exact; Muller's sequence to n = 100 exact.
**Mutations:** gcd skipped; sign left on d; budget checked after computing; the chip not set on fallback; d = 1 not folded; a lossless tag crossed.
**Done when:** those pass.

### B8. Exact functions and display
**Goal:** the rest of B5's table stays exact where it can, and rounds honestly where it can't.
**Needs:** B7.
**Files:** `Eval/ExactFunctions.cs`, `Eval/RoundedCall.cs`, `Text/ResultText.cs`, tests.
**Exact:** `abs`, `sign`, `max`, `min`, `floor`, `ceil`, `trunc`, `round`, `mod`, `rem`, comparisons, `fma` as a·b + c, and bit operators on integers (otherwise `Domain`). `sqrt` of a perfect square stays exact, by `SqrtRem` only when bits(x)² is within the work budget, because it's quadratic: `sqrt(2^1000000)` took 11.8 s that way. A power of two is settled from its exponent. Past that, go straight to the float route.
**`RoundedCall`, the helper every W session uses:** an exact argument the target can't hold must not simply be rounded first. That throws away a value the calculator holds: at 256 bits `ln(1+1e-80)` would be 0, and `sqrt(1/7)` at 53 bits would be wrong in the last place. So call f on the argument rounded down and up at W = p + 64 + max(0, top(x)), and double W until both results agree. With two exact arguments, call f on all four corners. Stop once W reaches p + 64 + max(0, top(x)) + bits(n) + bits(d), then use the nearest conversion and stay float: `log10(0.001)` in a directed mode never settles. An end that errors or gives ∞ counts as disagreeing until that bound: `1-1e-100` rounds up to exactly 1 at W = 320, and `atanh` of that has a finite answer. Format mode and Exact off don't use it: they keep hardware semantics.
**What goes through it:** `sqrt` of anything not exact; a mixed operator with a non-dyadic exact side, such as `1/3*sqrt(2)`, where rounding first differs in about 30% of cases; and `^` with an exact base the target can't hold, with f = x ↦ x^y and y held exact. `1.1^5000` at 53 bits is 9.19233389907065E+206, where rounding the base first gets 3 of 15 digits wrong. `y = 1/2` on an exact base goes to `sqrt`.
**Display:** a ratio shows its decimal, plus `= n/d` when both parts are short. An exact integer shows in full while it fits `Main`, otherwise `E99` through `WithPrecision(1024)`, with "exact, about N digits". Chips: exact, exact = n/d, float, float (exact too large).
**Tests:** a table of exact results; `sqrt(1/7)` at 53 bits is 0.37796447300922725; `sqrt(1+1e-1000)` at 256 bits in every mode; `RoundedCall`'s bound, and an erroring end, by stand-in functions, because Natural has no `ln` yet; `1.1^5000`; one random test of mixed operators against the rational oracle; ratio and big-integer previews, timed.
**Mutations:** `SqrtRem`'s remainder ignored; a ratio floor on negatives; the helper taking one end; the bound off by one; an erroring end accepted.
**Done when:** those pass, and `calc-worker --eval` shows `0.1+0.2` as 0.3 and `2^1000` in full.

### B9. Worker process
**Goal:** the engine behind a stdio loop that survives anything.
**Needs:** B8.
**Files:** `Calc.Protocol/EvalContext.cs`, `Messages.cs`, `Calc.Worker/Program.cs`, `Loop.cs`, tests.
**`EvalContext`:** one record, carried on every request and stored on every history line: target, mode, exact, angle, budgets. Protocol has no Natural, so target is bits or (exponent bits, precision), and mode is Protocol's own enum; Engine maps them to `Target` and `RoundingMode`. G4 adds an interval flag, F2 a word size, and H2 a definitions hash. It keys every worker cache.
**Protocol:** one JSON object per line, `System.Text.Json` with source generation, no packages. Later sessions add fields, and name this file in their Files.
- Requests: `eval` (id, source, context, ans, options), `digits` (id, value, format), `reformat` (id, value, notation, digits), `fetch` (id, heldId), `functions` (id), `stop` (id), `ping`.
- Replies: `result` (id, short, main, kind, digits, lossless or held, flags, ms, understood, warnings), `digits` (id, text), `value` (id, lossless), `error` (id, kind, message, from, to), `progress` (id, done, estimate), `functions` (id, list), `pong`. `value` answers `fetch`, and is exempt from the size limit below.
- `digits` and `reformat` are implemented in C7 and C6. `understood` is the printed tree, when implicit multiplication or `°` was involved.
**`ans`:** sent only when the source mentions it. A float `ans` whose target differs from the context's is refused (rule 14): that's the second wall behind the Client's guard.
**Size limit:** `lossless` goes on the wire only up to 1 MiB of text. Past that the reply says `held`, the worker keeps the value by id, and `ans` can name it. The worker evicts its oldest held values past a quarter of the heap cap. A miss replies `error` kind `ValueGone`, and the client replays the line. Without this, 30 values at 2^28 bits would be 4 GB in the client.
**Threads:** reader; a compute thread at `BelowNormal` with an explicit stack, `new Thread(body, 256 << 20)`; a locked writer. Parse on the compute thread. The reader sets a stop flag, and the evaluator checks it between nodes for the soft stop, which ends in `error` kind `Stopped`. Stdin EOF ends the process, so an orphan dies.
**Traps:** a malformed line gets an `error`, and never kills the loop. `OutOfMemoryException` becomes `TooBig`. Spike: `docs/research/ui/worker-thread-process-spike.cs`.
**Tests (real exe):** round trip; ids echoed; 200 evals in a row; malformed lines; soft stop between nodes; stdin close ends the process within a second; an 830,000-character reply intact; `1/3` then `Ans*3` passing the returned lossless text gives exact 1; `2^1000000` then `Ans+1` stays exact; a mismatched float `ans` refused; `2^5000000` (1,250,001 hex characters) replies `held`, `Ans+1` naming it stays exact, and `fetch` returns it; a 199,999-term sum gives an answer, and a 1,000,000-term sum a clean error.
**Done when:** those pass.

### B10. Worker client, with Stop
**Goal:** a client that owns the worker's life, so nothing above it thinks about processes.
**Needs:** B9.
**Files:** `Calc.Client/IWorker.cs`, `WorkerClient.cs`, `CommitInfo.cs`, `tests/…/FakeWorker.cs`, tests.
**How:** spawn `calc-worker` from `AppContext.BaseDirectory`, stdio redirected. `Evaluate` returns a task. `Stop`: send `stop`, wait 150 ms, then `Kill(entireProcessTree: true)`. The task completes as `Stopped`, and the next request respawns lazily. A worker that dies by itself completes as `WorkerDied`, with a message, and respawns the same way. Replies with a stale id are dropped.
**Heap cap:** `DOTNET_GCHeapHardLimit`, default 4 GiB, a setting. **The runtime reads this as hex:** "4294967296" gave 266 GiB, which is no cap. Write `"0x" + bytes.ToString("X")`. At spawn use the smaller of the setting and free commit less 1 GiB (`GlobalMemoryStatusEx`, `ullAvailPageFile`), in signed Int128, with a floor of 256 MiB: this laptop runs out of commit before it runs out of RAM. A cap of 0 means none, a negative one wraps to none, and a tiny one stops the worker starting, so below the floor don't spawn: complete with a message saying memory is short.
**Traps:** read stdout on one dedicated task, or a full pipe deadlocks a big reply. Stop during a respawn. Dispose kills.
**Tests (real exe):** Stop inside a 2,000,000-bit multiply (the spike timed `Kill` to exit at 2 to 3 ms, so about 153 ms from Stop, and about 250 ms to the next answer); a worker made to die; a child reports `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` equal to the cap; `CommitInfo` injected at 0.5, 1.0 and 1.1 GiB free; an out-of-memory under a small cap reports and recovers; 20 stop-and-go cycles leave no process behind.
**Done when:** those pass, and a test proves no `calc-worker` remains after dispose.

### B11. Session, the clear rule, console calculator
**Goal:** the behaviour in PLAN.md's clear table, in plain testable code, and a console calculator to try.
**Needs:** B10.
**Files:** `Calc.Client/Session.cs`, `HistoryLine.cs`, `Calc.Cli/Program.cs`, tests.
**A history line:** id, source, `EvalContext`, `AnsFrom` (the line `Ans` meant, when the source mentions it, found with the Lexer), short, main, kind, digits, lossless or held, flags, time.
**`Ans` is a line, not only a number.** The keypad writes `Ans` into every chain, so text alone isn't enough: `2+3` then `Ans*4` must recompute to 20, whatever else has happened. `Session` holds the current `Ans` line.
**Operations:**
- `EvaluateAsync(source)`. If the source mentions `Ans` and there's none, the message says it was cleared, and offers to recompute the line.
- `SetTarget` applies the clear table, stops running work, drops replies by id. A no-op if unchanged.
- `SetMode`, `SetExact`, `SetAngle` don't clear. An exact `Ans` converts at use with Exact off.
- `ClearEntry` (CE); `ClearAll` (AC: entry, result, `Ans`, and stops running work); `WipeEverything`; `StopAsync`.
- `RecomputeAsync(lineId)` replays the `AnsFrom` chain oldest first at the current target, using each line's own context with only the target replaced, feeding each step's result in as the next step's `ans`. It adds one new line. A deleted link gives a clear error.
**Cli:** type expressions; `:bits 300`, `:digits 100`, `:mode down`, `:exact off`, `:clear`, `:history`, `:rerun 3`, `:quit`. Ctrl+C during an evaluation calls Stop and returns to the prompt with history intact.
**Traps:** a reply arriving after any clear belongs to the old state: drop it. Dim a line when any of its context differs; only a target change clears. Held values: on `ValueGone`, replay the line.
**Tests:** one for each row of the clear table, using `FakeWorker`; the smuggling test, trying to pass an old number through every route, which must fail; the flow `2+3`, `Ans*4`, SetTarget, `5`, recompute line 2, expect 20; no-op on the same target; a stale reply dropped after AC; after a kill, `ValueGone` replays the line; 100 lines at 2^24 bits stay under a set memory figure; a real long multiply stopped from the Cli.
**Done when:** those pass. **Try this:** run `Calc.Cli`, work out `1/3` and `Ans*3`, set `:bits 1024`, try `Ans`, then `:rerun 2`.

### B12. Cost model and preflight
**Goal:** refuse what can't fit, and estimate what will take a while.
**Needs:** B11.
**Files:** `Calc.Engine/Cost/CostModel.cs`, `Preflight.cs`, `Calc.Protocol/Messages.cs`, `Calc.Worker/Loop.cs`, tests.
**How:** walk the tree estimating each node's result size and cost, in limb steps at about 15 ns each, from the tables in report 2. Calibrate with a short timing loop at worker start, because the machine has two speeds. Expect ±3×.
**Output:** seconds and bytes. Bytes past the heap cap or live free commit: `TooBig`, before starting, naming the size ("about 125 GB"). Estimated seconds go in the first `progress` reply, for C5.
**Exact path:** `2^(10^12)` exceeds the budget and falls to float, which is fine: the preflight must not refuse a thing the fallback handles.
**Traps:** a shift can use twice its result size for a moment. `ApInt.Pow` takes an int: check before casting (rule 5). Don't refuse anything merely slow, because Stop handles slow. Later sessions add entries for their functions (W0 to W5, F4).
**Tests:** a table of expressions and verdicts; for ten operations, estimated against measured within ×4 at three sizes; a 199,999-term sum through the preflight walk on the compute thread.
**Done when:** `2^(2^40)`, `3^(2^40)`, huge shifts and long literals all give a clean answer or a clean refusal, under a 1 GiB heap cap.

## C. Window

Every session here runs `--smoke`, takes dark and light screenshots, and ends with a try-this list. All window state lives in `Calc.Client`, so tests need no WPF.

### C1. WPF shell and themes
**Goal:** the real window, empty but for its layout, in both themes, with the agent's eyes working.
**Needs:** B11.
**Files:** `src/Calc.App` (csproj as the skeleton's), `App.xaml`, `MainWindow.xaml`, `Themes/`, `SmokeTest.cs`.
**How:** the regions in PLAN.md's window sketch, with placeholders. Merge Fluent's `Fluent.Dark.xaml` or `Fluent.Light.xaml` by hand: `ThemeMode` is build error WPF0001. `--smoke` spawns the worker, evaluates `1+1`, and returns an exit code; `../Evolutionary.Net/Studio/App.xaml.cs` is the model for that. `--screenshot <dir>` renders PNGs with no window, by Measure, Arrange and `RenderTargetBitmap`, as in `docs/research/ui/wpf-spike.cs` lines 403 to 412. Studio's own version shows a window, so don't copy that part. Add a PerMonitorV2 manifest.
**Traps:** extend `LayeringTests` to `Calc.App.dll`. The worker must land beside the app: the skeleton's copy step.
**Done when:** `--smoke` exits 0 and both theme screenshots look right. **Try this:** open it; drag it between scales if you have a second screen.

### C2. Entry and result
**Goal:** type, press Enter, see the answer, and see where an error is.
**Needs:** C1.
**Files:** `Calc.Client/ShellState.cs`, `Calc.App/Views/EntryView.xaml`, `ResultView.xaml`.
**How:** a monospace entry box. Enter evaluates, Esc is CE, Up and Down walk the history. The result shows `Main`, the kind chip, and `understood` when the worker sends it. An error underlines its span in the entry, and says what's wrong in a sentence.
**Traps:** nothing evaluates on a keystroke yet (H8).
**Tests:** `ShellState` against `FakeWorker`: evaluate, error spans, history walk.
**Done when:** those pass, and a screenshot shows an answer and an underlined error. **Try this:** `1/0`, `2^1000`, `(1+`.

### C3. Standard keypad
**Goal:** a keypad that behaves the way a calculator's should, on a text-first model.
**Needs:** C2.
**Files:** `Calc.Client/KeypadState.cs`, `Calc.App/Views/Keypad.xaml`.
**Keys:** digits, `.`, `+ − × ÷`, `( )`, `^`, `√`, `x²`, `1/x`, `%`, `±`, `⌫`, CE, AC, `=`, `Ans`. Keys edit the entry's text; they hold no state of their own. Use the Lexer to find the last operand.
**The table to build and test:**
- `±` toggles the sign of the last operand: `2+3 ±` gives `2+(-3)`, then -1 on `=`. Straight after a result it gives `-Ans`.
- `1/x`, `√`, `x²` and `%` apply to the last operand, or to `Ans` straight after a result: `9 √` gives `√9`.
- `1/x` wraps in parentheses unless its operand follows +, − or nothing: `7 + 2 1/x` gives `7+1/2`, `8 ÷ 2 1/x` gives `8÷(1/2)`, and `2 ^ 3 1/x` gives `2^(1/3)`.
- After a result, an operator key continues from `Ans`; a digit or `(` starts afresh.
- Keyboard `=` always evaluates until H2. From H2 it inserts only after a bare name, or `name(a, b)` with bare identifiers as arguments, when the name isn't `Ans`, a constant, or in the worker's `functions` reply.
**Tests:** key sequences to entry text, in a table, against `KeypadState`.
**Done when:** the table passes, and the keyboard and keypad agree. **Try this:** `2 + 3 =`, then `× 4 =`, then `±`, then a digit.

### C4. Precision selector and clear
**Goal:** the feature you asked for by name.
**Needs:** C3.
**Files:** `Calc.Client/PrecisionState.cs`, `Calc.App/Views/TopBar.xaml`, `StatusRow.xaml`.
**How:** presets from PLAN.md, each showing bits and digits, from `Digits`. A custom box: `300` is bits, `100d` is digits. 1 to 2^30 is accepted, with a warning above the top preset. Rounding selector, in words: nearest, toward zero, up, down. Exact toggle. Changing the target calls `Session.SetTarget`, and the status row says "Set to 1,024 bits. Cleared." with a "Recompute last line" button. AC, CE and a Wipe everything menu item.
**Traps:** a change during a running evaluation stops it. Custom values validate on commit, not on each key.
**Tests:** `PrecisionState` table; AC against CE against a target change; `2+3`, `Ans*4`, change precision, Recompute last line gives 20.
**Done when:** those pass. **Try this:** `1/3` at 53, then 256, then custom `100d`; check `Ans` is gone; do the `2+3` chain above.

### C5. Busy, progress, Stop
**Goal:** long sums feel safe.
**Needs:** C4, B12.
**Files:** `Calc.Client/BusyState.cs`, `Calc.App/Views/StatusRow.xaml`.
**How:** after 200 ms of work, show a bar of elapsed over estimate, and Stop. Before the first estimate, the bar is indeterminate. The entry stays editable. Enter while busy says "still working" and does nothing. A stopped line isn't added to history; its text stays in the entry.
**Traps:** the bar is elapsed over estimate, not true progress, so cap it at 95%. Worker death shows a message, not a dialog.
**Tests:** `BusyState` with a slow `FakeWorker`; `--smoke` extended to stop a real `sqrt(2)` at 262,144 bits.
**Done when:** those pass. **Try this:** set custom `262144`, enter `sqrt(2)` (about 30 s), press Stop, type `1+1`.

### C6. History panel
**Goal:** a long history that stays fast and is useful.
**Needs:** C5.
**Files:** `Calc.Client/HistoryState.cs`, `Calc.App/Views/HistoryView.xaml`, and the worker's `reformat` request.
**How:** a virtualised list: source, `Short`, kind chip, precision tag; dimmed if any of its context differs from the current one. Click recalls the source to the entry. Context menu: copy result, copy lossless, recompute, delete line. Lossless copies as the `A` text, or the exact form, which pastes back. A toggle for aligned view: monospace, right-aligned, always scientific, fetched with `reformat`.
**Traps:** rows hold `Short`, never full digits: stock boxes split long lines every 9,600 characters. A virtualised list of 1,000 rows filled in 59 to 110 ms.
**Tests:** `HistoryState`; 10,000 lines stay responsive in a smoke check.
**Done when:** those pass. **Try this:** aligned view with ten numbers of different sizes; check older lines dim after a precision change.

### C7. Full digits from the worker
**Goal:** every digit of a long result, as text, with progress. No WPF in this session.
**Needs:** C6.
**Files:** `Calc.Worker/Loop.cs`, `Calc.Engine/Text/FullDigits.cs`, `Calc.Client/DigitsState.cs`, tests.
**How:** `digits` steps `StartFormat` in slices, with `progress` replies. It runs in a **second worker instance** of the same exe, so a long Show all doesn't hold up the calculator, and Stop on either is independent. A held value crosses by `fetch`. Warn first when the estimate is over about 5 s. An exact integer past about 1 Mbit takes minutes in decimal, so offer hex, which is linear. A ratio shows `n/d` in full, plus its decimal to the current digits. `SetTarget`, `ClearAll` and `WipeEverything` stop the digits worker and drop its replies, or old digits would paint into the open window.
**Traps:** jobs overrun their slices on long non-integer output (report 3), so the bar may stall. Stop still works: it's the kill.
**Tests (real exe):** text identical to `ToString`; a job stopped mid-way; evaluating while a `digits` job runs; a held value through `fetch`; a target change mid-job leaves no digits.
**Done when:** those pass.

### C8. Digit viewer
**Goal:** a viewer that shows a million digits without freezing.
**Needs:** C7.
**Files:** `Calc.App/Controls/DigitView.cs`, `Views/DigitsWindow.xaml`, line maths in `Calc.Client`.
**How:** "Show all" opens it. `DigitView` is a `FrameworkElement` that shapes only visible lines with `FormattedText`. The 23-line spike at `wpf-spike.cs` line 27 took a median of 2 ms on a random jump through a million digits, but it has no scrolling, selection, gutter, grouping, jump or theme, so those are this session's work: group by 10, 100 per line, a line number per row, jump to digit N, selection, copy all. A clear blanks the window and offers to recompute.
**Tests:** line maths in `Calc.Client`; a `--screenshot` of a jump at a million digits.
**Done when:** those pass. **Try this:** `2^1000000`, Show all, jump to the middle.

### C9. Settings, publish, v0.1
**Goal:** something you can double-click.
**Needs:** C8.
**Files:** `Calc.Client/Settings.cs`, `publish.cmd`, `README.md`.
**How:** settings in `%APPDATA%\Calc\settings.json`: target, mode, exact, theme, window bounds, heap cap (bytes; B10 writes it as hex), and B7's budgets. `publish.cmd` publishes App and Worker into one folder, because publishing the App alone drops the worker's DLLs (0.56 MB framework-dependent, 140 MB self-contained). README with a screenshot. Tag v0.1.
**Tests:** a bad settings file falls back to defaults; run `--smoke` from the publish folder.
**Done when:** that passes. **Try this:** a week's real use, and tell me what annoyed you. Then check a real high-DPI screen, which nothing has yet.

## D. Speed

These split the session 10 brief in Natural's TODO. Old-against-new timings go in Natural's WORKLOG. `docs/research/bench/throwaway-fast-magnitude.diff` is a quick version of the adder, Karatsuba and short division, without squaring, and passed all 682 tests. It's a reference, not code to paste.

### D1. Adder and short division (Natural)
**Goal:** `HalfAddAll`'s loop replaced by a Kogge-Stone form, five steps whatever the bits; and a faster constant for one-limb divisors in `DivRem`.
**Needs:** A2. The adder is within the bitwise rule (Matthew, 2026-11-07).
**Baseline:** multiply at 10k bits 10.7 ms, 100k bits 1.91 s; π Machin at 4,096 bits 169 ms, 16,384 bits 2.72 s. The diff gave 8.9 ms, 883 ms, 52 ms and 511 ms.
**Tests:** the suite unchanged; 2,000,000 word triples against plain addition, biased to all-ones; one-limb divisors against `BigInteger`, including limbs with the top bit set, which the diff doesn't handle.
**Mutations:** a prefix step skipped, which only all-ones runs catch; a shift amount wrong; the remainder carried wrongly; a limb boundary.

### D2. Karatsuba and squaring (Natural)
**Goal:** Karatsuba above a measured threshold (flat from 8 to 16 limbs in the bench), plus a dedicated squaring. Still built on `FullAdd`.
**Needs:** D1.
**Baseline, after D1:** multiply at 100k bits 883 ms, 1M bits about 90 s. The diff gave 89 ms and 3.5 s.
**Tests:** sizes either side of the threshold, unequal lengths, one operand under half the other, squares against `Multiply(a, a)`.
**Mutations:** the list in Natural's session 10 brief.

### D3. Calc: raise the ceiling
**Needs:** D1, D2, B12, C4.
**Goal:** Calc takes advantage. Rerun `docs/research/bench/bench.cs`, update the cost model's constants, raise B7's work budget to suit, add the 16,384 preset, and label 8,192 normally. Add a results table to WORKLOG, including the largest `ModPow` under 10 s, which F5 reads for its warning.

## E. Science

**T sessions run in Natural,** from `docs/transcendentals/TRANSCENDENTALS-PLAN.md` section 12 there (A1 copies it). Each brief has goal, files, pitfalls and mutations, plus baseline and tests where the session needs them. Read its Amendments first. Before T1, ask the seven questions in its section 15, in one round, recommended answers first.

| Session | Delivers | Needs |
|---|---|---|
| T1 | `Ball`: mid-radius enclosure | A2, D1 preferred |
| T2 | Ziv's loop, nudge, certificate, test hooks | T1 |
| T3 | Binary splitting, π, τ, the cache | T2 |
| T4 | e, ln 2, ln 10, `IFloatingPointConstants` | T3 |
| T5 | `Exp` | T4 |
| T6 | `Log`, `LogP1` | T5 |
| T7 | Exp family | T6 |
| T8 | Log family, the lazy exact check | T7 |
| T9 | `Magnitude.CubeRoot`, `ApInt.Cbrt`, `ApFloat.Cbrt` | T4 |
| T9b | `ApInt.Root`, `RootN`, `Hypot`, `IRootFunctions` | T9, T6 for huge n |
| T10 | `Pow` | T8, T9b |
| T11 | `Sin`, `Cos`, `Tan`, `SinCos`, `TrigReductionBits` | T5 |
| T12 | `AngleUnit`, `SinPi` family, degrees, unit overloads with flags | T11 |
| T13 | `Atan`, `Atan2` | T6, T11 |
| T14 | `Asin`, `Acos`, unit overloads with flags | T12, T13 |
| T15 | The six hyperbolics, `IHyperbolicFunctions` | T8 |

T17 to T20 (jobs, generic math, tuning, Payne-Hanek) aren't needed for launch. T21 and T22 are I21 and I22.

**W sessions run in Calc, each in its own chat.** Each one adds its functions to `FunctionTable`, adds cost entries by bit size from sections 2 and 13 of the T plan, with an estimate-against-measured test within ×4 at three sizes, and has one evaluator test per function.

### W0. Function table, constants, Scientific tab
**Needs:** T4, C9.
**Goal:** the Scientific tab exists, and every function has a key. Those not yet in Natural are greyed, from the worker's `functions` reply. `pi`, `tau`, `e`, `ln2`, `ln10`, `phi` are constants, through `GetPi` and friends, with `phi` from `Sqrt`. Constants are floats at the target. Cost entries from section 2's constants table.
**Traps:** `e` the constant against `1e5` the literal; test `2e`. Natural's `GetPi` serves any precision, so nothing here is cleared by a target change.
**Done when:** `2π` works at every preset. **Try this:** `π` at 4,096 bits; it's slow only the first time.

### W1. exp and ln
**Needs:** T6, W0.
**Goal:** `exp`, `ln`, `log1p`, keys lit, and two rules the later W sessions reuse.
**Exact arguments:** through B8's `RoundedCall`, so `ln(1+1e-80)` is 1e-80 at 256 bits.
**Promotion, back to exact:** a float result becomes exact only if (1) its argument is an exact value (`IntValue` or `RatioValue`) that converted with no Inexact, read from that conversion's own flag change; (2) the call itself raised no Inexact; and (3) the exact value fits B7's budgets. Otherwise it stays float. Never in format mode or with Exact off. So `exp(0)` is exact 1, and later `log2(8)` is exact 3. But `ln(1+1e-80)` must not become exact 0: `ieee.Divide(10^80+1, 10^80)` is 1 with Inexact, and a per-call flag alone would miss that. Nor must `ln(sqrt(1+1e-80))`: that argument is a float that happens to equal 1.
**Tests:** one per function; both rules, with those counter-examples; domain errors; `exp(1000.1)` at 256 bits against a pinned value.

### W2. Logs, powers and roots
**Needs:** T10, W1.
**Goal:** `log2`, `log10`, `log(x, b)`, `expm1`, `exp2`, `exp10`, `root(x, n)`, `cbrt`, `hypot`, keys lit. The float path of `^` becomes `Pow`, retiring B5's bounds route, and any non-integer `y` works. An exact base still goes through `RoundedCall`, as B8 set up, with `Pow` inside it.
**Traps:** the root key must call `RootN`: `Pow(8, 1/3)` isn't 2, because the exponent was rounded. An exact ratio exponent p/q with small q goes through `RootN` and an integer power, so `8^(2/3)` is exact 4. `e^y` rewrites to `Exp(y)` when the base is the constant `e`, because powering a rounded e loses digits. `2^(10^12)`, a float with no Inexact, must stay float: `(ApInt)x` would throw, so promotion's budget check matters here.
**Ask once:** one-argument `log(100)` (recommended: base 10, with `ln` for natural); and a negative base with odd q, `(-8)^(1/3)` (recommended: -2 in exact mode, `Domain` otherwise).
**Tests:** `4^0.5`, `16^0.75`, `log2(8)` exact, `log_4(8)` is 3/2.

### W3. Trig and Angle
**Needs:** T12, W2.
**Goal:** `sin`, `cos`, `tan`; Angle (rad, deg, grad) on the top bar and in `EvalContext`; postfix `°`.
**`°`:** directly on a trig function's whole argument, it calls the function in degrees whatever Angle says, so `sin(30°)` is exact 1/2. Anywhere else, `x°` converts to the current Angle: x at deg, x·10/9 at grad, x·π/180 at rad, so `sin(30°+15°)` is right at all three. Show the understood form.
**Exact arguments in degrees or gradians:** reduce exactly mod 360 or 400 in `ExactArithmetic` first. Otherwise the reduction works on the stored x, not the typed one: `sin(10^23)` in degrees at 53 bits gives 0.53, and the truth is -0.98. A reduced value the target can't hold then goes through `RoundedCall` in that unit: skipping it gets about 22% of k/m degrees wrong. Radians use `RoundedCall` throughout.
**Ask once:** `sin(π)` prints about 1e-77 at 256 bits. Recommended: at Angle rad only, `sin`, `cos` and `tan` of an exact rational multiple q of π rewrite to the degrees form of 180q, through the exact reduction above, giving 0, and exact 1/2 for `sin(π/6)`. Not `SinPi`, which takes a rounded float. At deg, `sin(π)` means sin(3.14°).
**Tests:** every 15° in every mode; `sin(30)` in degrees raises no Inexact; `sin(1e22)`; `sin(1e120)` in degrees at 256 bits is sin(280°); `sin(30°+15°)` is 0.7071 at all three Angles; `sin(100/3)` in degrees against an exact oracle; `sin(π/6)` exact 1/2 in every mode; recompute of a line keeps its own Angle.

### W4. Huge-argument warnings
**Needs:** W3, B12.
**Files:** `Calc.Protocol/Messages.cs`, `Calc.Worker/Loop.cs`, `Calc.Client/ShellState.cs`, tests.
**Goal:** two warnings. The cost one comes before the work starts: `eval` gains `confirmed`, and without it the worker replies `error` kind `NeedsConfirm` with the warning text, and the window asks and resends. The accuracy one goes in the reply's `warnings`.
- *Cost:* above a limit, from `TrigReductionBits`: "needs π to about 3.32M bits, about N minutes: continue?". N comes from W0's π cost entry. Degrees skip it: no π is needed.
- *Accuracy, float arguments only:* when ulp(x) reaches 2^-10 of the period, say how many digits survive. Exact arguments don't need it, because W3 and `RoundedCall` keep them whole.
**Tests:** both warnings, either side of their edges, in rad and deg; `sin(10^16+1)` at 53 bits, exact, is -0.1056 with no warning.

### W5. Inverse trig and hyperbolics
**Needs:** T14, T15, W3.
**Goal:** `asin`, `acos`, `atan`, `atan2`, honouring Angle; the six hyperbolics; the Scientific tab complete.
**Tests:** one per function; domain edges; `asin(1/2)` in degrees is exact 30. **Try this:** a long sum of your own; note anything to fix before moving on.

## F. Programmer

### F1. Radix text
**Goal:** integers to and from any radix 2 to 36, plain sign-magnitude, and `x to hex` display requests.
**Needs:** B8.
**Files:** `Text/Radix.cs`, tests.
**How:** `ToRadix`, `FromRadix` and `ToOctal` in `natural-api/OnTop.cs`; chunked `DivRem` is 13 times faster than one digit at a time (`Loose.cs`). Hex and binary by rule 6. `to` is reserved in B3: use `to hex`, so `in` stays free for inches.
**Tests:** round trips on biased values; against BigInteger; 0 and negatives.
**Mutations:** chunk boundary; leading zeros kept; sign dropped.

### F2. Word-size arithmetic
**Goal:** a backend where values live in N bits, signed or unsigned, N from 1 up, or unbounded.
**Needs:** F1.
**Files:** `Eval/WordArithmetic.cs`, `Calc.Protocol/EvalContext.cs`, tests.
**How:** `EvalContext` gains `Word` (null, or size and signed). Hold masked non-negatives (rule 7), wrapping after every operation. `and or xor not`, `shl shr sar`, `rol ror` by the recipe `((x<<r)|(x>>(w-r)))&mask`, `popcount`, `clz`, `ctz`, `bswap`, `bitrev`, all by own code. Division truncates; signed views convert first.
**Edges:** unbounded is signed only, `not` is -x-1, and `rol`, `ror`, `clz`, `bswap` and `bitrev` give `Domain` there. `bswap` needs N a multiple of 8. A shift of N or more gives 0, or sign fill for `sar`, and hardware tests use counts below N. MIN / -1 wraps to MIN.
**Tests:** at 8, 16, 32 and 64 bits against the hardware types, 140,000 cases; at 1,024 bits against BigInteger with masks.
**Mutations:** a mask missed; signed `>>` logical; rotate by 0 or N; sign extension.

### F3. Programmer tab
**Goal:** word size picker, signed toggle, live HEX DEC OCT BIN rows, and a clickable bit grid.
**Needs:** F2, C9.
**Files:** `Calc.App/Views/ProgrammerTab.xaml`, `BitGrid.cs`, `Calc.Client/ProgrammerState.cs`, `Session.cs`, `Calc.Protocol/Messages.cs`, `Calc.Worker/Loop.cs`.
**How:** the tab sets `Word`. Integers are exact, so this isn't contamination, and **a word size or sign change keeps the bits and reinterprets them, truncating to the new width, without clearing.** Leaving the tab clears `Word`; older lines dim. The rows come from the worker, through `reformat` in hex, dec, oct and bin, and update on each result, not on each key. **Ask once,** with -1 at 8 bits going to 16 as the preview: keeping the bits gives 255, and keeping the value gives -1. Recommended: keep the value when widening. `A`–`F` and base keys insert prefixed literals (`0xFF`), so a line's text never depends on the radix. `^` stays power, and the XOR key inserts `xor`. A bit click writes a literal into the entry, as a new line. Group the grid by 4 and 8.
**Tests:** `ProgrammerState`; clicking a bit changes the value; -1 at 8 bits flipped to unsigned is 255.
**Done when:** those pass. **Try this:** -1 at 8 bits, then flip to unsigned.

### F4. Integer functions
**Goal:** `n!`, `nCr`, `nPr`, `gcd`, `lcm`, `modpow`, `modinv`, `isqrt`, `fib`, floored and truncated division. They're typed; if W0 has built the Scientific tab, add keys there too.
**Needs:** B8, B12.
**How:** `OnTop.cs` has `Factorial`, `NPr`, `NCr`, `Lcm`, `ModInverse` and `ProductRange`; `gcd`, `modpow` and `isqrt` are Natural members; `fib` is new, by fast doubling. Factorial and nPr use a product tree. Preflight sizes each one first: 100000! took 297 s by repeated multiply. These have no float route, so the work budget doesn't apply; Stop covers them. Non-integer `!` waits for I13. With Exact off, an integer-valued float converts first.
**Tests:** against BigInteger; zero and negative edges; `modinv` not invertible.
**Mutations:** `nCr` symmetry skipped; `modpow` sign; `lcm(0, n)`.

### F5. Primes and factoring
**Goal:** `isprime`, `nextprime`, `factor`.
**Needs:** F4.
**How:** Miller-Rabin with 12 fixed bases, wording its answer "probable prime" above the range those bases prove; trial division then Pollard rho, with a time budget, reporting a partial factorisation when it runs out. Warn above the largest `ModPow` size D3 recorded under 10 s.
**Traps:** one 2,048-bit `ModPow` takes 7 s before stage D, so `isprime` there is 84 s. It runs in the worker, so Stop covers it.
**Tests:** known primes, Carmichael numbers, semiprimes to 64 bits, and products checked by multiplying back.

## G. Truth tools

**These run with Exact off, and the panel says so.** Exact-first solves Rump outright, as -54767/66192, so his example only shows anything as floats. On an exact result, show "exact: nothing to prove", with a "run as floats" action.

### G1. Format mode, engine
**Goal:** a target that's an IEEE format, behaving exactly as that hardware would.
**Needs:** B8, B11.
**How:** mostly a switch: `FloatArithmetic` at a format target, Exact off. Literals go straight into the format, rounded once. Custom formats (`bf16`, `(8,8)`, `(11,53)`) through `new IeeeFormat(w, p)`.
**Tests:** every operation at binary16, 32 and 64 bit for bit against `Half`, `float` and `double`, nearest mode, all NaNs equal; `0.1+0.2` gives 0.30000000000000004; flags predicted by the rational oracle.
**Mutations:** literal parsed then converted. Both `2.4703282292062328e-324` and the 100-digit `2.470328229206232720882843964341106861825299013071623822127928412503377536351043759326499181808179962e-324` are needed: the second catches a parse at 256 bits first.

### G2. Format mode, window and flag lamps
**Goal:** format presets in the precision selector, and five lamps.
**Needs:** G1, C9.
**How:** the lamps show one line's flags, not sticky ones. Beside each format result, a truth row: the same line replayed exactly or at 1,024 bits (rule 14), with the error in ulps. Overflow and Underflow only ever light in a format, so grey them out at bit targets. Extend the smuggling test to the truth row.
**Try this:** `0.1+0.2` in binary16, 32, 64; `1e300*1e300` in binary64.

### G3. Bit inspector
**Goal:** the Playground's "take the result apart" as a tab.
**Needs:** G2.
**How:** port `../Natural/Natural_Playground/FloatReport.cs` into `Calc.Engine`, replacing its internals with public calls, behind an `inspect` request in Protocol and Worker. Sign, exponent and significand fields, ulp, next up and down (`OnTop.cs`), the exact stored value as a ratio. Not on keystrokes: that's `FloatPanel`'s trap. A clear blanks it.
**Tests:** against `FloatReport`'s output for a table of values; ulp and next against `Math.BitIncrement`, 19,734 doubles.

### G4. Intervals, engine
**Goal:** a true enclosure for every result, and a count of proven digits.
**Needs:** B8.
**Files:** `Eval/IntervalArithmetic.cs`, `Values/IntervalValue.cs`, `Text/Lossless.cs` (a fourth tag, `v:<target>:<A lo>:<A hi>`), `Calc.Protocol/EvalContext.cs` (an interval flag, since it changes the value kind), `Messages.cs` (a `proven` field), `--eval --proven`, tests.
**How:** directed rounding in `IeeeContext`, `TowardNegative` for lows and `TowardPositive` for highs. + − × ÷ √ and integer powers. `demo.cs`'s backend ran at about 4.8× float cost. It sits under `ExactArithmetic`'s fallback, so exact parts stay exact, and intervals start where exactness ends.
**Proven digits:** both ends rounded to n digits print alike (`demo.cs` lines 95 to 102), capped at rule 9. Don't scan down from the cap, which took 17.7 s on one result at 8,192 bits: n can't exceed about (top(x) − top(hi − lo))·log10 2 + 2, so start there, and an interval spanning 0 is 0 at once. A divisor spanning 0, or an interval partly outside √'s domain, is unbounded with 0 proven, never an error. "0 proven" means unproven, not wrong.
**Never claim what isn't carried:** `Ans`, M and saved values must carry both ends when intervals are on. A plain float operand in an interval evaluation is unproven from that point, never a point interval. Without this, `(sqrt(2)+1e70)-1e70` then `Ans*3` prints 76 "proven" digits, of which 7 are true.
**Tests:** the true value, by the rational oracle, inside the interval on random trees; Rump, Exact off, gives 0 at 121 bits and 36 at 122; that `Ans` case. H4 and H6 test M and saved values.
**Mutations:** an end rounded the wrong way; a product's corners missed; a sign case; a point interval accepted.

### G5. Proven digits in the window
**Goal:** a toggle, off by default, and a proven-digits chip on each result, which grey out digits that aren't proven.
**Needs:** G4, C9.
**Try this:** Rump's expression with Exact off, at 100 and at 130 bits.

### G6. Intervals through monotone functions
**Goal:** the 17 monotone functions, table-driven, with domain edges.
**Needs:** G4, W5.
**How:** both ends through T's four-mode correct rounding. An interval partly outside the domain (`ln` over 0, `asin` past 1) is unbounded with 0 proven, never an error.
**Tests:** containment against Natural's values at higher precision. **Mutations:** a decreasing function's ends not swapped; a domain edge.

### G7. Intervals through the rest
**Goal:** `sin`, `cos`, `tan`, `cosh`, `x^y`, `hypot`, `atan2`, `log(x, b)`, even roots.
**Needs:** G6.
**How:** extrema inside the interval, from `GetPi` enclosures, or exact 90 + 180k in degrees. Width of a full turn or more gives [-1, 1]. A pole inside is unbounded.
**Tests:** intervals spanning extrema and poles. **Mutations:** an extremum missed; a wrong quadrant.

### G8. N proven digits on demand
**Goal:** `prove(expr, 40)`: raise working precision until 40 digits are proven, or stop and say why.
**Needs:** G5.
**How:** a Ziv loop at expression level, capped in rounds and time, replaying the line at each precision (rule 14). `sqrt(2)^2-2` straddles 0 through 4,096 bits and must report a bound, not spin.
**Tests:** Rump, 40 digits at 256 bits; that equality wall; the cap; the smuggling test extended.

### G9. Precision sweep
**Goal:** a table of one line at a ladder of precisions, with stable digits marked.
**Needs:** G5.
**Words:** say "stable", never "correct". **Ladder:** 80, 96, 106, 113, 120, 121, 122 by default, plus custom. Each row replays the line and its `AnsFrom` chain at that precision (rule 14).
**Tests:** `1/3` then `Ans*3`, Exact off, swept at 53, 106 and 212 bits, gives no row of 0.99999999999999994. Rump across 99 to 122 bits gives 23 agreeing wrong rows, then a change. Muller by a tree built in the test, because the sequence can't be typed before I1: at 53 and 106 bits it reaches exactly 100, and at 212 bits it matches 100 to 52 places. The truth is 6.00000002. Assert wrong by more than 90.

### G10. Digit-loss x-ray
**Goal:** show each operation's proven bits, and name the one that lost them.
**Needs:** G5.
**How:** the evaluator records a per-node trace in interval mode, sent as a `trace` field. The window lists the tree with a bar per node.
**Tests:** at 100 bits, Exact off, `333.75*33096^6 + 77617^2*(11*77617^2*33096^2 - 33096^6 - 121*33096^4 - 2) + 5.5*33096^8 + 77617/(2*33096)` names operation 18 of 21, the `+` that adds 5.5·b^8, dropping 98 bits to 0.

### G11. Format lab
**Goal:** a grid of formats by rounding modes, one line, each cell the result and its error.
**Needs:** G2.
**Tests:** every cell against G1. Each cell replays the line (rule 14), and the smuggling test extends to it.

### G12. Bit-exact receipts
**Goal:** expression, full `EvalContext` and hex significand as a text receipt, which replays to the same bits, and a Verify action.
**Needs:** G1, H2, H6.
**How:** a receipt inlines any definition or `Ans` chain it uses. The context matters: `0.1+0.2` at 53 bits has different bits with Exact on and off.
**Tests:** round trip on random expressions; a changed bit fails Verify. **Trap:** the arithmetic uses no floating-point hardware, so receipts should replay across machines, but that's reasoned, not tested. Test it on the desktop too.

## H. Comfort

### H1. Comparisons and `if`
**Goal:** the comparison operators in the gap B3 left, a boolean result, and a lazy `if(c, a, b)`, in the float and exact backends.
**Needs:** B8.
**Tests:** tables on both backends; `if` doesn't evaluate the branch not taken.

### H2. Variables and functions
**Goal:** `x = 3`, `f(x) = x^2 + 1`, recursion.
**Needs:** H1, B11.
**How:** the worker parses, and the `result` reply gains `defined` (name, params, body text). The client stores it, and sends all definitions on each `eval`. `EvalContext` gains a definitions hash, which keys every cache; dimming ignores it, and a rerun marks "definitions changed" when it differs. Definitions bind by name at use. Each stores the Angle and Exact it was made under. Rerunning `x = 3` re-defines x, and says so. Recursion caps evaluator frames, not calls, because a call can be many frames deep.
**`Ans` in a definition:** a body that mentions `Ans`, found with the Lexer, stores that line id, as a history line does, so `x = Ans*2` keeps meaning that line, not whatever `Ans` is later. `eval` gains `lines`, one value per such line, lossless or held. A value from another target is first rebuilt by replaying its `AnsFrom` chain, adding no history line. A deleted line is a clear error.
**Ask once:** `x = 3` then `x = x + 1`. Recommended: a definition with no parameters whose body names itself takes the old body in parentheses at definition time, so x becomes `(3) + 1`, which stays text and survives a target change.
**Tests:** a definition survives a target change and rebuilds at the new precision; `5`, `x = Ans*2`, `1`, then `x` is 10, and still 10 after SetTarget; `a = Ans`, `b = Ans`, `a+b`; `x = x + 1`; the smuggling test extended; the recursion cap.
**Mutations:** a definition's cached value reused across targets; the hash ignored.

### H3. Variables panel
**Needs:** H2, C9. **Goal:** list, edit and delete definitions, and show each one's current value lazily, blanking on a target change.

### H4. Memory keys
**Needs:** H2, C9. **Goal:** M+, M−, MR, MC. M is a number, so a target change clears it, as PLAN.md's table says. AC leaves it alone. Keep its lossless value in the session; with intervals on, M holds both ends. Tests: the smuggling test extended; `(sqrt(2)+1e70)-1e70` stored in M and multiplied keeps its 7 proven digits.

### H5. Autocomplete and shortcuts
**Needs:** H2, C9. **Goal:** function and variable names as you type, with signature hints, and a shortcuts sheet. Client-side, using the Lexer and the worker's `functions` reply.

### H6. Saved sessions and export
**Needs:** H2, C9. **Goal:** save and restore history and definitions as JSON text; export as text and CSV. Saved values are lossless text, tagged with their context, and dimmed on load. A line whose value was held saves its text only, and replays on demand. With intervals on, saved values carry both ends. Extend the smuggling test.

### H7. Display settings
**Needs:** C9. **Goal:** a settings panel for notation, grouping, theme, digits shown, and B7's budgets.

### H8. Live preview
**Needs:** C9, B12.
**Goal:** a result under the entry as you type, on only at or below 1,024 bits.
**How:** previews use the soft stop only, **never the kill**, or typing would restart the worker. Drop stale ids. Skip anything the preflight says is over a small budget. A real Enter always wins.
**Tests:** a slow exact term in the entry doesn't kill the worker; Enter during a preview.

### H9. RPN mode
**Needs:** C9. **Goal:** a second input model on the same evaluator: a stack, Enter, swap, roll. A stack is numbers, so a target change clears it. The stack builds expressions as text, so it inherits recompute. Text from a long stack is a deep tree: rule 13 covers it. Extend the smuggling test.

### H10. Unit values
**Goal:** `60 km/h to m/s`: a `UnitValue` with dimensions, `to` at the loosest precedence, and exact rational factors.
**Needs:** B8.
**Ask once:** do units multiply, as above, or only convert (`3 ft to m`)? Recommended: multiply. If conversion only, H10 and H11 become one session.
**Tests:** dimensions; round trips stay exact; incompatible units are `Domain`. No network.

### H11. Unit table
**Needs:** H10. **Goal:** about 150 units and the SI prefixes, each factor an exact rational with a test, and temperature offsets handled separately. `in` means inches: F1 used `to`, so nothing clashes.

## I. Depth

Optional. The session before writes each brief out in full against the code as it stands.

- **I1 Sums, products, sequences.** `sum(k, 1, 1000, 1/k)`: exact H_1000 took 0.48 s, float 9 ms. Needs H2.
- **I2 Statistics.** Mean, deviation, regression, on a list literal. Exact where it can be. Needs B8.
- **I3 Complex arithmetic.** A `ComplexValue` with + − × ÷ and √; `i` as an identifier, by setting. Needs B8.
- **I4 Complex functions.** exp, ln, trig and powers, polar display. Needs I3, W5.
- **I5 Matrix values.** `[1,2;3,4]`: syntax, value kind, + − ×, transpose, display, lossless. Needs B8.
- **I6 Linear algebra.** Exact Bareiss determinant (40×40 took 434 ms), inverse, solve. Needs I5.
- **I7 Matrix entry.** A grid editor in the window. Needs I6, C9.
- **I8 Solver.** Sign-change proof by interval bisection: x³−x−2 took 120 ms at 256 bits, to 76 digits. Needs G4, H2.
- **I9 Numeric integration.** Tanh-sinh: 2,489 evaluations, 6.4 s at 256 bits. Labelled an estimate, not proven. Needs H2, W5.
- **I10 Plot sampling.** Worker batch request, chunks, per-point errors, Stop. No WPF. Needs H2, W5.
- **I11 Plot canvas.** Start from Studio's `PlotCanvas`; pan in `double` (rule 3's exception); 800 points took 129 ms at 64 bits. Needs I10, C9.
- **I12 Deep-zoom plot.** Past double's resolution, re-render in Natural. Needs I11.
- **I13 Gamma family.** `gamma`, `lgamma`, `beta`, non-integer `!`: Stirling with exact Bernoulli numbers, shifting and reflection, on G4's intervals, reported through G8's proven digits, since these won't be correctly rounded. Needs G7, G8.
- **I14 Error functions.** `erf`, `erfc`, the same way. Needs G7, G8.
- **I15 Identify a fraction, uncertainty input.** `0.142857142857142857` gives 1/7 by continued fractions, with denominators capped, because π gives 245850922/78256779. `1.5±0.1` reads as an `IntervalValue`, with `±` joining the Lexer's tokens. Needs B8, G4.
- **I16 Line references.** `#3 * 2`, using `#`, reserved in B2, on B11's line ids. Needs H2, C9.
- **I17 Live recalculation.** Rerunning what depends on a changed line, spreadsheet style, through a worker that can be killed. Needs I16.
- **I18 Newton division (Natural).** Overtakes restoring division near 5,000 bits: 129 ms against 470 ms at 65,536 bits, but not yet correctly rounded. Needs D2.
- **I19 Newton square root (Natural).** At 1M bits, sqrt is 140 of π's 286 s. Needs I18.
- **I20 Divide-and-conquer radix text (Natural).** Decimal text stays quadratic until this. Needs I18.
- **I21 Bit-burst (Natural).** T21 in the T plan. Needs I18, T11.
- **I22 Streaming digits, million-digit constants (Natural).** T22 in the T plan. Needs I19, I20.
