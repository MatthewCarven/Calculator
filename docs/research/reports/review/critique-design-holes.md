# design-holes

Verdict: Two headline promises fail as written: the clear is not lossless once a line's text mentions `Ans`, which every keypad chain does, and "correctly rounded" is false for exact arguments, with wrong shown digits at the default 256 bits. Most of the rest comes from three gaps: no single evaluation context, no lossless form for exact values, and a contamination guard that lives only in the client. Probes are in C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\critique\design-holes\.

## 1. [blocker] TODO.md B9 (lines 347-348), C3 (line 400), C4 (line 410), H1 (line 625); PLAN.md lines 89-90, 113-114

Problem: A line's text is not self-contained if it mentions `Ans`, and the keypad writes `Ans` into every chained line. `2 + 3 =` then `× 4 =` stores `Ans×4` (20). After a target change, `:rerun` or "Recompute last line" gives "no answer yet". After `1+1` at the new target, the same rerun silently gives 8, not 20, with no warning. A saved session has the same fault. H1 only handles bare `x = Ans`: `x = Ans*2` changes value at every Enter and errors after a clear. PLAN's "Entry line, no precision in it" is false for `Ans×4`. Report 5 row 2 said "stored `Ans` becomes a line reference", but the briefs lost that. B9's try-this reruns line 1, which has no `Ans`, so it passes.

Fix: B9: give HistoryLine an `AnsFrom` line id, set when the source mentions `Ans`. RecomputeAsync replays the chain oldest first at the current target, feeding each step's result in as the next step's `ans`, and adds one new line. A deleted link gives a clear error. The entry keeps the same link. H1: any definition mentioning `Ans` binds that line id. Change B9's try-this to rerun a keypad chain, and add a test of rerun after another line. Do this in B9, because C4, C6, H1, H5 and G10 all inherit the line format.

## 2. [blocker] TODO.md W1 (lines 520-521, "Exact arguments convert at the target first"), B6 (line 305), W0 and W3; PLAN.md line 32

Problem: The target-first conversion throws away a value the calculator holds exactly, so the "correctly rounded" claim and the "float, 76 digits" chip are false. Exact-fraction oracle, default 256 bits: `ln(1.0000001)` has its last 7 of 76 shown digits wrong (8.09e6 ulp); `exp(1000.1)` its last 2 (367 ulp). `ln(1+1e-80)` gives 0, because the argument rounds to exactly 1; the truth is 1e-80. At 53 bits: `exp(100.1)` shows ...581E+43, truth ...582E+43; `ln(1.0000001)` has 6 of 15 digits wrong; `sin(1/3)` is 1 ulp off. Random k/1000 at 53 bits: exp differs in the last bit 318/400, in a shown digit 56/400; sin 305/400 and 117/400. It reaches B6 too: `sqrt(1/7)` at 53 bits is 0.3779644730092272, correct 0.37796447300922725 (6,318 of 48,677 ratios wrong). `e^1000` through `Pow` of the rounded constant differs from `exp(1000)` in the last 3 of 15 digits at 53 bits, and in the last digit at 256.

Fix: Add one helper in ExactArithmetic, used by every W session and by `sqrt` on ratios. For an exact argument the target can't hold, call f at the target on the argument rounded down and up at W = p + 64 + max(0, top(x)), doubling W until both results agree. Cap it at about 4 rounds, because `log10(0.001)` in a directed mode never settles; then use the nearest conversion and stay float. W3: rewrite `e^y` to `Exp(y)` when the base is the constant e. Format mode and Exact off keep hardware semantics. Until W1 lands, reword PLAN line 32 to "correctly rounded for the value it receives".

## 3. [major] TODO.md B5 (line 278), B6, B7 (line 316), B9 (lines 345-348), C7

Problem: Lossless is defined only for floats ("`ToString("A")` plus the precision"). B6 adds IntValue and RatioValue and never says how they cross the wire, and `Ans` crosses as lossless text. ApInt has no "A" format (report 1: D, G, R, X, x, B). If B7 sends a float, `1/3` then `Ans*3` stops being exact, which is the first thing exact-first promises. Nothing catches it: B6's tests are engine-only and B9 uses FakeWorker. Decimal text for ints would cost 19.8 s at 1 Mbit in every reply. Show all on a ratio is undefined. Copy lossless gives `0X1.5555555555555P-2`, and B2 lists only lower-case `0x…p`, so it may not paste back.

Fix: B6: define a tagged lossless form per kind, e.g. `i:<sign><hex>`, `r:<sign><hex>/<hex>`, `f:<target>:<A text>`, with hex by rule 6 or `ToMagnitudeBytes`. B7 real-exe tests: `1/3` then `Ans*3` gives exact 1; `2^1000000` then `Ans+1` stays exact and quick. C7: state what Show all means for a ratio. B2: accept `0X` and `P`.

## 4. [major] TODO.md B7 (line 316), B9 (lines 345-347, 352), C6 (line 432), H1 (line 625), W4, G10 (line 618), H5

Problem: Only target and mode travel with a line, so recompute silently changes meaning. `sin(30)` in degrees, then Angle to rad: rerun gives -0.988, undimmed. A G10 receipt (expression, target, mode, hex) doesn't replay: `0.1+0.2` at 53 bits in nearest has significand ...3333 with Exact on and ...3334 with Exact off. B9 has no SetExact operation and the clear table is silent on Exact and Angle. Toggling Exact with a ratio `Ans` hands a RatioValue to FloatArithmetic, which B6 says is used directly, giving `Internal`. Changed definitions are unhandled: `x=3`, `x+1`, `x=5`, rerun gives 6 with no sign. `x = sin(30)` defined in degrees silently becomes -0.988 after an Angle change, because it is rebuilt from text. `y = x` (bind now or later) and rerunning a line `x = 3` (does it reassign?) are undefined. H1's cache mutation only names target as a key.

Fix: B7: one `EvalContext` record in Calc.Protocol (target, mode, exact, angle, policy, later word size and definitions hash), carried on every request and stored on every HistoryLine. It keys every worker cache and is printed in receipts. Dim on any difference. Rerun uses the line's own context with only the target replaced, and marks "definitions changed" when the hash differs. Add SetExact to B9: no clear, and `Ans` converts at use. H1: state bind-by-name, store Angle with a definition, and make rerunning an assignment explicit.

## 5. [major] TODO.md B1 and B2 (line 209), rule 1 (line 29), H1 (line 625), H4 (line 635); PLAN.md lines 72-79

Problem: The Lexer and Parser sit in Calc.Engine, which the Client may not reference, so the Client cannot tokenise. Yet it must know whether a line mentions `Ans` (the rerun chain), find the last operand for keypad keys, work out which definitions a request needs (H1 says "sends those a request needs"), and find names for autocomplete. B2 already notes no Natural call appears in this code, so nothing forces it into Engine.

Fix: B1: add a Natural-free `Calc.Syntax` project holding Token, Lexer and CalcError, and Parser, Ast and Printer if wanted. Engine and Client both reference it; LayeringTests are unchanged. Add the row to PLAN's project table. It costs nothing at B1 and a refactor after C3.

## 6. [major] TODO.md B3 (line 229), B7 (line 319), B10 (line 360), H1 (line 625), H7; PLAN.md line 86

Problem: The 500 cap counts nesting, but left-associative chains are flat in the text and deep in the tree. A planned-shape tree walk on the B7 compute thread (default stack, BelowNormal, Release) died with uncatchable 0xC00000FD at about 6,350 terms of `1+1+…`, only 12,700 characters. The cost walk died at about 3,430 terms, the printer at about 5,350, and generated record Equals at about 8,000. In the parser, `---…1` died at about 3,850 and `2^2^…` at about 7,450; neither is paren nesting. So pasting 3,500 numbers joined by `+` kills the worker with no span and no CalcError. The capped-length fuzz never reaches it. H7 builds left-deep text. H1's recursion cap multiplies with body depth (cap 1,000 × 10 nodes overflows).

Fix: B7: create the compute thread with an explicit stack, `new Thread(body, 256 << 20)`; a small walker ran 1,000,000 terms on that in 388 ms, and died on the default stack. B3: one depth counter over every recursion (parens, unary, `^`) plus left-spine length, refusing with a span past a set limit (e.g. 200,000). Parse on the compute thread. H1: cap evaluator frames, not calls. Add a real-worker test: a 1,000,000-term sum gives an answer or a clean error, including through B10's walk.

## 7. [major] TODO.md B9 (lines 346-348), B7 (line 316), G2 (line 581), G6 (line 602), G7 (line 607), G9 (line 615), I13, C7, H2

Problem: The guard is one comparison in Session, with one target per request. G2's truth row, G6, G7 and G9 run one request at many targets inside the worker, where nothing checks `ans`. Probe: a 53-bit `Ans`, then `Ans*3`, gives 0.9999999999999999444888488 at 106, 212 and 424 bits. That is PLAN's own contamination example, and G7 would print it as "stable". I13's `#3` value at an old target is unspecified. AC and Wipe are not covered either: only SetTarget stops work and drops replies, so AC during a run gets `Ans`, the result and a history line back from the late reply. Open views are not in the clear table: the digits window, Inspector, Variables panel values, truth row and Programmer rows keep showing old numbers.

Fix: Tag `ans` with its target on the wire, and have the Evaluator refuse it at any evaluation target that differs. Multi-target features replay the `AnsFrom` chain or refuse. Every clear (SetTarget, ClearAll, WipeEverything) stops running work and invalidates ids. Add a clear-table row: every open view of a computed value blanks or recomputes. Extend the smuggling test to G2, G6, G7, G9, H3, H5, H7 and I13.

## 8. [major] TODO.md G4 (line 591), B7 (line 316), H3, H7; PLAN.md lines 151-152

Problem: Proven digits can state a falsehood. `Ans`, M and saved values cross the wire as one float, with no enclosure. Probe at 256 bits: `(sqrt(2)+1e70)-1e70` has an enclosure proving 7 digits. `Ans*3` with `Ans` as a point prints alike at both ends to all 76 digits, 4.24264061450958251953125, while the truth is 4.24264068711928. That is 76 proven and 7 true. Carrying the enclosure gives 7. Keypad chains always go through `Ans`, so this is the normal path. The plan's whole selling point here is that "proven" never lies.

Fix: G4: when intervals are on, `Ans`, M and saved values carry lo and hi in their lossless form. A plain float operand in an interval evaluation is treated as unproven from that point, never as a point interval. Add the probe above as a test, plus one through M.

## 9. [major] TODO.md B7 (line 317), B9 (line 345), C4 (line 408), C6; PLAN.md lines 86-87, 209

Problem: Every reply carries `lossless`, every history line stores it, and `ans` sends it back on every request. The App has no heap cap. Measured `A` text: 4,194,311 characters at 2^24 bits. By the same ratio, 2^28 bits (where `1/3` takes 9.5 s) is 67 M characters, 134 MB of UTF-16 per line, and 30 such lines is 4 GB. C4 accepts up to 2^30. So memory runs out in the client, which loses history, contradicting PLAN's "running out kills only the worker". Exact integers up to 2^24 bits push 4 MB each way per Enter.

Fix: B7: send `lossless` only up to a limit (e.g. 1 MiB). Above it, reply `held: true` and keep the value in the worker, keyed by line id; the client stores nothing. After a kill, the F1 chain rebuilds it or `Ans` reports gone. Send `ans` only when the source mentions it. Add a client test: 100 lines at 2^24 bits stay under a set memory figure.

## 10. [major] TODO.md C3 (lines 399-402)

Problem: "`±` wraps the entry in `-( )`" makes `2 + 3 ± =` give -5 where every keypad gives -1. "Removes that wrapper" by text breaks `-(1)+(2)`, leaving `1)+(2`. "Keys insert text at the caret" fails for `√` and `1/x` after a number: `9 √` becomes `9√`, a syntax error; `7 + 2 1/x` is undefined. The brief doesn't say which keys count as "operator keys" after a result (`±`, `1/x`, `x²`, `%`, `√`, `(`). "Keyboard and keypad agree" makes keyboard `=` evaluate, which later stops H1's `x = 3` being typed. This is the Standard keypad of the first release.

Fix: C3: write the table. `±` toggles the sign of the last operand (`2+(-3)`). `1/x`, `√`, `x²` and `%` apply to the last operand, or to `Ans` straight after a result, so `9 √` gives `√9` and `7 + 2 1/x` gives `7+1/2`. Keyboard `=` evaluates unless the text before the caret is a bare name or `name(args)`, when it inserts. Requires tokens in the Client (Calc.Syntax).

## 11. [major] TODO.md W1 (lines 520-521), B4 (line 255), W3, B10 (line 364)

Problem: The promotion rule is unsound as written. Probe: `ieee.Divide(10^80+1, 10^80)` at 256 bits gives exactly 1 with Inexact, and a function call on that 1 raises nothing. B4 checks "the flag change after each operation", so a per-call delta sees no Inexact and `ln(1+1e-80)` is labelled exact 0, `log2(1+1e-80)` likewise, and `sin(30 + 1e-90)` in degrees exact 1/2. Those are exact chips on wrong numbers. The rule also has no size check. `exp2(10^12)`, and after W3 `2^(10^12)` (B10: "falls to float, which is fine"), are floats with no Inexact from exact input, so promotion runs `(ApInt)x`, which throws past exponent 2^36 and is OOM below it (report 1). A good float answer becomes an error. `exp2(-20000)` makes a ratio past the 2^14-bit budget.

Fix: Rewrite W1's rule: promote only if (1) every argument converted with no Inexact, checked on that conversion's own delta, (2) the call's own delta has no Inexact, and (3) the exact value fits B6's budgets. Otherwise stay float. Add these counter-examples as tests. In formats and with Exact off the rule never runs.

## 12. [major] TODO.md W4 (lines 532-533); TRANSCENDENTALS-PLAN.md section 7 (line 282); PLAN.md line 212

Problem: "Degrees need neither warning" is false. The reduction is exact on the stored x, not the typed one. `sin(1e23)` degrees at 53 bits: typed mod 360 is 280, stored is 32, so sin is +0.530 against -0.985. At the default 256 bits, `1e120`: stored mod 360 is 120, so +0.866 against -0.985. No warning fires, and the input was an exact integer the calculator holds. The radian threshold ulp(x) ≥ 2π is also too late: `sin(10^16+1)` at 53 bits stores 10^16, ulp 2, no warning, 0.7797 against the true -0.1056.

Fix: W4: for an exact argument in degrees or gradians, reduce it exactly mod 360 or 400 in ExactArithmetic before converting. Radians: use the exact-argument helper, which passes a long x. Keep a warning only for float arguments, firing when ulp(x) passes a small fraction of the period (e.g. 2^-10 of it), and say how many digits survive. Correct T-plan section 7's claim. Test `sin(1e120)` degrees at 256 bits.

## 13. [major] TODO.md B8 (line 332), C8 (line 454); PLAN.md lines 209-210

Problem: `DOTNET_GCHeapHardLimit` is read as hex. Probe: setting `4294967296` gave a heap limit of 266 GiB, which is no cap; `100000000` gave 4 GiB. The brief doesn't say so, and `limit.ToString()` is the natural code. A fixed 4 GiB also ignores commit. The notebook has a limit of 21.1 GB with about 8 GB held by Claude and its VM, Resource-Exhaustion 2004 killing other programs, and report 1 saw free RAM at 486 MB. PLAN's row "Same heap cap, and the worker runs at below-normal priority" does nothing for memory.

Fix: B8: format the limit with `ToString("X")`. Test: a child reports `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` equal to the setting. At spawn, cap = min(setting, GlobalMemoryStatusEx ullAvailPageFile − 1 GiB). B10 compares estimated bytes with live free commit too. Fix PLAN's risk row.

## 14. [major] TODO.md G4 (line 593), G7 (line 607), G8 (line 611), W6 (line 540), G6; PLAN.md lines 151-161, question 2

Problem: Exact-first is on by default and Rump's expression is rational: every part fits in 123 bits and the result is exact -54767/66192 at any precision. So "0 proven at 121 bits", the six wrong rows, and "operation 16 of 19" never appear in the default mode, and W6's try-this shows nothing. B6 itself tests Muller exact. No brief says which arithmetic the truth tools use, or how exact and interval combine, since ExactArithmetic falls back only to FloatArithmetic. Muller also can't be typed: G7 needs only G4 and C8, there is no iteration before I1, and a plain recursive u(100) under H1 is about 1.4e38 calls.

Fix: G4: state that truth tools run with Exact off, and the panel says so. On an exact result show "exact: nothing to prove" with a "run as floats" action. Define the exact-to-interval fallback. G7: add I1 to Needs, or drive Muller by a tree built in the test. Say this in question 2 so Matthew decides with it in view.

## 15. [major] TODO.md F3 (lines 557-559), F2

Problem: F3 says word size and signedness "are part of the target, so changing them clears". Its own try-this is "-1 at 8 bits, then flip to unsigned": under that rule the value is gone and nothing shows 255. Contamination is no reason here, because integers are exact. Is moving from Standard to Programmer a target change? If so, a tab click wipes `Ans` and dims all history. `A`–`F` keys: `FF` lexes as an identifier, and `10` in HEX means 16, so line text no longer says what it means and recompute depends on radix. A bit click has no text form.

Fix: F3: a word size or sign change keeps the bits and reinterprets them, truncating to the new width, and does not clear. Treat tab change as a context change with a stated rule. Keys insert prefixed literals (`0xFF`) so line text is radix-free. A bit click writes a literal into the entry as a new line.

## 16. [major] TODO.md B6 (lines 299-300), B10 (line 366), C5 (line 426), C7 (line 448)

Problem: Budget numbers contradict each other. "Integers to 2^24 bits" against "one operation stays under about 100 ms": two 2^23-bit operands, multiplied within budget, take about 5 hours from report 2's 274 s at 1 Mbit. Tuning to 100 ms puts operands near 25,000 bits, so C7's try-this `2^1000000` falls to float and Show all has 76 digits. B10 says slow is fine because Stop handles it, while B6 falls back to avoid slow. C5's try-this `3^(3^20)` is over any budget: measured 6 ms power and 19 ms text on the float path, so there is nothing to Stop.

Fix: Decide once: the budget is a size limit only (2^24 bits), the 100 ms line goes, B10 shows the estimate and Stop handles slow. Change C5's try-this to something that runs long, e.g. `3^5000000` exact, or `1/3` at 2^28 bits (9.5 s).

## 17. [minor] TODO.md B4 (lines 250-253)

Problem: The bounds themselves held: 40,000 cases against an exact oracle, negative bases and exponents, all modes, both routes, 0 wrong, and exact cases settled in round 1. The defect is "round once through the public constructor": it takes a precision, not a format, and raises no flags. Probe: `10^400` at Binary64 gives finite 1E+400 with no flag; `ieee.Convert` gives ∞ with Inexact and Overflow. `3^200` at 256 bits raises no Inexact, so a later `Inexact` test is misled. Also unhandled: ∞ or NaN bases (`Significand` throws InvalidOperationException) and base 0 with negative y (`ApInt.Pow(0,-1)` throws ArgumentOutOfRange).

Fix: Route 1: build the exact value at MaxPrecision and pass it through `ieee.Convert`, or `ieee.Divide(1, exact)` for negative y. Check specials first, with T10's table, in `Ieee` policy. Check base 0 with negative y before the exact route.

## 18. [minor] TODO.md C4 (line 412), rule 9

Problem: `(p−1)·1292913986 >> 32` is wrong from p = 70,778, and for 6.1% of random p up to 2^30 (the constant is 1.1e-10 short per bit). The brief's own random test will fail. Separately, p ≤ 4 gives d = 0, and `G0` is Natural's Shortest: 1/3 at 4 bits prints 0.34, labelled 0 digits.

Fix: Use `(int)(((UInt128)(ulong)(p - 1) * 0x4D104D427DE7FBCCUL) >> 64)`, which was right at the 2,755 worst cases up to 2^30. Use d = max(1, …).

## 19. [minor] TODO.md B3 (lines 228, 231)

Problem: B3 lists implicit products but doesn't bar a number from starting one, so a Pratt rule of "next token starts a primary" gives silent answers: `7 % 3` (a programmer's mod) is 0.21, `7 % x` is 0.07x (x is on the list), `1 000 000` is 0, and `1.2.3` is 0.36. `2√3`, which the keypad produces, isn't on the list.

Fix: State that a number token never starts an implicit product: those inputs are Syntax errors, and `7 % 3` hints "use mod". Add `2√3` to the table.

## 20. [minor] TODO.md B4 (line 250), B6 (lines 296, 304), W3 (line 528)

Problem: Power edges are undefined. `(-8)^(1/3)`: W3's exact root first doesn't say -2 or Domain, and T10 gives NaN. `2^(1/3)` goes through `Pow` with a rounded exponent while `root(2,3)` uses RootN, and `8^(2/3)` isn't covered. B4 covers y only if it fits a long, so `1^(10^30)`, `(-1)^(10^30)` and `0^(10^30)` have no route. `1^(2^40)` is estimated at 2^40 bits and falls to float with Inexact, though it is exactly 1.

Fix: B6: settle base 0 and ±1 for any integer y before the budget check. Past a long with |x| > 1, Overflow. W3: an exact ratio exponent p/q with small q goes through RootN and an integer power. Ask Matthew once about a negative base with odd q.

## 21. [minor] TODO.md B4 (line 249), B6 (lines 297, 303-304), F4

Problem: Probe: `0/0` and `5 mod 0` are Invalid (so `Domain`) on the float path and DivideByZeroException on the exact path, so the error changes with the Exact switch. B6 only covers `1/0`. Integer operations are unusable with Exact off or in a format: B4's "`Domain` until B6" stays true because B6's off switch uses FloatArithmetic, so `5 & 3`, `gcd(12,18)` and `5!` fail. B6's stay-exact list omits `abs`, `sign`, `max` and `min`, so `abs(-1/3)*3` is a float.

Fix: B6: a table giving one error kind per case on both paths. FloatArithmetic accepts an integer-valued float for bit operators and F4 functions by exact conversion, with a size check. Add the four functions to B6's list.

## 22. [minor] TODO.md B5 (line 270), B6 (lines 301-302), B7 (line 317), C2 (line 389), C6 (lines 433-434), W1, W4; T12

Problem: Later sessions assume things B7's wire lacks. `Preview` is at most about 100 digits (B5), yet 30,103 digits for an exact integer (B6), while C6 says rows never hold full digits, and report 3 says stock boxes split at 9,600 characters. C6's aligned view needs all digits and the client can't format. C2's "understood as" has no field. W4's "continue?" has no warning reply or confirm. T12 lists no `ieee.Sin(x, unit)`, yet W4's promotion needs flags. W1 says `sin(30°)` is exact, but at Angle rad `30°` evaluates to a float, so it needs a tree rewrite or an angle value.

Fix: B7: separate `short` and `full` text, with `full` capped under 9,600 characters, plus `understood` and `warning` fields. Add a `reformat` request for aligned view, or send that text too. W4: a `confirm` flag. T12: add context overloads with a unit. W4: define `°`.

## 23. [minor] TODO.md W2 (line 525), W4 (line 532)

Problem: Two things any user tries first are undefined. One-argument `log(100)`: W2 lists only `log(x, b)`, yet the keypad has a `log` key, and the answer is 2 or 4.6. `sin(π)` prints about 1e-77 at 256 bits, while `sin(30°)` is advertised as exact.

Fix: Add both to the ask-once rule. Recommended: `log(x)` is base 10. `sin` of an exact rational multiple of π goes through `SinPi`, if Matthew wants 0.

## 24. [minor] TODO.md C5 (line 423), C7 (line 442), B7 (line 319)

Problem: The `digits` job shares the one compute thread and C5 says Enter while busy does nothing. Show all at 1 Mbit takes about 20 s and C7 expects minutes past that, so opening a long number blocks the calculator. Stop then kills the worker, and any cached work with it.

Fix: Run `digits` jobs in a second worker process from the same exe, so evaluation stays free and Stop on either is independent.
