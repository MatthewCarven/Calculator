# cold-start

Verdict: No session is unstartable, but B6, B9 and W4 tell a cold agent to build silent wrong answers or contradictory limits, and B4 and T12 point at APIs that can't deliver the flags those briefs rely on. The headline truth-tool numbers only reproduce with Exact off, which no brief says; B3 and B10 stood up, and C7's only gap is the missing reply covered under B7.

## 1. [major] Calc/TODO.md B9 lines 347-348; C4 line 410; PLAN.md line 121; H1 line 625

Problem: RecomputeAsync 'reruns a line's text', but nothing says what happens when that text contains Ans. The Ans rule sends whatever Ans is current, or 'no answer yet'. Flow: `1/3`, `2`, `Ans*3` (=6), `:bits 1024`, `:rerun 1`, `:rerun 3` gives 1, a silent wrong number at the right precision. The plain flow `1/3`, `Ans*3`, change precision, click 'Recompute last line' (C4, the feature he asked for by name) just errors. Report 5 row 2 said 'stored Ans becomes a line reference'; that line was lost from the brief. H1's `x = Ans` inherits the same gap.

Fix: Add to B9: when a source uses Ans, the line stores AnsLine (the id of the line Ans came from). The client finds it by a whole-word scan, or the worker's `result` reports usedAns. RecomputeAsync first recomputes that chain at the current target and passes its result as `ans`; a deleted line gives a clear error. The test: the three-line flow above must give 6. H1's `x = Ans` reuses the same chain.

## 2. [major] Calc/TODO.md B6 lines 292-300; knock-on W4, C7, F4, B10 line 366

Problem: The budget contradicts itself: 'integers to 2^24 bits' and 'one operation stays under about 100 ms' differ by about 10^5. Measured ApInt multiply: 16,384² bits 58 ms, 32,768² 229 ms, 131,072² 3.7 s, so 2^23² is about 4 hours by ×4 per doubling. `1e1000000` is 3.3 Mbit, under budget, so the literal is built exactly: Pow(10,100000) took 4.8 s, quadratic, about 8 minutes at 10^6, against 2 ms for the float parse. That is the plan's own sin(1e1000000) case, so W4's warning can't come first. Tuning to 100 ms kills C7's try-this `2^1000000`. B10 says slow is fine because Stop handles it, while B6 falls to float for time.

Fix: Replace the Budgets paragraph with two limits, as settings. A size cap of 2^24 bits (memory). A work cap checked from bit lengths before computing: × over bits(a)·bits(b) > 2^28 (58 ms); ^ by the same test on its last squaring; 2^n and shifts exempt (Pow(2,10^6) took 0.9 ms). Over either limit goes to float. State that `1e1000000` goes float and `2^1000000` stays exact. Ask Matthew once, with the preview: `3^100000` exact in 2.3 s, or float at once.

## 3. [major] Calc/TODO.md W4 line 533; TRANSCENDENTALS-PLAN.md section 7 line 282

Problem: 'Degrees need neither warning' is false for the second warning. The π-cost warning isn't needed, but rounding x happens in any unit. Per W1, exact arguments convert at the target first, so stored x isn't typed x. Checked with exact integers: sin(10^23 degrees) is -0.9848 (10^n mod 360 = 280), but the 53-bit stored value gives mod 360 = 32, sin 0.5299. At the default 256 bits, 10^120 gives 120°, sin 0.8660. So the brief tells the agent to drop the warning on a silent wrong answer, in the mode the plan calls the daily one.

Fix: Rewrite: degrees and gradians skip the π warning. For an exact argument, reduce exactly first (ApInt or ratio mod 360, or 400) and only then convert, so sin(1e120) in degrees is right at any precision. For a float argument, warn when ulp(x) ≥ 360 (400). Test: sin(10^23) at 53 bits in degrees is sin(280°).

## 4. [major] TRANSCENDENTALS-PLAN.md section 10 line 318, T12 line 417; Calc/TODO.md W1 line 520, W4 line 534

Problem: W1's rule promotes a result to exact only when the Inexact flag is clear, and W4's test needs `sin(30)` in degrees exact 1/2. Rule 2 limits Calc to the public API. The plan promises `Sin(x, AngleUnit unit, precision, mode)` only: no flags channel, no format overload, and no `ieee.Sin(x, unit)`. Section 10's `ieee.Sin(x)` has no unit, and degrees can't go through `ieee.SinPi` because x/180 isn't dyadic. W4 would have to stop and go back to Natural, or compare up and down roundings.

Fix: Add to T12's goal, and T14's for the inverses: `Sin/Cos/Tan(x, unit, format, mode)` and `ieee.Sin/Cos/Tan(x, unit)`, raising Inexact as the rest do, with a test that sin(30, Degrees) raises no Inexact in the context.

## 5. [major] Calc/TODO.md G4 lines 591-593; G7 line 607, G8 line 611; PLAN.md extras 1, 2, 5

Problem: The numbers quoted hold only with Exact off, and no brief says so. With Exact on (default, question 2) Rump is an exact ratio, so 'Rump gives 0 at 121 bits' comes out as an exact answer shown beside '0 proven', and G7's 'six wrong agreeing rows' are all right. Other gaps: 'digits both ends print alike' has two readings (demo.cs IvB.Proven rounds to n digits; common prefix differs: at 128 bits I measured 37 against 35). The √ of an interval reaching below 0 is undefined: under Mathematical policy Invalid becomes a Domain error, though the main result is fine (demo's NaN just gives 0). G4's Files are Engine only, so nobody owns the toggle, the protocol field or the display.

Fix: Add to G4: an exact result is fully proven and runs no interval. The interval backend sits under ExactArithmetic's fallback. The Rump and Muller tests, and PLAN's demos, say 'Exact off'. Proven means demo.cs lines 95-102, capped at rule 9. An interval partly outside √'s domain is unbounded, 0 proven, never an error. List the eval option, the reply field and the window row as G4's work, or add a G4b needing C8.

## 6. [major] Calc/TODO.md B4 line 250

Problem: 'Round once through the public constructor' misleads twice. The constructor has no flags channel, so `^` never raises Inexact, though the goal is 'a value and flags'. It takes a precision, so at a format target it ignores the range: 3^1000 'at binary64' measured finite 1.32E+477 with no flag, where `ieee.Convert` gives ∞, Inexact|Overflow. It also can't express negative y on this route, since 'invert the bounds' is the other route's. B4's oracles are bit-target and the hardware test must exclude ^ because Math.Pow isn't correctly rounded, so this passes B4 and surfaces at G1 or G2.

Fix: Replace with: `ieee.Convert(new ApFloat(ApInt.Pow(m, n), e*n, ApFloat.MaxPrecision))`, which rounds once, into a format too, and raises flags (verified). Negative y: `ieee.Divide(1, thatExactValue)`. If the exact product would pass 2^30 bits, use the bounds route. The bounds route takes both ends through `ieee.Convert` before comparing, as notes.txt line 38 says. Add one format-target test: 3^1000 at binary64 is ∞ with Overflow.

## 7. [major] Calc/TODO.md W4 line 532; W1 line 520

Problem: Postfix `°` is named but given no meaning. W1 promises `sin(30°)` is exact 1/2, but if `°` is an ordinary postfix multiplying by π/180 the argument is an inexact radian value, so no Inexact-free result exists and T12's exact table is bypassed. `sin(30°+15°)`, `30°` on its own, and `°` under Angle = rad are undefined. History lines also store target and mode but not Angle (B9, C6), so recomputing `sin(30)` after switching rad and deg silently changes the answer.

Fix: Define it in W4. Recommended: `°` directly on a trig argument's whole expression calls Sin(x, Degrees) whatever Angle says. Anywhere else it means x·π/180 as a float. Show the understood form. Add Angle to the eval request, HistoryLine and RecomputeAsync, and dim a line whose Angle differs.

## 8. [minor] Calc/TODO.md A2 item 1 lines 175-179; Natural src/Natural/ApFloat.Ieee.cs lines 178, 236

Problem: 'Check Top where a value is made' misses two copies of the sum: RoundToFormat and RangeOf compute their own unchecked `top`. I applied the check in the private constructor to a copy: the 682 tests pass and Parse is fixed, but `Multiply(3·2^(2^62), 3·2^(2^62-1), Binary64)` still returns 0 with Inexact|Underflow where ∞ with Overflow is right. Calc can't reach it (format operands stay in range), but the public API can, and A2's tests and mutations don't cover it.

Fix: Add to item 1: make `top` in those two lines Int128 or saturating, so the overflow branch runs, and add that product as a test, expected ∞ with Overflow, plus a mutation restoring the unchecked sum.

## 9. [minor] Calc/TODO.md A2 lines 178, 184; natural-api/notes.txt lines 56-57

Problem: 'The two values at the very top that parse but won't print' leads nowhere: only notes.txt and report 1 use the phrase, and neither names them. Measured, it is a band: every value with Top ≥ 2^63-2 (from 5e2776511644261678565 up) throws OverflowException in E, R and default, because `checked(v.Top + 2 + ...)` overflows (ApFloat.Text.cs 472). That includes the brief's 'last sound exponent' 1e2776511644261678566, at 53, 64, 256 and 1024 bits, before and after the fix. `A` still prints. The tests have no expected outcome.

Fix: Replace the phrase with that band and state the outcome. Recommended: leave printing throwing, pin it with a test, call 1e2776511644261678566 'the last that parses', and let ErrorMap show Overflow.

## 10. [minor] Calc/TODO.md C4 line 412; B5 line 271 and B9 line 350 use it earlier

Problem: C4's own test fails as written. `(p−1)·1292913986 >> 32` isn't the exact formula: the constant truncates log10(2)·2^32 = 1292913986.49. Wrong at p = 70,778 first, and on 6% of random p up to 2^30 (12,256 of 200,000). All presets are right, so the failure only appears at the random step. Rounding the constant up is also wrong (6.4%). Digits.cs also arrives at C4, though B5 needs rule 9 in the worker and B9's `:digits 100` needs the reverse sum in Client.

Fix: Use `(long)(((UInt128)(p-1) * 5553023288523357132UL) >> 64)`: 0 wrong of 2,000,000 random p and at every near-convergent below 2^30. Do digits to bits the same way. Move Calc.Protocol/Digits.cs and its test to B5.

## 11. [minor] Calc/TODO.md B5 line 271 and test line 284; B6 line 302

Problem: 'Under 40 ms at any size' holds for 1/3 only. E99 measured: random 1,048,576-bit significand with exponent ≥ -524288 about 330 ms (exponent -1048576 about 56 ms; 1/3 36 ms), so B5's 50 ms test fails on such operands. B6's exact integers via (ApFloat)x: 31 ms at 100 kbit, 314 ms at 1 Mbit, 1.2 s at 4 Mbit, 6.5 s at 2^24 bits. WithPrecision(1024) first took 3 ms, and in all 8 comparisons the text matched.

Fix: Both briefs: preview through `x.WithPrecision(1024).ToString("E99")`, or about 4·d bits. Test the time on random significands with positive exponents, not 1/3.

## 12. [minor] Calc/TODO.md B5 line 278, B6 line 301 and 309, B7 lines 315-321, B10 line 363, C2 line 389, C7 line 441

Problem: The protocol leaves gaps each later session must invent. B5 defines Lossless for floats only, and B6 adds int and ratio kinds without one, so `ans` and `digits` can't carry an exact 1/3. `digits` is listed in B7 and claimed by C7, with no reply type (`result` holds a preview). Nothing says how a soft stop is acknowledged, though B8's Stop needs it and the error kinds have no Stopped. `progress` has no estimate field for B10. `result` has no 'understood as' for C2 and drops B5's Digits; B5's `Width` is defined nowhere. B6's Done-when needs real `--eval`, which arrives in B7.

Fix: In B7: define lossless as tagged text (`int:`, `ratio:n/d`, `float:p:A-text`). Add reply `digits (id, text)`, implemented by C7. Add error kind `Stopped`. Give `progress` (id, done, estimateSeconds?). Add `understood` and `digits` to `result`, and drop `Width` or define it. In B6, either wire `--eval` or change Done-when to test Evaluator plus ResultText.

## 13. [minor] Calc/TODO.md B9 lines 345-346, 350; C4 line 409; PLAN.md lines 107-119

Problem: The Exact toggle has no rule. B9 has `:exact off` and an exact flag but no SetExact, and PLAN's clear table covers precision, format and mode only. The agent must guess whether it clears, and what happens when an exact `Ans` meets B6's 'evaluator uses FloatArithmetic directly', which has no ratio input.

Fix: State: toggling Exact doesn't clear. `Ans` stays, and an exact `Ans` converts at the target when Exact is off. Add SetExact to B9 and a row to PLAN's table.

## 14. [minor] Calc/TODO.md H1 line 625

Problem: 'The client holds definitions as text and sends those a request needs' can't be done as written. Calc.Client has no parser and may reference only Protocol; the Lexer lives in Calc.Engine, which references Natural. So the client can't tell that `x = 3` is a definition, or which names an expression uses. `x = Ans` has no syntax for a line reference until I13.

Fix: Say: the worker parses. The `result` reply gains `definition` (name, params, body text); the client stores it and sends all definitions on each `eval`. `x = Ans` stores the line id from the B9 fix.

## 15. [minor] Calc/TODO.md G5 lines 596-598

Problem: Only `sin` over π/2 is given. A cold agent has no policy for an interval partly outside a domain (ln over 0, asin past 1), tan over a pole, or finding extrema with Angle in degrees. Under Mathematical policy an Invalid at one end becomes a Domain error on a line whose main result is fine.

Fix: Add: partly outside the domain or spanning a pole is unbounded with 0 proven, never an error. Extrema come from GetPi enclosures, or exact 90+180k in degrees. Width ≥ a full turn gives [-1, 1].

## 16. [minor] Calc/TODO.md F2 lines 550-553

Problem: Several results Matthew will see are undefined. 'Or unbounded' clashes with 'hold masked non-negatives': what are `not`, `rol`, `clz` there? Others: `bswap` when N isn't a multiple of 8; shift by ≥ N (C# masks the count, so the hardware test disagrees with any wrap-to-0 rule); MIN / -1 (C# throws). F3 says word size is part of the target, but neither brief adds it to Target or the protocol.

Fix: State: unbounded is signed only, `not` is -x-1, and rol, ror, clz, bswap and bitrev give Domain there. bswap needs N a multiple of 8. Shift ≥ N gives 0 (sign fill for sar), and hardware tests use counts below N. MIN / -1 wraps to MIN. F2 extends Target.

## 17. [minor] Calc/TODO.md C5 line 426, --smoke line 425

Problem: `3^(3^20)` has nothing to stop. Exact is over budget and falls to float, and B4's bounds route finished in 3 ms (2.198E+1663618948), so the try-this can't exercise Stop.

Fix: Use something truly slow, for example custom precision 1000000, then `sqrt(2)*sqrt(3)`, with Exact off for the smoke case.

## 18. [minor] Calc/TODO.md G8 line 611; PLAN.md line 154

Problem: 'Operation 16 of 19' describes demo.cs's hand-built DAG with b2, b4, b6, b8 shared, not any expression the calculator can parse. Typed with `^` as one node, it is 21 ops and the same `+`, dropping 98 bits to 0, is op 18 (measured at 100 bits).

Fix: Pin the text: `333.75*33096^6 + 77617^2*(11*77617^2*33096^2 - 33096^6 - 121*33096^4 - 2) + 5.5*33096^8 + 77617/(2*33096)` names op 18 of 21. Update PLAN to match.

## 19. [minor] Calc/TODO.md line 54, lines 11, 21-22; B1 Files line 194; line 512 against lines 6-7

Problem: Every session ends 'commit, push', but the plan only has Matthew run `git init`, so there's no remote and no authority to make one. Step 1 reads a WORKLOG that no brief creates, and nothing creates a Calc CLAUDE.md. Line 512 lets W sessions share a chat with the T before them, while lines 6-7 put T sessions in a chat opened in Natural, where Calc's memory doesn't load and Calc's edits sit outside the working folder.

Fix: Line 54: add 'and creates the GitHub repo and sets origin; until then sessions commit and flag it'. Add WORKLOG.md to B1's Files. Line 512: W sessions get their own chat opened in Calc unless both folders are attached.
