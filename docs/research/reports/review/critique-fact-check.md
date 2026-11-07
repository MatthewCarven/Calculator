# fact-check

Verdict: Most numbers, line pointers, API names and links check out: stage totals 84/20, the digits column, all A1/A2 line numbers, 682 tests, wpf-spike lines 27/405, and Rump's 0/36. Five briefs carry a false fact that sends a session wrong (C4's formula, B8's heap cap, B4's power route, B6's budget, C5's Stop demo), plus a tail of smaller slips, all with verified corrections.

## 1. [major] TODO.md C4, line 412 (`Digits.cs` and the test it mandates, line 412-413)

Problem: `(p−1)·1292913986 >> 32` is not floor((p−1)·log10 2). log10(2)·2^32 is 1292913986.49, so the constant runs low. An exhaustive run over p = 1 to 2^30 against an exact 128-bit fraction found 66,011,104 of 1,073,741,824 values differ (6.15%), first at p = 70,778 (exact 21306, formula 21305), last at p = 1,073,741,823. All presets 53 to 65,536 match, so the bug only shows on random p, and the mandated 'random p to 2^30' test goes red almost at once. Rounding the constant up is no cure (68,206,624 differ, first at p = 112,818), and a 34-bit constant still gives 32,456,671. There is no long overflow: (2^30−1)·c is 1.388×10^18, below 2^63. But with an `int` p, C# multiplies in int and `>> 32` becomes `>> 0`: p = 256 gives −1019415362.

Fix: Replace the formula in C4 with `(int)Math.BigMul((ulong)(p - 1), 0x4D104D427DE7FBCCUL, out _)`, the high 64 bits of (p−1)·floor(2^64·log10 2). Verified: 0 differ across all of p = 1 to 2^30. It needs no Natural, no Int128 and no packages. Have the test use the 128-bit fraction 0x4D104D427DE7FBCC_47C4ACD605BE48BC as its exact formula.

## 2. [major] TODO.md B8 line 332, C8 line 454; PLAN.md Risks lines 209-210

Problem: The brief says set `DOTNET_GCHeapHardLimit` (default 4 GiB) but not that the runtime reads the value as hex. Probe of GC.GetGCMemoryInfo().TotalAvailableMemoryBytes: "4294967296" gives 285,960,729,238 bytes, "1073741824" gives 70,656,464,932 bytes, and "100000000" or "0x100000000" give 4,294,967,296. So a decimal string set from a settings value silently means no cap. PLAN's top risk mitigation then does nothing, and B8's out-of-memory test runs loose on a 15.8 GB laptop whose C: drive is nearly full. The research probes used "0x40000000", so the trap was known but never reached the brief.

Fix: Add to B8's traps: the value is hex, so write `"0x" + bytes.ToString("X")`. Add a test that the worker reports GC.GetGCMemoryInfo().TotalAvailableMemoryBytes equal to the setting. Note it in C8 beside the heap-cap setting.

## 3. [major] TODO.md B4 line 250, against B4's goal (line 238) and G1 tests (line 576); report 1 line 14

Problem: 'Compute the exact product and round once through the public constructor' cannot meet B4's own goal. The constructor takes only a precision, so it has no format range and raises no flags. Probe: 1e200 squared at binary64 through `new ApFloat(sq, e, 53)` gives 1E+400 with no flag change, where the hardware gives ∞. At 8 bits, 3^9 by constructor leaves flags None though the result was rounded. Yet B4 returns 'a value and flags', Target can be an IeeeFormat, and G1 predicts flags bit for bit.

Fix: Change the sentence to: build the exact value with `new ApFloat(product, e, ApFloat.MaxPrecision)` and round once with `ieee.Convert(...)`; for negative y, `ieee.Divide(1, exactValue)`. Verified: the same 1e200 square gives ∞ with Inexact|Overflow, and 3^9 at 8 bits gives Inexact. Note that this route needs the product under 2^30 bits, so keep the guard at very high p.

## 4. [major] TODO.md B6 lines 299-300; knock-on to B5/B6 line 302, C5 line 426, C7 line 448

Problem: The two sentences contradict. 'Integers to 2^24 bits' lets through a product of two 2^23-bit integers, which scales from report 2's 1.91 s at 100k bits to about 3.7 hours. 'One operation stays under about 100 ms' corresponds to about 30,000-bit operands (from 10.7 ms at 10k bits). An agent who obeys 'measure and tune' cuts the budget about 500-fold, and then C7's `2^1000000` and any demo in the exact range fall to 76-digit floats. An agent who keeps 2^24 fails the 100 ms test. B10 meanwhile says 'Don't refuse anything merely slow, because Stop handles slow'.

Fix: Pick one in the brief. Recommended: integers keep the size budget of 2^24 bits with no time target, since Stop covers slow work; ratios keep 2^14 bits, because GCD is 64 ms there. Delete 'so one operation stays under about 100 ms', or scope it to ratio normalisation only.

## 5. [major] TODO.md C5 line 426 ('Try this: `3^(3^20)`, press Stop')

Problem: This expression never runs long. Its exact result is 5.5×10^9 bits, past B6's budget, so it falls to B4's float route. Simulated at 256 bits with W = 384: both bounds took 15.6 ms and rounded alike, and G76 text took 10.1 ms (2.198…E+1663618948). There is nothing to Stop, so the human acceptance check for 'long sums feel safe' passes without touching Stop.

Fix: Use an expression inside the exact budget, such as `3^1000000` (1,584,963 bits; `ApInt.Pow(3,100000)` measured 2.1 s, so about 3.5 minutes today). This depends on keeping the 2^24 budget. If that budget is tuned down, use a long multiply at a custom precision with Exact off. Give the `--smoke` line a concrete expression too.

## 6. [major] TODO.md B4 line 258 ('Exponent overflow throws, even at binary64: `Overflow`'); report 1 line 91

Problem: False for anything the evaluator can hold. With in-format operands, `IeeeContext(Binary64).Multiply(1e300, 1e300)` returns ∞ with Inexact|Overflow, and `Parse("1e400")` gives ∞ and flags. It throws only when the operands' own exponents overflow a long (notes.txt says so), which cannot happen at a format target. The line also contradicts G2's try-this `1e300*1e300` in binary64, PLAN's 'Format mode behaves like the hardware', and B4's own bit-for-bit `double` test.

Fix: Reword: 'At a bit target, exponent overflow throws: map to `Overflow`. At a format target, overflow is ∞ with the Overflow flag; only operands far outside the format would throw.'

## 7. [minor] TODO.md B8 line 332 with B9 line 354; PLAN.md line 78; docs/research/ui/skeleton/src/Calc.App/Calc.App.csproj

Problem: B8 spawns `calc-worker` from `AppContext.BaseDirectory`, but the skeleton's copy step exists only in Calc.App, and B1 says to keep it 'for later'. PLAN gives Calc.Cli a reference to Client only. So `Calc.Cli` (B9's try-this) and B8's real-exe tests in `Calc.Client.Tests` have no worker beside them until C1.

Fix: In B1 add the `ReferenceOutputAssembly="false"` worker reference and the CopyWorker target to Calc.Cli and Calc.Client.Tests, and update PLAN's table so Calc.Cli also lists a build-only Worker reference.

## 8. [minor] TODO.md G8 line 611; PLAN.md line 154

Problem: 'Operation 16 of 19' describes only demo.cs's hand-chained tree, with b2, b4, b6, b8 and a2 shared. The expression as typed has 21 operations, each `^` being one. Run op by op at 100 bits with `^` as one operation, the cancelling `+` is operation 18 of 21, still dropping 98 proven bits to 0. G8's test as written cannot pass on a parsed expression, since variables arrive only in H1.

Fix: Change G8's test and PLAN line 154 to 'operation 18 of 21, the + that adds 5.5·b^8', or test by operator and bits lost rather than by index.

## 9. [minor] PLAN.md line 160; report 5 line 56; G7 line 607

Problem: 'Muller's sequence gives exactly 100 at 53, 106 and 212 bits' is false at 212 bits. There n = 100 gives 100 + 5.93×10^-52 (`== 100` is false), and demo.cs's E20 print hid it. It reaches exactly 100 only by n = 110. A G7 test asserting 100 at 212 bits fails.

Fix: Say 'exactly 100 at 53 and 106 bits, and 100 to 52 decimal places at 212', or assert only that it is wrong by more than 90.

## 10. [minor] TODO.md G7 line 607; PLAN.md line 159

Problem: 'Six precisions from 80 to 121' is demo.cs's hand-picked list: 80, 96, 106, 113, 120, 121. A full sweep shows 8 of the 42 precisions in that range give other wrong answers: 84, 85, 90, 91, 93, 95, 97, 98 (p = 90 gives 8.59E+009). The unbroken run is 99 to 121. A ladder chosen by a cold agent will not reproduce 'Rump's six wrong agreeing rows'.

Fix: Name the ladder in G7 (80, 96, 106, 113, 120, 121, 122), or test 99 to 122 and assert 23 agreeing rows then a change.

## 11. [minor] TODO.md B5 line 272 ('because shortest is what hardware people expect there')

Problem: `"R"` is not shortest for subnormals, because it works from the value's 53-bit precision, not the bits the subnormal really has. Probe: double.Epsilon prints `5E-324` in .NET, `4.9406564584124654E-324` from ApFloat; 1e-320 prints `9.99988867182683E-321`; float's smallest prints `1.4012985E-045` against `1E-45`. Any test comparing format text with hardware text fails on subnormals.

Fix: Add to B5's traps: in a format, try G1 upward until `ieee.Parse` of the text gives the same bits, or accept long subnormal text and say so; compare bits, not text, in tests.

## 12. [minor] TODO.md B5 lines 271-272, 284

Problem: 'The preview stays under 40 ms at any size (report 3)' is false, and report 3 measured E20 at 21 to 41 ms, not E99. E99 of 1/3 measured 35 ms at 2^20 bits, 124 ms at 2^22, 470 ms at 2^24, and it scales linearly toward C4's 2^30 limit. G76 near the top of the exponent range took 140 ms. The test's 50 ms bound at 2^20 leaves only 1.4× margin on a machine with a ×3 slow phase, so it will flake.

Fix: Reword to 'about 35 ms per Mbit'. Set the test bound at 200 ms, or scale it by the calibration loop.

## 13. [minor] TODO.md B5 line 281 ('`F` of 1e100000 takes 7.9 s')

Problem: Wrong source. notes.txt's 7.9 s is `1.5` with `"F100000"`, 100,000 digits after the point (6.4 s here). `F0` of 1e100000 took 1.9 s here, and Natural's own doc says 2.5 s.

Fix: Write: '`"F100000"` of 1.5 takes 7.9 s, and `F0` of 1e100000 about 2 s.'

## 14. [minor] TODO.md A2 lines 178, 185

Problem: '`1e2776511644261678566` is the last sound exponent' is true for Top and comparison, but that value cannot print. `ToString()`, `G76` and `E20` all throw OverflowException; only `A` works. `1e2776511644261678565` is the last that prints. The tests line mentions 'values that parse but won't print' without saying what should happen, and B5 has no handling, so a typed literal crashes display rather than evaluation.

Fix: In A2 say: '…566 is the last with a true Top; …565 is the last that prints.' State the wanted outcome (print fixed, or OverflowException documented), and in B5 route a ToString OverflowException through ErrorMap as `Overflow`.

## 15. [minor] TODO.md B8 line 337

Problem: 'Spike: 2 to 3 ms to exit, about 100 ms to the next answer' was timed from `worker.Kill()`, with no flag and no wait (wpf-spike.cs lines 368-373). B8's Stop waits 150 ms first, and a running multiply never reads the flag. So Stop to exit is about 153 ms and the next answer about 250 ms. A test bound taken from the brief fails.

Fix: Write 'Kill to exit 2 to 3 ms, so about 153 ms from Stop, and about 250 ms to the next answer'.

## 16. [minor] TODO.md rule 5, line 36

Problem: 'Those casts wrap' is half false. `(long)` on ApInt never wraps: `(long)(ApInt.One << 70)` throws OverflowException. `(int)` wraps only for values that fit a long (5000000000 gives 705032704 as stated) and throws above that. The advice stands, but the stated reason would mislead anyone reasoning about ErrorMap.

Fix: Reword: '`(int)` wraps (`(int)(ApInt)5000000000` is 705032704); `(long)` throws. Use `int.CreateChecked` and `long.CreateChecked`.'

## 17. [minor] TODO.md B4 line 259, G1 line 576

Problem: 'Bit for bit against C# `double`' fails on every NaN. Hardware 0.0/0.0 is FFF8000000000000, while Natural's single NaN encodes as 7FF8000000000000 (its sign is never set).

Fix: Add 'all NaNs count as equal' to both test lines.

## 18. [minor] TODO.md G1 line 577 (the mutation note)

Problem: `2.4703282292062328e-324` catches parse-at-53-then-convert (gives 0), but not the likely mutant `ieee.Convert(ApFloat.Parse(s))`. That parses at the default 256 bits and still gives the correct subnormal, so the mutation survives.

Fix: Add the 100-digit literal `2.470328229206232720882843964341106861825299013071623822127928412503377536351043759326499181808179962e-324`. Verified: double.Parse and the direct format parse give bits 1; parsing at 53 or at 256 bits first gives 0.

## 19. [minor] TODO.md C1 lines 379-381

Problem: 'Needing no shown window' is placed beside 'App.xaml.cs of Studio shows the pattern', but Studio calls `window.Show()` (App.xaml.cs line 30) and captures after Loaded, a visible window. The window-free route is in wpf-spike.cs lines 403-412 (Measure, Arrange, Png). Its `Offscreen` at line 281 uses the forbidden `ThemeMode`.

Fix: Point `--screenshot` at wpf-spike.cs lines 403-412, and keep Studio only as the model for argument handling and `--smoke`.

## 20. [minor] PLAN.md lines 38, 200; TODO.md lines 116, 597, 619

Problem: 'Stages F, G and H don't depend on E' and 'They don't depend on each other' are false as written: G5 needs W6, G10 needs H5, and I-stage items aside, F3 and G7 to G9 also wait on window sessions. Swapping stage G ahead of E or H will stall at G5 and G10.

Fix: Add 'except G5, which needs E, and G10, which needs H5'.

## 21. [minor] PLAN.md line 29; TODO.md B9 lines 346-350

Problem: PLAN says stage B ends in a console calculator with Stop, but B9's `Session` operations and Cli commands list no stop of any kind, and C5 is the first thing that calls it.

Fix: Add `StopAsync` to B9's operations and Ctrl+C in Calc.Cli, or drop 'Stop' from PLAN's stage B row.

## 22. [minor] PLAN.md lines 34, 149

Problem: 'The features no other calculator has' and 'The ones only this calculator can do' overstate the source. Report 5 line 55 says of N proven digits: 'Rare, not unique: Qalculate has intervals, and Android's reals hit the same equality wall', and no competitor was actually run.

Fix: Say 'that nothing surveyed offers together', and keep 'Nothing surveyed offers this' for the x-ray.

## 23. [minor] PLAN.md line 24

Problem: 'A calculator can reach all three by typing' is not true of bug 2 under this plan. `ToRadixDigits` has one caller, ApFloat's `"B"` at ApFloat.Text.cs line 219. ApInt `"B"` goes through `ToTwosComplementDigits`, rule 6 and F1 avoid ApFloat `"B"`, and no brief calls it. The bug is real (33 zeros confirmed), but reachable only if something adds that call.

Fix: Reword to 'two by typing, and the third only if binary display ever uses ApFloat's "B"', and add a rule that any such display caps its length until A2 lands.

## 24. [minor] TODO.md D intro line 462, D3 line 477

Problem: 'A quick version of all three' overstates the diff: it has no dedicated squaring, so D2's 89 ms and 3.5 s are Karatsuba alone. Its `DivRemShort` takes only divisors below 2^31 (`(divisor[0] >> 31) == 0`), so D3's 'one-limb divisors' tests would miss top-bit-set limbs if the diff is copied. D3's 'a linear path' is also not new: one-limb division is already linear at 33 ns a bit. It is the faster constant that the session buys.

Fix: Say 'a quick version of the adder, Karatsuba and short division, without squaring', and add to D3: 'the diff handles divisors below 2^31 only; test limbs with the top bit set'.

## 25. [minor] TODO.md F4 line 563 ('`OnTop.cs` has them'); E intro line 488

Problem: OnTop.cs has no `fib` at all (grep of OnTop.cs, Loose.cs and demo.cs finds none), so F4's list is not covered; `gcd`, `modpow` and `isqrt` are Natural members, not recipes. Separately, E's 'each brief there has goal, files, baseline, pitfalls, tests and mutations' is untrue: T7, T8, T10 and T12 to T16 have no Baseline, and T16 has no Tests.

Fix: In F4 add 'fib by fast doubling, new here'. In E say 'goal, files, pitfalls and mutations, and baseline and tests where the session needs them', pointing T16 to T15's tests.

## 26. [minor] TODO.md I1 line 653, W4 line 532; PLAN.md line 36

Problem: Three small number slips. I1 pairs `sum(k, 1, 100, 1/k)` with H_1000's 0.48 s and 9 ms; H_100 measured 9.4 ms exact. W4's 'needs π to 3,321,933 bits' implies W = 5, as top(1e1000000) is 3,321,928; at 256 bits it is at least 3,322,216. PLAN's stage I row says Calc, but I14 runs in Natural.

Fix: Use `sum(k, 1, 1000, 1/k)`; say 'about 3.32M bits' in W4; mark stage I as 'Calc (I14 Natural)'.
