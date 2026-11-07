# Research behind the plan

Written 2026-11-07 by five planning agents, working read-only against scratch copies of Natural. Nothing in Natural was
changed. Copied here because the session scratchpad doesn't survive.

These are working notes and throwaway code, not project code. The `.cs` files are .NET file-based apps or spikes. Their first
lines hold `#:project` paths into a scratchpad that's gone, so repoint them before running. Nothing in this folder is
part of the Calc build.

## Reports

| File | What it answers |
|---|---|
| [reports/1-natural-api-gaps.md](reports/1-natural-api-gaps.md) | What Natural offers a calculator, the traps, the exceptions, the extremes, the bugs |
| [reports/2-measured-speed.md](reports/2-measured-speed.md) | Timings at every precision, prototypes of the functions, what speed work would buy |
| [reports/3-ui-stack.md](reports/3-ui-stack.md) | WPF against the others, and thread against process against step points |
| [reports/4-transcendentals-summary.md](reports/4-transcendentals-summary.md) | How exp, log and trig should be built, in 22 sessions |
| [reports/5-extras.md](reports/5-extras.md) | The ranked features, what the clear should mean, what to skip |

Five critics then attacked the first draft of the plan and found 111 problems, and two more reviewers checked the second draft and
found 50. Their findings, with the evidence, are in [reports/review/](reports/review/), the two `final-` files being the last. They
use the session numbers of the draft they reviewed. PLAN.md and TODO.md already include the fixes. Where a report above disagrees with
PLAN.md or TODO.md, the plan wins: the reviews corrected several of its numbers.

## Working files

| Folder | Holds | Use it for |
|---|---|---|
| `transcendentals/` | `TRANSCENDENTALS-PLAN.md` (the full design and T briefs), `Proto.cs`, `oracle.py`, `thresholds.py`, `dotnetapi.cs` | Every T session. A1 copies this folder into Natural, and that copy is the live one from then on |
| `natural-api/` | `OnTop.cs` (every recipe on the public API), `Loose.cs` (chunked radix), `Traps.cs`, `Extremes.cs`, `oracle.py`, `notes.txt`, `surface-public.txt`, `surface-internal.txt`, `mutate-9b.py` (session 9b's battery, recovered from its chat) | Stage B and stage F; A1 for the battery |
| `bench/` | `bench.cs`, `analyse.py`, `throwaway-fast-magnitude.diff` | Stage D. The diff is a quick estimate, not code to paste |
| `extras/` | `demo.cs`: one evaluator over float, format, interval and exact backends | B4, B7, stage G |
| `ui/` | `wpf-spike.cs` (`DigitView`, Fluent by hand), `worker-thread-process-spike.cs`, `step-points-spike.cs`, `winforms-spike.cs`, `skeleton/` | B1, B9, B10, stage C |

## Known limits of this research

- Timings are from one laptop with two speeds, ±25%. A slow phase cuts the precision ceilings by about 1.7× in bits.
- Function timings come from prototypes with x in (0, 1). Real builds add argument reduction and correct rounding.
- The prototypes covered exp, ln, sin, cos, atan and the constants. Asin, acos, the hyperbolics and general `pow` were not run.
- The worst-case carry patterns, a real high-DPI screen and the desktop machine were not tested.
- Competitors' features come from their published pages. None was run.
