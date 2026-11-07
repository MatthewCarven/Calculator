# session-size

Verdict: B4 and B6 are 9b-sized and sit on the path to the first release, about a dozen later sessions are two jobs under one name, and stages G, H and I are sketches a cold agent can't run as written. Corrected totals are 98 sessions, 23 to the first release, against the plan's 84 and 20.

## 1. [blocker] Calc/TODO.md, B4, lines 237-264

Problem: B4 is about 830 production and 720 test lines (±25%, in Natural's style, which is 24% comments and 11% blank). Production: Evaluator 160; FloatArithmetic's operators, literals and two policies 240; integer powers by two routes 120; FunctionTable 120; ten functions 70; ErrorMap 70; Value, Target and IArithmetic 50. Tests: RationalOracle 180, tree generator 100, double, float and all-mode runs 200, powers 100, functions and error kinds 140. That is 1,550 lines and six concerns, the size of 9b (798 + 809), which took 109 turns and 415k of context and stopped on hold with its battery unfinished. Natural's batteries ran one mutation per 7 to 35 production lines, so B4 wants at least 25, not the 7 listed. Also, B6's done-when needs a real `calc-worker --eval`, but B1's is hard-coded and only B7 builds the real one.

Fix: Split it. B4a Evaluator and float core: Value, Target, IArithmetic, Evaluator, ErrorMap, literals, + − × ÷, negate, both policies, RationalOracle, the tree generator, the double, float and all-mode oracles, and `calc-worker --eval` evaluating for real. About 490 + 500. B4b Functions and powers: FunctionTable with `NotAvailable`, the ten functions, mod, rem, percent, `^` by both routes. About 340 + 220. Order: B4a, B4b, B5, B6. Cut B7's `--eval` line down to the `--bits` and `--mode` flags.

## 2. [blocker] Calc/TODO.md, B6, lines 287-309

Problem: B6 is about 770 production and 620 test lines. Production: IntValue and RatioValue 60; literals with the size estimate across every B2 number form 80; + − × ÷ over int and ratio pairs with budget checks 120; mixed operands and fallback 70; `^` 80; sqrt 30; floor, ceil, trunc, round, mod, rem, comparisons, abs, sign, max, min 150; bit operators 50; previews in ResultText 70; budgets, tuning and off switch 60. At 1,390 lines it matches 5a (679 + 723, the largest session that closed cleanly, at max) and passes 9a (658 + 455). The research's own table costs exact-first at M, two or three sessions (report 5, row 1). The TODO gives it one.

Fix: Split it. B6a Exact values and operators: IntValue, RatioValue, literals, + − × ÷, `^`, budgets, fallback with Inexact, to-float, mixed operands, off switch. About 470 + 350, keeping all five listed mutations. B6b Exact functions and display: sqrt by SqrtRem, floor, ceil, trunc, round, mod, rem, comparisons, bit operators, ratio and big-integer previews. About 300 + 270. B6's done-when moves to B6b.

## 3. [major] Calc/TODO.md, A1, lines 157-162; Calc/PLAN.md line 20

Problem: A1 says 9b is finished and offers a fresh battery only if results can't be found. The 9b chat's last message (transcript 36ea4d6b, 2026-10-14) reports 14 of 15 red in batch 1, one survivor that was a real hole, 44 mutations put on hold, and the WORKLOG, notebook, memory and commit still to do. So the job is a 59-mutation battery, the largest in Natural's history (9a had 30). Nothing in Natural's repo or memory records this, and that chat's scratchpad is gone, so a cold agent would write 59 mutations from scratch. B1 and everything after it wait on A1.

Fix: Rewrite A1's state: 9b's code is done, its battery is not. Step 2 becomes: recover `mutate.py` from `~/.claude/projects/C--Users-Admin-Desktop---Programming---Natural/36ea4d6b-3562-4b2d-9ac5-e7068000cee2.jsonl` (a Write at 2026-10-14T14:25:44 and two Edits), rerun S13 (its test fix is already in `StyledTextFuzz.cs`), run the other 44 in batches under ten minutes, then write the WORKLOG entry. Change PLAN.md line 20 to match.

## 4. [major] Calc/TODO.md, stages G, H and I, lines 573-666; 'How a session runs' step 7, lines 21-22

Problem: Stage B briefs run 134 to 406 words. H2 has 14 words for about 300 lines, H4 24 for 450, H6 34 for 450, H8 26 for 650, I5 17 for 550, I6 13 for 300, I9 25 for 640, I10 12 for 300, I11 24 for 700. No G, H or I brief has a done-when. Only G4 names files. All 14 I briefs lack a goal, tests and mutations. So a cold agent at extra must design each feature and build it in one pass, which is not the bar the plan claims in TODO line 3.

Fix: Add to step 7: 'Before finishing, if the next session's brief is under 100 words, write it out in stage B's format (Goal, Needs, Files, How, Traps, Tests, Mutations, Done when) against the code as it now stands, and commit it.' Natural's memory already records this handoff. If stages are swapped and a session starts on a sketch, writing the brief is its first step and counts against its budget.

## 5. [major] Calc/TODO.md, B1 lines 191-204, B2 line 209, B3 line 224, H1 line 625, H4 line 635, I13 line 665; Calc/PLAN.md lines 72-79

Problem: The lexer and parser go in Calc.Engine, which references Natural, so the Client can't use them (rule 1, PLAN line 77). But H1's client 'sends those a request needs', H4 is client-side and must find the token at the caret, and I13 must know which lines a line references. Each then needs a worker round trip or a second hand-written scanner, about 60 to 100 lines each, in briefs that don't mention it. B2 already says no Natural call appears in this code, and numbers stay raw text, so nothing forces it into the Engine.

Fix: Add a project at B1, `Calc.Syntax` (net10.0, no references), holding Token, Lexer, CalcError, Ast, Parser and Printer, referenced by Engine and Client. Update B2's and B3's Files lines and PLAN.md's project table. That's about ten lines now.

## 6. [major] Calc/TODO.md, C7, lines 438-448

Problem: C7 is about 730 production and 150 test lines across four projects, on the way to the first release. The brief leans on a 23-line spike (`wpf-spike.cs` lines 27-49) that has no scrolling, selection, gutter, grouping, jump or theme. The real DigitView is about 350 lines, DigitsWindow 120, line maths in the Client 80, the worker's `digits` request with sliced StartFormat and progress 120, the over-5-s warning and hex offer 60. That is a headless worker job and a custom WPF control under one name.

Fix: Split it. C7a Full digits from the worker: the `digits` request, StartFormat slices, progress, the warning, the hex offer, tests on the real exe. About 220 + 100, no WPF. C7b Digit viewer: DigitView, DigitsWindow, line maths, jump, selection, copy. About 510 + 50, needs C7a and C6.

## 7. [major] Calc/TODO.md, G4 lines 589-593, G5 lines 595-598

Problem: G4's Files line stops at the engine, but its last sentence adds a window toggle: a request field, worker, session state and a result chip. That makes about 470 production and 350 test lines across five projects, and 'Needs: B6' leaves out the window. G5 is 62 words for 26 functions, about 500 + 400 lines. 17 are monotone and table-driven. Nine (sin, cos, tan, cosh, x^y, hypot, atan2, log(x,b), even roots) need extrema, poles, corners or a branch cut, plus exact extrema in degrees.

Fix: G4 Intervals, engine: IntervalArithmetic, IntervalValue, proven digits, an `--eval` flag. About 330 + 300, needs B6. G4w Proven digits in the window: about 140 + 50, needs G4 and C8. G5a Monotone functions, table-driven with domain edges: about 180 + 180. G5b Non-monotone functions: the nine, with extrema, poles and angle units, about 320 + 220. G7 and G8 then need G4w.

## 8. [major] Calc/TODO.md, H1, lines 623-626

Problem: H1 is 91 words with no files line and no done-when, for about 560 production and 400 test lines: four grammar additions (assignment, `f(x) =`, comparisons, `if`) 120; environments, call frames, lazy `if` and the depth cap 150; comparisons in both backends 60; definitions on the wire and in the worker 70; the client's store and `x = Ans` as a line reference 120; replies saying a line was a definition 40. That is near 9a's size with far more concerns.

Fix: Split it. H1a Comparisons and if: the operators in the gap B3 left, a boolean result, lazy `if(c, a, b)`, in Float and Exact. About 200 + 150. H1b Variables and functions: assignment, definitions, environments, recursion cap, definitions in requests, the client store, `x = Ans`. About 360 + 250, needs H1a. H2, H4 and H5 then need H1b.

## 9. [major] Calc/TODO.md, H6, lines 640-641; B8 line 333

Problem: H6 is two unrelated jobs in 34 words, a settings panel and a live preview, about 450 + 120 lines. The preview also hides a design problem. B8's Stop is 150 ms then a kill, and a cold restart is 62 to 119 ms (report 3). So 'stopping the previous preview on each keystroke' can kill the worker at typing speed whenever a slow exact term such as `3^(3^20)` is in the entry, at any precision. The worker has one compute thread, so a preview can also hold up a real Enter.

Fix: Split it. H6a Display settings: notation, grouping, theme, digits shown. About 250 + 70. H6b Live preview: about 200 + 100. H6b's brief must say previews use the soft stop only and never the kill, drop stale ids, skip anything preflight says is over a small budget, and always give way to a real Enter.

## 10. [major] Calc/TODO.md, I14, line 666; TRANSCENDENTALS-PLAN.md lines 451-452 and 467

Problem: One 42-word bullet holds five Natural projects: correctly rounded Newton division, Newton square root (section 14 pairs it with division and the bullet omits it), divide-and-conquer radix text both ways, T21 bit-burst and T22 streaming digits. That's about 900 production and 1,500 test lines, the size of 5a and 5b together.

Fix: Five sessions, in dependency order: I14a Newton division, correctly rounded; I14b Newton square root; I14c Divide-and-conquer radix text; I14d T21; I14e T22. Each gets a T-format brief and goes in Natural's order of work.

## 11. [major] Calc/TODO.md, I11, line 663

Problem: 24 words for about 700 production and 500 test lines, and 'friends' is undefined. Gamma alone needs exact Bernoulli numbers, Stirling with a tail bound, argument shifting and reflection, all carried through enclosures. Natural's `Ball` is internal and rule 2 bars it, so this runs on G4's intervals and needs G5's functions, but the Needs line names only G6 and W6.

Fix: Split it and name the functions. I11a Gamma family: gamma, lgamma, beta, non-integer `!`. About 450 + 300. I11b Error functions: erf, erfc. About 250 + 200. Add G5b to Needs.

## 12. [major] Calc/TODO.md, I5 line 657, I9 line 661, I13 line 665

Problem: Each is two jobs in one line. I5, about 550 + 350: a new value kind (syntax, printer, previews, lossless text, element-wise dispatch in both backends) plus det, inverse and solve, each exact and float. I9, about 640 + 200: sampling in the worker (batch request, chunks, per-point errors, Stop) plus the canvas, where PlotCanvas's 251 lines are only a start. Report 5 costs plotting L, four or more sessions, and the TODO gives it two. I13, about 500 + 300: line references plus a dependency graph that reruns through a worker that can be killed. Report 5 costs it M.

Fix: I5a Matrix values: syntax, value kind, + − ×, transpose, display, lossless, about 300 + 200. I5b Linear algebra: Bareiss det, inverse, solve, about 250 + 150. I9a Plot sampling, no WPF, about 200 + 120. I9b Plot canvas, about 440 + 80. I13a Line references, about 150 + 100. I13b Live recalculation, about 350 + 200.

## 13. [minor] Calc/TODO.md, H8 lines 646-647, F1 line 545, B3 line 225

Problem: 26 words for about 650 + 250 lines if it handles `60 km/h to m/s`, which anyone will try, and the brief doesn't say whether it must. A unit value is a new kind that every operator and preview meets, plus about 150 units and prefixes, each an exact rational to check. `in` is inches here and a keyword in F1's `in hex`, and B3's precedence table has no place for `to`. Report 5 costs units M.

Fix: Ask Matthew once whether units multiply. If yes: H8a Unit values (UnitValue, dimensions, `to` at the loosest precedence), about 350 + 130, and H8b Unit table (units, prefixes, temperature offsets, every factor tested), about 300 + 120. If conversion only, one session of about 400 + 200, and the total below drops by one. Either way reserve `to` in B3 and use `to hex` in F1, so `in` stays free.

## 14. [minor] Calc/TODO.md, W4, lines 531-534; Calc/PLAN.md lines 109-117

Problem: 74 words for about 420 + 250 lines and four jobs across five projects: the functions; an Angle setting through request, session, settings and top bar; postfix `°` in lexer, parser and printer; and two warnings needing a new reply, a minutes estimate and a continue prompt. PLAN's clear table has no Angle row, so nothing says whether changing it clears or dims history.

Fix: W4 Trig and Angle: about 260 + 170. W4b Huge-argument warnings: about 160 + 80, needs W4. Add a clear-table row: Angle is a setting, doesn't clear, and history dims as it does for a mode change.

## 15. [minor] TRANSCENDENTALS-PLAN.md T9, lines 399-402; Calc/TODO.md line 501

Problem: T9 has three algorithms, each with its own oracle: digit-by-digit cube root, Newton nth root with .NET's special-case table, and Hypot. 6c did one root in 136 + 432 lines, 54 turns and 21 mutations. T9 is about 300 + 850 lines, past 9a's total.

Fix: T9a Cube root: Magnitude.CubeRoot, ApInt.Cbrt, ApFloat.Cbrt, about 140 + 430. T9b RootN and Hypot: ApInt.Root, RootN, Hypot, `IRootFunctions`, about 160 + 420. T10 needs T9b.

## 16. [minor] Calc/TODO.md, D1 lines 464-468, D3 lines 476-480

Problem: Too small to stand alone. D1 is about 12 production and 40 test lines, D3 about 30 + 60 (`throwaway-fast-magnitude.diff` lines 30-43 and 99-123). Same file, same old-against-new timing. Session 8 did 149 + 188 with four features in 28 turns, so a fresh chat for 12 lines is mostly ceremony.

Fix: Merge D3 into D1 as 'D1 Adder and short division'. D2 stays Karatsuba and squaring, about 110 + 120. D4 becomes D3. Update the cross-references.

## 17. [minor] TRANSCENDENTALS-PLAN.md T15 and T16, lines 432-438; Calc/TODO.md lines 101, 507-508

Problem: Too small to stand alone. T15 and T16 are 44 and 36 words, about 120 + 180 and 130 + 180 lines, each a few formulas over `Expm1Of` or `LogP1`. Together they're the size of 6b, and section 13 already calls them small.

Fix: Merge into T15 Hyperbolics: six functions and `IHyperbolicFunctions`, needs T8.

## 18. [minor] Calc/TODO.md, line 512 against lines 6-7 and rule 15; W2 lines 524-525, W5 lines 536-537, W6 lines 539-540

Problem: W0 lights keys from the worker's function table, so W2, W5 and W6 are about 10 production lines each: table rows and a few tests. Line 512 says a W session can share a chat with the T session before it, but lines 6-7 put T sessions in a chat opened in Natural, where Calc's memory doesn't load, and rule 15 bars building both at once. So each is a full cold start for 10 lines.

Fix: Merge W2 into W3 (after T10) and W5 into W6 (after T15). Replace line 512 with 'W sessions run in Calc, in their own chat.' Add to PLAN's risk row that the gap between lit keys grows to five T sessions before W3.

## 19. [minor] Calc/PLAN.md lines 14-15, 26-38; Calc/TODO.md order of work, lines 52-147

Problem: PLAN's 84 and 20 don't survive the splits and merges above. Ruler, from git and the chat logs at max (production + test lines, turns, context used): 8 149+188, 28, 37k; 6a 219+266, 36, 80k; 6b 230+427, 24, 71k; 6c 136+432, 54, 112k; 7 290+407, 87, 155k; 9a 658+455, 52, 118k; 5a 679+723, 64, 167k; 9b 798+809, 109, 415k. One chat held 6c, 7, 8 and 9a and chats reached 780k of context, so the window isn't the limit; concerns per pass are. I flagged anything past about 1,100 lines, or past about 800 with two jobs that split cleanly. Report 5's cost key gives nine features more sessions than the TODO does (rows 1, 10, 14, 18, 20, 21, 22, 26, 27). The changes above fix rows 1, 20, 21 and 26. Rows 10, 14, 18, 22 and 27 stay at one because they fit the ruler. Everything else, as production + test estimates: A2 60+200; B1 150+60; B2 290+180; B3 440+350; B5 350+280; B7 370+250; B8 280+300; B9 410+300; B10 320+180; C1 385+40; C2 390+150; C3 350+120; C4 310+180; C5 190+100; C6 310+120; C8 180+60; D4 30+0; T1 180+300; T2 250+350; T3 290+250; T4 120+150; T5 290+450; T6 270+300; T7 300+350; T8 350+350; T10 300+400; T11 300+400; T12 250+300; T13 250+300; T14 200+250; W0 330+120; W1 80+80; W3 120+100; F1 180+150; F2 380+300; F3 550+150; F4 220+250; F5 250+200; G1 150+300; G2 420+150; G3 420+200; G6 150+120; G7 250+80; G8 400+100; G9 350+80; G10 180+120; H2 300+80; H3 120+60; H4 450+120; H5 280+150; H7 380+200; I1 200+150; I2 380+200; I3 450+300; I4 450+250; I6 300+80; I7 300+200; I8 300+120; I10 300+80; I12 300+200.

Fix: New stage table: A 2; B 12 (was 10); C 9 (8); D 2 + 1 (3 + 1); E 16 + 6 (16 + 7); F 5; G 12 (10); H 11 (8); I 22 (14). Totals: 98 sessions (84), 23 to the first release (20), 76 without optional stage I (70). T stays at 16 because T9 splits and T15 and T16 merge. T17 to T20 stay uncounted and would add four. Change PLAN.md line 38 and renumber TODO's order of work to match.
