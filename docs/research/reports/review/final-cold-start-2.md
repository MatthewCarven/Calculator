# cold-start-2

Resolved: None

Verdict: mutate-9b.py runs as A1 says (59 of 59 old strings match once, all 59 build in a private copy, S13 goes red in 20 s), and B7's 2^32 budget does cost about 1 s at every split. The briefs still allow silent wrong answers, chiefly W3's `°` outside radians, B7's rounded base before `^`, B8's 4-round cap and B5's unchecked products; probes are in C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\final\cold2\.

## 1. [major] TODO.md W3, line 557 (`°` rule)

Problem: "Anywhere else it means x·π/180" ignores Angle. With Angle on deg, `sin(30°+15°)` makes the argument 0.785, then `sin` reads that number as degrees: 0.0137, where the truth is 0.7071. `sin(2*15°)` fails the same way. It's silent, in the mode the key exists for, and no listed test catches it.

Fix: Say: anywhere else, `x°` converts to the current Angle: x at deg, x·10/9 at grad, x·π/180 at rad. Add a test that `sin(30°+15°)` is 0.7071 at all three Angles.

## 2. [major] TODO.md B7 line 324 ("operands convert at the target"), B8 line 339, W2 line 549

Problem: `RoundedCall` covers functions, but an over-budget `^` still rounds an exact base and then raises it. Every result below is computed that way and compared with the truth. Shown digits that agree: `1.1^5000` 73 of 76 at 256 bits and 12 of 15 at 53 bits; `1.1^1000000` 71 of 76; `1.0000000001^10000000000` at 53 bits 6 of 15 (2.71828205323479, truth 2.71828182832313); `1.000000000000000001^(10^18)` at 256 bits 59 of 76. The ratio limit trips early: `1.1^4740` already has a 16,398-bit numerator. The chip claims a full-digit float. W2 keeps the fault, because `Pow` takes a float base. `(1/7)^(1/2)` has the same fault: it goes through `y = 1/2` on a rounded base, giving ...2720 where `sqrt(1/7)` gives ...725.

Fix: Add to B8: `^` with an exact base the target can't hold goes through `RoundedCall` with f = x ↦ x^y and y held exact, and `y = 1/2` on an exact base goes to B8's `sqrt`. W2 keeps this when `Pow` replaces the bounds route. Probed: `RoundedCall` over `x^y` matched the truth on the five cases above (the last three plus `1.1^5000` at both 53 and 256 bits) in all four modes, in 1 round, at 0.4 to 19 ms. Pin `1.1^5000` at 53 bits as 9.19233389907065E+206.

## 3. [major] TODO.md B8, line 339 ("Cap it at 4 rounds ... then use the nearest conversion")

Problem: Four doublings run out when x sits close to a representable value, and the fallback then rounds the argument first, which is the fault the helper exists to stop. Natural has no `ln` yet, so these use x−1, its leading term near 1: `ln(1+1e-300)` at 53 bits, `ln(1+1e-693)` at 256 and `ln(1+1e-3000)` at 1024 all cap and give 0. A real function shows it too: `sqrt(1+1e-1000)` at 256 bits in Up gives 1, where the truth is 1+2^-255. `1+1e-1000` rounds to 1 at 256 bits, so `asin` of it would answer π/2 where the truth is `Domain`, `atanh(1-1e-1000)` would be a false `DivideByZero`, and `acos(1-1e-1000)` would be 0. The brief also doesn't say what happens when only one end errors: at W = 320, `1-1e-100` rounds up to exactly 1, so `atanh`'s high end errors though the value is finite.

Fix: Replace the 4-round cap with a size bound: stop once W ≥ p + 64 + max(0, top(x)) + bits(n) + bits(d), then use the nearest conversion and stay float. An end that errors counts as disagreeing until that bound. Probed: `ln(1+1e-300)` at 53 and `ln(1+1e-3000)` at 256 then come right in 5 to 8 rounds, `sqrt` is right in every mode, and an exact case that can't settle (x·1000 at x = 1/1000, standing in for `log10(0.001)`) stops after 2 rounds.

## 4. [major] TODO.md B5, line 294 (guard `|y|·bitlen(m)`, and `e*n` in the exact route)

Problem: Both products wrap in C#'s default unchecked long, and Natural can't catch it because the wrapped exponent is valid. Probed with the formula as written: `(2^(2^62))^2` gives 7.2E-2776511644261678567 and `(1e1000000000000000000)^4` gives 5.2E-1553023288523357133, where both should be `Overflow`. At y = 2^62 the guard wraps negative and passes, so `(int)y` is 0 and `3^(2^62)`, `1.5^(2^62)` and `7^(2^62)` all give 1. B12's `3^(2^40)` doesn't reach this. Rule 5 covers only `ApInt` casts, so `(int)y` on a long is the natural code.

Fix: Write the guard as `|y| <= limit / bitlen(m)` and the exponent as `checked(e * n)`, with `int.CreateChecked` for n. Add tests for `3^(2^62)` and `(2^(2^62))^2`.

## 5. [major] TODO.md W1, line 544 (promotion rule 1)

Problem: "Its argument converted with no Inexact" is also true of a float argument, because converting a float at its own target raises nothing. Probed: `sqrt(1+1e-80)` at 256 bits is exactly 1 and Convert raises None, and `ln(1)` raises no Inexact by the T plan's table. So `ln(sqrt(1+1e-80))` is promoted to exact 0, where the truth is 5e-81. That is the false exact chip the rule was written to stop.

Fix: Rule 1 should read: its argument is an exact value (`IntValue` or `RatioValue`) that converted with no Inexact. Add `ln(sqrt(1+1e-80))` staying float to the tests.

## 6. [major] TODO.md C3, line 427 ("`7 + 2 1/x` gives `7+1/2`")

Problem: Inserting `1/` before the last operand is wrong whenever the operator before it isn't + or −. By B3's left-to-right rank, `8 ÷ 2 1/x` becomes `8÷1/2`, which is 4 where a calculator gives 16. `2 ^ 3 1/x` becomes `2^1/3`, which is 2/3. Both are silent, on a first-release key.

Fix: Add to the table: `1/x` wraps in parentheses unless the operand follows +, − or nothing, so `7+1/2` stays, and `8÷(1/2)` and `2^(1/3)` join the table's tests.

## 7. [major] TODO.md C3, lines 429 and 431 (keyboard `=`, against "the keyboard and keypad agree")

Problem: "A bare name or `name(args)`" also matches `sqrt(2)`, `abs(-3)`, `pi` and `Ans`. Typing `sqrt(2)=` inserts `=` instead of evaluating, and Enter then gives a syntax error, since `=` has no grammar until H2. The keypad's `Ans` then `=` evaluates, while keyboard `Ans=` inserts, so the Done-when can't be met.

Fix: Narrow the rule: `=` inserts only when the text is a bare name, or `name(a, b)` with bare identifiers as arguments, and the name isn't `Ans` or anything in the worker's `functions` reply or constants.

## 8. [major] TODO.md H2, line 704, against B9 lines 351 and 354

Problem: Definitions that use `Ans` have no way to work. `y = Ans*2` isn't covered, because only bare `x = Ans` stores a line id. Then `y` as a later source doesn't mention `Ans`, so B9 sends no `ans` and the body can't resolve. For `x = Ans`, the worker can't get x's value: `eval` has one `ans` slot, which means the current `Ans`, and `a = Ans`, `b = Ans`, `a+b` needs two. After a target change rule 14 needs a replay, but `RecomputeAsync` adds a history line. The agent has to invent the wire design.

Fix: Say: any definition whose body mentions `Ans` (found with the Lexer) stores that line id, and `eval` carries a value per such definition, lossless or held. A value from another target is first rebuilt by replaying the `AnsFrom` chain without adding a line. Add both cases to the smuggling test.

## 9. [major] TODO.md H2, line 704 ("Definitions bind by name at use")

Problem: `x = 3`, then `x = x + 1`, replaces x with a body that names itself. Every later use hits the recursion cap and the 3 is gone. This is a very common calculator line, it isn't a Natural limit, and neither the brief nor its tests mention it.

Fix: Ask once, recommending that a definition with no parameters whose body names itself takes the old body in parentheses at definition time, so x becomes `(3) + 1`. It stays text and survives a target change. Add a test.

## 10. [major] TODO.md G4, lines 641, 644 and 645, against H4 line 712 and H6 line 718

Problem: G4's test "one through M" can't be written: M arrives in H4, after G in PLAN's order, and G4 needs only B8. H4 and H6 say nothing about carrying both ends, so when M and saved sessions land, a value proven to 7 digits can read as 76 again by that route. G4's Files also leave out `Text/Lossless.cs` (a fourth tag for two ends) and `Calc.Protocol/EvalContext.cs` (intervals on or off changes the value kind, and context keys caches and recompute).

Fix: Move the M test to H4 and the saved-value test to H6, each with the line "with intervals on, carry both ends". Add both files to G4's Files, and add the interval flag to `EvalContext`.

## 11. [minor] TODO.md A1, steps 2 and 5 (lines 189 and 195)

Problem: The battery rewrites files in place before 9b is committed. All 45 mutations still to run edit uncommitted code, and `StyledNumber.cs` and `ApFloat.GenericMath.cs` are untracked, so `finally` is their only restore and a killed run skips it. Natural's recipe, "check the originals are back before committing", has nothing to diff against.

Fix: Commit 9b locally first, without pushing, then run the battery with `git status` clean, and put any closed hole in a second commit. Or run it in a scratchpad copy of the repo: S13 built and tested there in 20 s.

## 12. [minor] TODO.md A1, step 3 (line 192)

Problem: "Add 9b's notebook and memory notes" names no source, and the brief itself says nothing in the repo records this. The transcript does exist, but only the script's header hints at it, by a short id.

Fix: Point at `~/.claude/projects/C--Users-Admin-Desktop---Programming---Natural/36ea4d6b-3562-4b2d-9ac5-e7068000cee2.jsonl` for the WORKLOG entry and notes.

## 13. [minor] TODO.md B3, lines 261 and 266, against PLAN.md's risk table

Problem: The Depth rule counts a left spine and refuses past 200,000. The Done-when says 1,000,000 terms of `1+1+…` parse. Both can't hold, and PLAN's risk table sides with the refusal.

Fix: Change the Done-when to: 199,999 terms parse on the big stack, and 1,000,000 give the `CalcError` with a span. B9's test already allows a clean error.

## 14. [minor] TODO.md B7, lines 322 to 324, and B8 line 338

Problem: The ratio limit is applied from bit lengths even when the quotient is an integer. An integer stays exact to 2^24 bits, but `10^6000/10` becomes a float with Inexact. `10^6000/10 + 1 - 10^5999` then gives 0, where the exact answer is 1 and the bound is met at `10^4932`. The limit trips at `10^4933` (16,388 bits). `floor(10^6000/7)*7` loses its low digits the same way. Cost isn't the reason: DivRem took 4.8 ms there, where GCD took 117 ms.

Fix: Say: integer ÷ integer tries `DivRem` first, and remainder 0 gives an `IntValue`. The 2^14 limit applies only to a ratio that remains.

## 15. [minor] TODO.md B7 line 326, B5 line 293

Problem: "Past a long with |x| > 1 it's `Overflow`" gives a false refusal near 1: `(1+1e-21)^(10^21)` is 2.718281828459045 (computed at 2000 bits). The case |x| < 1 isn't covered, as in `(2/3)^(10^30)`. B5 doesn't cover an integer float y past a long at all, for example `1.5^1e30` with Exact off.

Fix: State: past a long, |x| ≥ 2 or |x| ≤ 1/2 is `Overflow` at bits and ∞ or 0 at a format. Otherwise it's `NotAvailable` until W2's `Pow`.

## 16. [minor] TODO.md B8, line 338 (`sqrt` by `SqrtRem`)

Problem: No budget covers `SqrtRem`, which is quadratic, while the float route costs almost nothing. Measured on random integers: 460 ms at 2^16 bits, 1.5 s at 2^17 and 6.1 s at 2^18, so by extrapolation about 100 s at 1 Mbit and hours at B7's 2^24 limit. `sqrt(2^1000000)` took 11.8 s. `ieee.Sqrt` at 256 bits on the same-sized values took under 1 ms.

Fix: Add: `SqrtRem` only when bits(x)² ≤ the work budget, and settle a power of two from its exponent. Past that, go straight to the float route.

## 17. [minor] TODO.md B8, line 338 (Exact list)

Problem: `fma` is in B5's table but not in B8's Exact list, and `RoundedCall` takes one argument, so `fma` falls to converted operands. `fma(1/3, 3, -1)` gives 4.3E-78 at 256 bits and -5.6E-17 at 53 bits, where `1/3*3-1` gives exact 0.

Fix: Add `fma` to the Exact list as a·b + c through `ExactArithmetic`, using the float route only over budget.

## 18. [minor] TODO.md W3, lines 558 to 559

Problem: "Reduce exactly ... and only then convert", with "Radians use `RoundedCall`", reads as degrees skipping it. A ratio then gets rounded first: by an exact oracle, about 22% of random k/m degrees come out differently (59 of 265 at 53 bits, 61 of 268 at 256). `sin(1/3)` in degrees at 53 bits ends ...be1 where the truth ends ...be2. Separately, the `sin(π)` rewrite doesn't say it applies only at rad, and at deg `sin(π)` is sin(3.14°) = 0.0548, not 0.

Fix: Add: after reduction, a value the target can't hold goes through `RoundedCall` in the angle unit. Limit the π rewrite to Angle = rad.

## 19. [minor] TODO.md B5 line 295, against B4 line 280

Problem: At a format the bounds route throws where hardware gives a value, though B4 says only operands far outside the format throw. `5^(2^62)`, `1e10^1e18` and `1e300^1e16` at binary64 throw `OverflowException` where hardware gives ∞. `1e-10^1e18` throws where hardware gives 0. The W-bit squarings run out of exponent.

Fix: Add: at a format, leave the loop once the low bound's top passes emax + 1, or the high bound's top falls 2 below the smallest subnormal, and take ∞ or 0 through `ieee.Convert`.

## 20. [minor] TODO.md B4, line 274, against B2 line 244

Problem: The Lexer accepts underscores between digits, but `ieee.Parse` refuses them in an exponent. `1e1_000` and `1_0e1_0` throw `FormatException`, so a literal that passes the Lexer becomes `Syntax` with no span.

Fix: Add: strip `_` from the raw text before `ieee.Parse`.

## 21. [minor] TODO.md B9 lines 351 to 353, against C7 line 464

Problem: `fetch` is a request with no reply listed. It isn't deferred the way `digits` and `reformat` are, and it has no B9 test, yet C7 relies on it. The agent has to invent the reply, and it must waive B9's 1 MiB limit.

Fix: Add a reply `value` (id, lossless), exempt from the size limit, with a B9 round-trip test on a value over 1 MiB.

## 22. [minor] TODO.md F3, lines 596 to 597

Problem: The Client has no Natural and rule 3 bars `BigInteger`, so DEC rows and the bit reinterpretation must come from the worker, yet Files name no `Messages.cs`, `Loop.cs` or `Session.cs`, which B9 requires. "Keeps the bits" doesn't settle widening: -1 at 8 bits taken to 16 is 255 if the bits are kept, but -1 if the lossless `i:-1` is simply re-masked. "Live" rows also clash with nothing evaluating on a keystroke before H8.

Fix: Add those three files, with rows from `reformat` notations hex, dec, oct and bin. State the widening rule and ask once, with -1 at 8 to 16 bits as the preview. Say rows update on each result.

## 23. [minor] TODO.md G4, line 643 (`demo.cs` lines 95 to 102)

Problem: The cited `Proven` scans down from the cap, one `ToString` per digit. On `sqrt(2)^2-2`, which proves nothing, it took 519 ms at 4,096 bits and 17.7 s at 8,192, for one result.

Fix: Start the scan at the width's limit: both ends can only round alike at n digits if n ≤ about (top(x) − top(hi − lo))·log10 2 + 2. An interval spanning 0 is 0 at once.

## 24. [minor] TODO.md C3, lines 426, 427 and 431

Problem: The try-this list does `±` straight after a result, but the table gives `Ans` handling only to `1/x`, `√`, `x²` and `%`. Is `Ans×4` then `±` `Ans×(-4)` or `-Ans`? The agent has to guess on the exact case Matthew will try.

Fix: Add a table row: straight after a result, `±` gives `-Ans`.
