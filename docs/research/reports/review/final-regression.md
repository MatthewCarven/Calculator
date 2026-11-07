# regression

Resolved: 38

Verdict: 38 of the 48 blocker and major findings are really fixed and every renumbered reference in PLAN.md and TODO.md is right, but ten are only part fixed, both blockers among them (H2's `x = Ans*2`, and RoundedCall returning 0 for `ln(1+1e-700)`). The rewrite also added faults of its own, chiefly B3's unsatisfiable Done-when, B5's wrapping `e*n` and B10's cap vanishing under 1 GiB of free commit; none stops a session starting, but the 12 major items should go into the briefs before A1.

## 1. [major] TODO.md B3 lines 261 and 266; PLAN.md line 224; B12 tests line 396 (design-holes 6)

Problem: B3 counts the length of a left spine and refuses past 200,000, yet its Done-when says 1,000,000 terms of `1+1+…` parse. Both can't hold. PLAN's risk row says the parser refuses, and B9's test accepts either outcome. An agent will close B3 by dropping the spine from the counter, and every later walk then meets a tree 1,000,000 deep. The test design-holes 6 asked for through the cost walk isn't in B12 either.

Fix: B3 Done when: "199,999 terms of `1+1+…` parse, and 1,000,000 give a `CalcError` with a span, both on the big stack." B12 Tests: add a 199,999-term sum through the preflight walk on the compute thread.

## 2. [major] TODO.md B5 line 294 (new, in the recipe that fixed cold-start 6 and fact-check 3)

Problem: `e*n` is a plain long product, and C# is unchecked by default (the skeleton's Directory.Build.props doesn't turn checking on). Measured: for x = 2^(2^60) and n = 16, `e*n` wraps to 0 and the route returns 1.000… with flags None, where squaring through `ieee` throws OverflowException. It's reachable by typing `(2^(2^60))^16`: B7 sends the base to float, and the route guard passes because |y|·bitlen(m) is 16. That's a silent wrong answer, and B5's tests and mutations don't touch it.

Fix: Write `checked(e*n)` in the recipe, with the exception mapped to `Overflow` by ErrorMap. Add `(2^(2^60))^16` to B5's Tests and "`checked` dropped" to its Mutations.

## 3. [major] TODO.md B8 line 339; W1 line 543; PLAN.md lines 193-194 (design-holes 2, blocker)

Problem: W only grows eightfold over RoundedCall's 4 rounds, and on reaching the cap it takes the nearest conversion, which is the rounding-first answer the helper exists to prevent. Simulated as written: `ln(1+1e-600)` settles at 256 bits, but `ln(1+1e-700)` at 256 bits and `ln(1+1e-300)` at 53 bits both give 0, shown as a full-digit float, where the truth is 1e-700 and 1e-300. Those literals are inside B7's budgets, so PLAN's definition of correctly rounded is still false for them. Separately, `atanh(1-1e-200)` rounds up to exactly 1 at W = 320, raises DivideByZero on that end, and B4's policy makes that a CalcError, though the answer is finite.

Fix: B8: "After 4 rounds, if the two results are adjacent, take the nearest and stay float: that's the `log10(0.001)` case. Otherwise keep doubling, up to W = bits(n) + bits(d) + 2p + 64. An error or ∞ at one end means not settled, not an error." Add tests for `ln(1+1e-700)` at 256 bits and `atanh(1-1e-200)`.

## 4. [major] TODO.md W3 line 558; B8 line 339; W2 line 549; W5 line 571 (design-holes 2, blocker)

Problem: Two paths miss RoundedCall. W3 says to reduce exactly and "only then convert", then "Radians use RoundedCall", so a non-dyadic angle in degrees is rounded first. Simulated: sin(k/3°) at 53 bits has its last bit wrong in 463 of 2,000 cases. And B8 defines the helper for one argument, while "every W session" uses it and W2's `hypot`, `log(x, b)` and `x^y` with large q, and W5's `atan2`, take two exact arguments.

Fix: W3: "reduce exactly, then pass the reduced value through RoundedCall in degrees or gradians." B8: "With two arguments, call f on all four corners; all must agree." Add a W3 test of `sin(100/3)` in degrees against an exact oracle.

## 5. [major] TODO.md B7 line 329; B8 line 339; PLAN.md line 193 (design-holes 2, blocker)

Problem: PLAN says each operation is right for the value it receives, and in exact mode that's the exact value typed. B7 says the exact side of a mixed operation converts at the target first. Measured on random n/d combined with a float sqrt(2), rounding the ratio first differs from the once-rounded result in about 30% of cases for + × ÷, at both 53 and 256 bits (1,419 to 1,574 of 5,000). Same fault as the blocker, on a common path such as `1/3*sqrt(2)`, and it isn't covered by `RoundedCall`.

Fix: B8: "Mixed operators with a non-dyadic exact side move onto RoundedCall here." Add one random test against the rational oracle. If that's more than is wanted, reword PLAN line 193 so the promise covers function arguments only.

## 6. [major] TODO.md B10 line 366, test line 368; PLAN.md line 222 (design-holes 13)

Problem: "Free commit less 1 GiB" gives zero or wraps when free commit is at or below 1 GiB, and tiny values just above it. Measured: `0x0` gives no cap (16.9 GB available). `0xFFFFFFFFC0000000` reports TotalAvailableMemoryBytes as -1,073,741,824 and allocates freely, so also no cap. `0x100000` stops the worker starting ("GC heap initialization failed", exit -2147450743). `0x1000000` starts, then runs out inside 64 MiB. So the cap vanishes or the worker won't spawn in the low-commit state it was added for, and B10's tests don't cover it.

Fix: B10: "cap = min(setting, free commit − 1 GiB), computed in signed Int128, with a floor of 256 MiB. Below the floor, don't spawn: complete with a message saying memory is short." Make `CommitInfo` injectable and test free commit of 0.5, 1.0 and 1.1 GiB.

## 7. [major] TODO.md B9 lines 352, 355, 358; B11 lines 384-385 (design-holes 9)

Problem: The client memory test design-holes 9 called for was dropped, and no test crosses the 1 MiB line. `2^1000000` is 250,001 hex characters and the 830,000-character reply is under the limit too, so `held`, `ans` naming a held value, `ValueGone` and the replay stay unexercised until C7, which tests only `fetch`. Also `fetch` has no reply in B9's list. Nothing bounds what the worker holds, so enough held values reach the heap cap and an unrelated `1+1` reports TooBig. And 1 MiB is per value, so the client's total is still unbounded.

Fix: B9 tests: `2^5000000` (1,250,001 hex characters) replies `held`, and `Ans+1` naming it stays exact. B11 tests: after a kill, `ValueGone` replays the line; 100 lines at 2^24 bits stay under a set memory figure. Add reply `value` (id, lossless) for `fetch`. State: "the worker evicts its oldest held values past a quarter of the heap cap, and a miss is `ValueGone`."

## 8. [major] TODO.md C7 line 464; C8 line 473; G3 line 635; B11 line 379; PLAN.md line 120 (design-holes 7)

Problem: The Blank row exists only in PLAN's table. C8 is the first open view, in the first release, and neither it nor G3's inspector says it blanks; only H3 does. B11's `SetTarget` stops the main worker, and C7 puts `digits` in a second worker, so a job survives a target change and paints old digits into the open window. No test covers this.

Fix: C7: "`SetTarget`, `ClearAll` and `WipeEverything` stop the digits worker and drop its replies." C8: "a clear blanks the window and offers to recompute." G3: the same line. Add a `DigitsState` test against `FakeWorker`: a target change mid-job leaves no digits.

## 9. [major] TODO.md G9 lines 672-675; B11 line 385; H2 line 705; G2, G8, G11 (design-holes 7, dependency-order 1)

Problem: Dependency-order 1 asked the sweep to state the chain rerun, and design-holes 7 asked for the smuggling test in every multi-target or value-holding session. G9, the session design-holes 7 used as its example, has neither: it's the only multi-target brief without "(rule 14)", and a 53-bit `Ans` printing 0.99999999999999994 as "stable" is exactly what it must not do. Only H2 extends the smuggling test.

Fix: G9: "each row replays the line and its `AnsFrom` chain at that precision (rule 14)", with the test: `1/3` then `Ans*3`, Exact off, swept at 53, 106 and 212 bits, gives no row of 0.99999999999999994. Add "the smuggling test extended" to G2, G8, G11, H4, H6 and H9.

## 10. [major] TODO.md G4 lines 641, 644-645; H4 line 712; H6 line 718 (design-holes 8)

Problem: G4's test "one through M" can't be written: M arrives in H4, after G in the order, and G4 needs only B8. H4 says only "keep its lossless value", so M stays a point float, which is the 76 proven of 7 true fault. B7's lossless form has no interval tag, and G4's Files omit `Text/Lossless.cs`, so `Ans` and saved values have no way to carry both ends either.

Fix: G4 Files: add `Text/Lossless.cs` with a tag such as `v:<target>:<A lo>:<A hi>`. Keep the `Ans` test in G4. Move the M test to H4, with "when intervals are on, M holds both ends", and say the same in H6. If H runs first, G4 adds it.

## 11. [major] TODO.md H2 line 704 (design-holes 1, blocker)

Problem: Design-holes 1 said any definition mentioning `Ans` binds that line id. The brief still covers only bare `x = Ans`. Under "definitions bind by name at use", `x = Ans*2` changes value at every Enter and errors after a clear, the case the blocker named. The rest of that blocker is fixed in B11.

Fix: H2: "A definition whose body mentions `Ans` stores `AnsFrom` as a history line does, and using it replays that chain; a deleted line is a clear error." Test: `5`, `x = Ans*2`, `1`, then `x` is 10, and still 10 after SetTarget.

## 12. [major] TODO.md C3 lines 426-431 (design-holes 10)

Problem: The keyboard `=` rule inserts whenever the text before the caret is a bare name or `name(args)`. So `sqrt(2)=`, `Ans=` and `pi=` insert an `=`, and Enter then gives a syntax error, for the whole of v0.1, since assignment only arrives in H2. The keypad's `=` evaluates, so the Done-when "the keyboard and keypad agree" can't hold. Also the try-this presses `±` straight after a result, but the table's after-a-result list omits `±` and `(`, the gap design-holes 10 named.

Fix: C3: "`=` always evaluates until H2. From H2 it inserts only after a bare name that isn't `Ans` or a constant, or `name(args)` with bare-name args and a name not in the `functions` reply." Add to the table: `±` after a result gives `-Ans`; `(` after a result starts afresh.

## 13. [minor] TODO.md B8 line 340 against B6 line 306; C2 line 415 (design-holes 22)

Problem: B6 caps `Main` at about 300 digits, and B8 shows an exact integer in full up to about 100,000 bits. That's 30,103 digits, 273 ms of `ToString` in every reply (measured), and past the 9,600 characters at which WPF's stock box splits a line (report 3). Design-holes 22's cap was dropped.

Fix: B8: "An exact integer shows in full while it fits `Main` (about 1,000 bits), otherwise `E99` through `WithPrecision(1024)`, with 'exact, about N digits' and Show all."

## 14. [minor] TODO.md B6 line 309 against rule 9 line 50; PLAN.md line 197

Problem: B6 makes Aligned the default `ToString()`, which prints `DecimalDigitsFor`'s 79 digits at 256 bits. Measured: 1/3 prints …3348E-001, the noise rule 9 and PLAN say not to show.

Fix: B6: "Aligned is `E<d-1>` with d from `Digits.For(p)`: always scientific, fixed width, no noise."

## 15. [minor] TODO.md B6 lines 305 and 311

Problem: `BitsFor(d)` is required to be the smallest p with `For(p) ≥ d`, but `For` clamps to 1, so For(1) to For(4) are all 1 while the formula gives BitsFor(1) = 5. A test of "smallest such" fails at d = 1, the only failure in d = 1 to 2,000.

Fix: B6: "the smallest such for d ≥ 2".

## 16. [minor] TODO.md B9 line 349 against B4 lines 271-272; rule 1; PLAN.md lines 77-78

Problem: `EvalContext` lives in `Calc.Protocol` and carries target and mode, but B4's `Target` holds an `IeeeFormat` and sits in Engine, and `RoundingMode` is a Natural type. Protocol and Client can't see Natural, so B9 can't use either as written.

Fix: B9: "target is bits or (exponent bits, precision), and mode is Protocol's own enum; Engine maps them to `Target` and `RoundingMode`."

## 17. [minor] TODO.md B7 line 321; B9 lines 349, 351; C9 line 481; H7 line 721; G12 line 691 (dependency-order 3)

Problem: The budgets decide exact against float, so they change bits, but they're in no defined field: B9 never says what `options` holds, and dependency-order 3's "add budgets to options" was dropped. A change in C9 or H7 has no route to the worker, and a recompute or G12 receipt under different budgets replays to different bits with nothing to show why.

Fix: B9: add `budgets` to `EvalContext`, so every line and receipt records them.

## 18. [minor] TODO.md W4 lines 564-565; B9 line 352 (design-holes 22, dependency-order 3)

Problem: W4 wants a confirm before long work, but `warnings` sits on `result`, which arrives after the work. Design-holes 22's `confirm` flag was dropped, and W4 names no wire change.

Fix: W4: "`eval` gains `confirmed`. Without it, the worker replies `error` kind `NeedsConfirm` with the warning text before starting, and the window resends. Files: `Messages.cs`, `Loop.cs`."

## 19. [minor] TODO.md H2 line 704; B11 line 384; C6 line 455

Problem: H2 puts the definitions hash in `EvalContext`, while B11 and C6 dim a line when any of its context differs. Read literally, every `x = 3` dims all earlier lines, including ones that use no definition.

Fix: H2: "Dimming ignores the hash; a rerun marks 'definitions changed' when it differs."

## 20. [minor] TODO.md B7 lines 324, 331 against W2 line 550

Problem: B7 raises `Inexact` on every fallback, and a mutation enforces that. W2 calls `2^(10^12)` a float with no Inexact. Under B7 it does carry the flag, so the lamp lights on an exactly representable value.

Fix: B7: "Flags come from the conversion and the operation; the chip always says 'float, exact too large'." Reword the mutation to match.

## 21. [minor] TODO.md W3 line 559

Problem: The recommended rewrite sends exact rational multiples of π through `SinPi`, which takes a binary float, so 1/6 is rounded first. Simulated: SinPi of rounded 1/6 is 1/2 − 5.6e-17 at 53 bits TowardNegative and 1/2 + 8.6e-78 at 256 bits TowardPositive. In nearest it's 1/2 only as a rounded float. So `sin(π/6)` is never exact, while `sin(30)` in degrees is.

Fix: W3: "Rewrite q·π as `Sin(180q, Degrees)`, through the exact reduction above." Test `sin(π/6)` is exact 1/2 in every mode.

## 22. [minor] TODO.md B3 line 260 (design-holes 19)

Problem: The rule covers a number token only, so `7 % x` and `7 % pi` still give 0.07·x and 0.07π silently. Design-holes 19 named `7 % x`, and it matters once variables exist.

Fix: B3: "`%` directly followed by a primary is a Syntax error with the same 'use mod' hint." Add `7 % x` to the table.

## 23. [minor] TODO.md line 7, step 7 lines 25-26, step 8 lines 27-29

Problem: Step 8 has the session before write out any brief under 100 words. D2 (84), D3 (49), W0 (98), W5 (45) and I19 to I22 all follow a session in a Natural chat, which line 7 forbids from working in Calc. Step 7 also covers only T groups: nothing ticks A1, A2, D1, D2 or I18 to I22 in Calc's order of work, though B1, D3 and others name them in Needs.

Fix: Step 8: "A Natural session never edits Calc: the next Calc session writes its own brief as its first step, and a following Natural session's brief goes in Natural's TODO." Step 7: "The next Calc session ticks any finished Natural session here, after checking Natural's TODO."

## 24. [minor] docs/research/README.md lines 28, 31, 32; WORKLOG.md line 13

Problem: These are the only renumbering slips, and they read as current. README line 31 says `demo.cs` is for B4, B6, but exact values are now B7. Line 32 says the UI spikes are for B1, B7, B8, but the worker sessions are now B9 and B10. Line 28 says A1 moves the folder, while TODO's A1 copies it and line 7 keeps a Natural chat out of Calc. WORKLOG line 13's "B4 and B6" are the draft's numbers.

Fix: README line 31: "B4, B7, stage G". Line 32: "B1, B9, B10, stage C". Line 28: "A1 copies this folder into Natural". WORKLOG line 13: "the draft's B4 and B6".

## 25. [minor] docs/research/transcendentals/TRANSCENDENTALS-PLAN.md line 292, Amendments lines 9-16 (design-holes 12, fact-check 26)

Problem: Section 7's calculator policy still gives "Needs pi to 3,321,933 bits" and the warning threshold "ulp(x) is at least 2π". W4 corrected both, to about 3.32M bits and 2^-10 of the period, and design-holes 12 showed 2π fires too late. The Amendments correct only section 7's degrees sentence, so that text still overrides nothing and stands as live policy after A1 copies it.

Fix: Add to the Amendments: "Section 7's calculator messages are replaced by W4 in Calc's TODO: float arguments only, from ulp(x) at 2^-10 of the period, and about 3.32M bits in the cost message."

## 26. [minor] TODO.md rule 1 lines 36-37; B1 lines 234-235; C1 line 408

Problem: Rule 1 lists `Calc.Cli` and says `LayeringTests` proves it. B1's test checks Syntax, Protocol and Client, and C1 adds App, so nothing checks Cli.

Fix: B1: add `Calc.Cli` to the assemblies `LayeringTests` reads.
