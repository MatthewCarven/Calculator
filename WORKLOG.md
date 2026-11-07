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

**Next:** A1, in a fresh chat opened in the Natural folder, at "extra" effort with ultracode off.
