# dependency-order

Verdict: The stated graph is nearly clean: all 84 items have briefs, the counts and T dependencies match section 12, there are no stated cycles, and the only stated forward edge is G10 on H5. The damage is in unstated edges: recompute through `Ans` and lossless text have no owner, eight sessions need wire changes they don't list, and PLAN's claim that stages can be swapped is false.

## 1. [major] TODO.md B9 (lines 347-348), C3 (line 400), C4 (line 410); also G2 (581), G7 (607), G9 (615); reports/5-extras.md rank 2 (line 10); PLAN.md lines 89-90, 121

Problem: Recompute fails on any line containing `Ans`, and the keypad's normal flow produces those lines. C3 says an operator key after a result continues from `Ans`, and keys insert text, so `2 + 3 =` then `× 4 =` stores the source `Ans×4`. B9's RecomputeAsync reruns that text at the current target, but its guard sends `Ans` only if the target matches. So after a precision change, C4's "Recompute last line" gets "no answer yet". Once anything is computed at the new target, it silently uses that unrelated `Ans` and adds a wrong line. The survey's rank 2 item says "stored `Ans` becomes a line reference", which is what makes the clear lossless. That clause fell out with no statement: only H1's `x = Ans` and optional I13's `#3` touch line references. G2's truth row, G7's sweep and G9's lab rerun "the same expression" at other targets, so they hit the same wall. B9's try-this reruns line 1, which has no `Ans`, so nothing catches it.

Fix: In B9, store `ansLine` (the id of the line `Ans` meant) on each history line. RecomputeAsync recomputes that line first, recursively, at the current target, then sends its fresh lossless value as `ans`. A deleted parent gives a clear error, as H1 does. Add the test: `1/3`, `Ans*3`, SetTarget, compute `5`, recompute line 2, expect 1 at the new target. Add `2+3`, `Ans*4`, change precision, Recompute last line to C4's try-this. State in G2, G7 and G9 that they use the same chain rerun.

## 2. [major] TODO.md B5 (line 278), B6 (lines 287-309), B7 (lines 314, 316, 324), B9 (lines 348, 353); C3 (line 402), C7 (line 448)

Problem: No session owns the round trip `Ans` depends on. B5 defines Lossless as `ToString("A")` plus the precision, when FloatValue is the only kind. B6 adds IntValue and RatioValue and never gives them a lossless form, and with Exact on by default most answers are those kinds. B7 adds an `ans` field, but its files are Protocol and Worker only. B3 and B4 give a bare identifier no lookup, and no brief says the evaluator resolves the name `Ans` or turns lossless text back into a Value. B7's tests never send `ans`, and B9's tests use FakeWorker. The first real use is C3's try-this, after B7 to B9 have closed green. C7's `digits` request (try-this `2^1000000`, an exact integer) and H5's saved values rest on the same undefined text.

Fix: B6: add `Text/Lossless.cs`, a tagged form (`int:<hex>`, `ratio:<hex>/<hex>`, `float:<bits>:<A text>`), with a round-trip test over every kind. B4: add an evaluator environment for bare names (constants, `Ans`, later definitions), with an unknown name giving a `CalcError`. B7: add a real-exe test: eval `1/3`, then `Ans*3` passing the returned lossless text, expect exact 1.

## 3. [major] TODO.md B7 (lines 316-317) against B10 (359, 363), C2 (388-389), F2/F3 (551, 557; Target at 242), G3 (586), G4 (591), G8 (611), H1 (625), W4 (532)

Problem: B7's message list reads as complete, but eight later sessions need wire changes their briefs don't list. B10: the estimate goes in `progress`, which has only (id, done), and the preflight isn't wired into Loop.cs. C2: "understood as" needs a reply field, because the Client has no parser, and C2's files are Client and views only. W4: no reply type can carry a warning. G3: no request exists for the inspector, whose numbers can only come from the worker. G4: proven digits and the toggle, with files only in Eval and Values. G8: the per-node trace. F2 and F3: `Target` is "bits or an IeeeFormat" and a word size is neither, yet neither brief touches Target, Messages or Loop. H1: without a parser the client can't tell that `f(x) = x^2` is a definition, or which definitions a source needs. B6's budgets are called settings, but C8's list and H6's panel both omit them.

Fix: Add `Calc.Protocol/Messages.cs` and `Calc.Worker/Loop.cs` to the Files of B10, C2, F2, G3, G4, G8, H1 and W4, naming each change: `estimate` on `progress`; `understood`, `warnings`, `proven` and `trace` on `result`; an `inspect` request; word targets in `Target`; a `defined` reply. H1 sends all definitions rather than "those a request needs". Add budgets to `options` and to C8's settings list.

## 4. [major] PLAN.md lines 38, 200; TODO.md G5 (lines 116, 597), G10 (619), H4 (635), W0 (516)

Problem: PLAN's swap claim is false, and Matthew is asked to decide question 6 on it. Line 38 says F, G and H don't depend on E, and line 200 says the stages don't depend on each other. G5 needs W6, as the TODO itself says. H4 reads "the worker's function table", but the request exposing it is added only in W0, which needs T4, so H4 can't be finished if H is swapped ahead of E. G10 needs H5, so G depends on H.

Fix: Move "Add a `functions` request" from W0 into B7's protocol list, since B4's table already exists. Change PLAN line 38 to: "Stage F doesn't depend on E, nor does H once the `functions` request moves to B7. In G only G5 does, and it comes after W6." Replace the last sentence of line 200 with: "Only G5 needs E, and G10 comes after H5."

## 5. [major] TODO.md order of work (line 121 against 128), G10 brief (line 619)

Problem: This is the one stated forward edge in the recommended order: G10 Needs H5, but H5 comes after all of stage G, and H5 itself needs H1. An agent told "start G10" finds its need unbuilt two sessions out. The survey groups receipts with sessions only by theme (rank 11, depending on history). However, a receipt whose expression uses a definition or `Ans` has to carry it, so the H1 edge is real.

Fix: Move G10 in the order of work to just after H5, keeping it counted in G (10). Change G10's Needs to `G1, H1, C8`, and add: a receipt inlines any definition or `Ans` chain its expression uses.

## 6. [major] transcendentals/TRANSCENDENTALS-PLAN.md section 10 (lines 314, 318), T12 (417), T14 (429); TODO.md table (504, 506), W1 (520), W4 (534), W5 (537), rule 4 (34-35)

Problem: W4 and W5 need a public API that T12 and T14 don't promise. W1's rule turns a float with no Inexact flag into an exact value, and W4's first test is `sin(30)` in degrees being exact 1/2. Flags come out only through `IeeeContext`: section 10's flagged form is internal, and rule 4 sends every float operation through the context. Section 10 line 314 gives a context form for plain functions, but line 318 gives the units form only as a static, and neither T12's goal nor the TODO table adds a context form. Found at W4, it means going back to Natural for an API change with its own tests.

Fix: Add to T12's goal: `ieee.Sin(x, unit)`, `Cos`, `Tan`, `SinCos` and the SinPi family on `IeeeContext`. Add to T14's: `ieee.Asin(x, unit)`, `Acos`, `Atan` and `Atan2` with units. Each gets one flags test, including no Inexact on an exact case.

## 7. [major] TODO.md B10 (lines 360, 363), C5 (422, 424), D4 (483), F4 (563), W0 to W6 (514-540), W4 (532)

Problem: Scientific functions never reach the cost model. B10 builds it from report 2's arithmetic tables, D4 updates its constants before stage E, F4 sizes its own functions, and no W session mentions it. After E, a `sin` node has no estimate, so C5's elapsed-over-estimate bar has nothing to divide by, and W4's "about N minutes" has no source. The promise is PLAN's Stop-and-progress row and its `sin(1e1000000)` risk, where the warning has to name a time.

Fix: W0: add cost entries for π, e, ln 2 and ln 10 by bit size, from section 2's constants table. W1 to W6: each adds its functions' cost by bit size from section 13's per-function limits, with an estimate-against-measured test within ×4 at three sizes. W4's N comes from the π entry. Recalibrate once after W6.

## 8. [minor] TODO.md B1 (199, 204), B6 (309), B7 (313, 321)

Problem: Real graph has a cycle: B7 Needs B6, but B6's Done-when needs B7's work. B1's `--eval` prints a hard-coded value and ignores its argument. Real parsing arrives in B7, whose files include `Calc.Worker/Program.cs`. B6 can't show `0.1+0.2` as 0.3 through `calc-worker --eval` as written.

Fix: Change B6's Done-when to Engine tests: `ResultText` of evaluating `0.1+0.2` is 0.3 and `2^1000` prints in full. Keep the `--eval` line in B7's Done-when instead.

## 9. [minor] TODO.md C4 (lines 407, 410, 416), C5 (421), C6 (431)

Problem: Real graph has a second cycle, and a try-this that can't be done. C4 writes "Set to 1,024 bits. Cleared." and a button into the status row, but StatusRow.xaml is in C5's files, and C5 Needs C4. C4's try-this says check "history is dimmed", but HistoryView arrives in C6. Step 6 then keeps raising an untried item for two sessions.

Fix: Move StatusRow.xaml to C4's Files and have C5 extend it with BusyState. End C4's try-this at "check `Ans` is gone", and add "check earlier lines are dimmed after a precision change" to C6's.

## 10. [minor] TODO.md D4 (lines 482-484), D briefs (461-480), F5 (568-569); PLAN.md line 178

Problem: D4 says "raise F5's limits to suit", but F5 comes after D4 in the recommended order, and F5's brief states no limit to raise. D1 to D4 have no Needs at all, though D2's baseline is "after D1" and D4 edits B10's constants and C4's presets.

Fix: Replace the D4 sentence with: "Record in WORKLOG the largest ModPow size under 10 s. F5 reads it to set its `isprime` warning." Add to F5: "warn above that size". Add Needs: D2 needs D1; D4 needs D1, D2, D3, B10, C4.

## 11. [minor] TODO.md H3 (632), W0 (515) against W4 (532), G4 (591), H4 (635), H5 (638)

Problem: Stated Needs miss real edges, which matters as soon as anyone reorders. H3 Needs only C8, but evaluating `M` needs H1's name lookup, since B7's `eval` carries only `ans` (the survey's rank 9 calls memory "short-named variables"). W4 uses C4's top bar, C5's status row and B10, but the W chain stops at C3. G4's toggle, H4's sheet and H5's dimmed load are window work, but their chains reach only B6 or B9.

Fix: H3 Needs H1, C8. W0 Needs T4, C8. G4 Needs B6, C8. H4 and H5 Need H1, C8.

## 12. [minor] TODO.md B1 (198), B8 (332), B9 (350), C1 (382); PLAN.md line 78

Problem: B8 spawns `calc-worker` from `AppContext.BaseDirectory`, but only the App gets the skeleton's copy step, and B1 keeps that "for later", with C1 the first brief to mention it. B8's real-exe tests in Calc.Client.Tests and B9's Calc.Cli hit file-not-found first. PLAN's table says Cli may reference only Client.

Fix: B8: add the `ReferenceOutputAssembly="false"` Worker reference and CopyWorker target to `tests/Calc.Client.Tests`. B9: the same for `Calc.Cli`. PLAN line 78: "Client, plus Worker as a build-only reference".

## 13. [minor] TODO.md C4 (lines 412-413), B5 (271), B9 (350)

Problem: Outside my angle, but it turns C4's own test red. `(p−1)·1292913986 >> 32` is right for every preset but wrong for 66,011,104 of the 2^30 values of p (6.15%), first at p = 70,778, where it gives 21,305 and the true value is 21,306. C4 tests random p to 2^30. Order problem too: B5 needs this formula first, and B9's `:digits 100` needs the reverse conversion that only C4 defines. Probe: C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\critique\deps-order\digitsfix.cs

Fix: Use `Math.BigMul((ulong)(p-1), 0x4D104D427DE7FBCCUL, out _)`, checked against a 96-bit reference for every p to 2^30 with 0 wrong. Create `Calc.Protocol/Digits.cs` in B5, holding both directions, so B9 and C4 share it.

## 14. [minor] TODO.md C5 (line 426), B6 (295, 299), B4 (250-252)

Problem: C5's try-this can't be done. `3^(3^20)` is about 5.5×10^9 bits, over B6's 2^24-bit budget, so it falls to float and B4's bounds route. Timed on the private copy, both bounds take 10 to 15 ms, under C5's 200 ms threshold, so Stop never appears. Probe: C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\critique\deps-order\c5try.cs

Fix: Replace it with: set custom 262144 bits, enter `sqrt(2)` (about 30 s, report 2), press Stop, then type `1+1`.

## 15. [minor] TODO.md step 1 (11), step 7 (21), B1 Files (194), B5 (285), line 54

Problem: Every Calc session reads and writes a WORKLOG that no session creates: B1's file list omits it, though B5 prints into it. Step 7 says push, but the pre-step is only `git init`, so Calc has no remote and no session creates one.

Fix: Add `WORKLOG.md` to B1's Files. Line 54: "Matthew runs `git init` in `Calc` and sets up its GitHub remote. Until `origin` exists, Calc sessions commit and skip the push."

## 16. [minor] PLAN.md line 29; TODO.md B9 (line 350)

Problem: PLAN promises stage B's console calculator has Stop. B9's Cli command list has no way to stop a running sum; B8's Stop is reachable only from code. Ctrl+C ends the whole console and loses the history.

Fix: Add to B9's Cli: "Ctrl+C during an evaluation calls Stop and returns to the prompt, with history intact", with a test on a real long multiply.

## 17. [minor] PLAN.md lines 111, 113; TODO.md C3 (399), B9 (353)

Problem: PLAN says "pending operation" is Gone on a precision change, but C3's keys "never hold their own state". A half-typed `2 +` is entry text, which line 113 says is Kept. No session can deliver that row, and B9's tests are told to cover each row.

Fix: Delete "pending operation" from PLAN line 111.

## 18. [minor] TODO.md table (503, 505); TRANSCENDENTALS-PLAN.md T5 (378), T11 (412), T13 (423), section 6 (255, 257)

Problem: The T table matches section 12 exactly, but two entries understate what's used. T11 lists oracle work and "the same recursion" as expm1, but `TranscendentalOracle.cs` is started in T5 and the recursion is built there, while T11 says only T4. T13 uses "the same block sum" from T6, while T13 says only T11. The recommended order is safe; running trig early, as section 13's stages invite, isn't.

Fix: In both places, make T11's dependency "T5" and T13's "T6, T11". The recommended order doesn't change.

## 19. [minor] TODO.md line 461; PLAN.md line 198; ../Natural/TODO.md line 582

Problem: TODO says all three D sessions need question 5, while PLAN says D1 does. Natural's brief ties the rule question to the adder alone. A "no" would wrongly block Karatsuba and short division.

Fix: TODO line 461: "D1 needs question 5. D2 and D3 don't: on a no, they run on the present adder, and D2's baseline is remeasured."

## 20. [minor] TODO.md I14 (line 666); PLAN.md lines 36, 38; TRANSCENDENTALS-PLAN.md lines 104-106, 467-468

Problem: I14 is counted as one session but holds at least three (Newton division made correctly rounded, T21, then T22 plus divide-and-conquer radix text), which fails the one-chat bar and undercounts the 84. It has no Needs, though section 14 requires 7b. It also omits Newton root: at 1,048,576 bits, sqrt is 140 of 286 s, so "long constants" isn't met by division alone.

Fix: Split it: I14 Newton Divide and Sqrt, correctly rounded (Needs D2). I15 T21 (Needs I14, T11). I16 T22 and radix text (Needs I14). Change PLAN to I = 16 and 86 sessions.

## 21. [minor] TODO.md I12 (line 664); reports/5-extras.md rank 19

Problem: "Uncertainty input" survives only as words in a title. Neither the brief nor the survey says what it is, and it is the only reason I12 needs G4. An agent has nothing to build from, and no statement says it was dropped.

Fix: Add "`1.5±0.1` reads as an IntervalValue; `±` joins B2's tokens", or drop it from I12's title, drop G4 from its Needs, and list it in PLAN's left-out section.

## 22. [minor] TODO.md B3 (225), B4 (243), W0 (515), W6 (540), F4 (562)

Problem: B3 parses `!` and the bit operators, but B4's IArithmetic list has neither, and nothing says what `5!` does before F4. V0.1 ships an operator with undefined behaviour. W6 calls the Scientific tab complete, yet `n!` and `nCr` have no key in any brief.

Fix: B4: add factorial and the bit operators to IArithmetic, with `!` reporting `NotAvailable` until F4. F4: add `n!`, `nCr` and `nPr` keys to the Scientific tab.

## 23. [minor] TODO.md lines 6-7, 21, 89-101, 512; TRANSCENDENTALS-PLAN.md line 341

Problem: Bookkeeping across the two repos isn't defined. Natural sessions run in Natural's chat and tick Natural's TODO, while Calc's T lines hold up to four sessions on one checkbox, so a W agent checking "Needs: T6" sees it unticked. Line 512 lets a W session share the T session's chat, which contradicts lines 6-7: that chat sits in Natural's folder, without Calc's memory.

Fix: Add to step 7: "Natural sessions tick Natural's TODO. A W session checks there, then ticks that T group here." Change line 512 to: "Each is small, and runs in its own chat in the Calc folder."
