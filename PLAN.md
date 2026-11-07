# Calc: the plan

A Windows desktop calculator whose number type is [Natural](../Natural/README.md). You choose the precision, the
window never freezes however long a sum takes, and it can show how much of an answer is really true.

Written 2026-11-07. This file is the what and the why. [TODO.md](TODO.md) is the build: the order of work and a brief
for every session, each small enough for one chat at "extra" effort with ultracode off. The measurements and spikes
behind every number here are in [docs/research/](docs/research/README.md).

## The short version

Four findings shaped this plan:

1. **Natural can't do sin, log or π yet.** Its own TODO calls them "a project of its own". They're planned here as 16 small
   sessions in Natural, correctly rounded like everything else in it. A throwaway prototype of the method got 2,516 of 2,516
   results right against an independent oracle, so the risk is low. The calculator doesn't wait: it works without them, and
   the scientific keys light up as each group lands.
2. **Speed sets the precision ceiling.** Multiply is still shift-and-add. Today 4,096 bits (1,232 digits) is the safe top. Two
   small Natural sessions lift it to 16,384 bits (4,931 digits).
3. **The GitHub repo is behind your folder, and session 9b isn't quite finished.** Three local commits aren't pushed. 9b's code
   is done and all 682 tests pass, but it's uncommitted, and its mutation battery stopped on 14 October with 44 mutations still
   to run, waiting for a match to end. Calc needs that work, so A1 finishes it, and Calc builds against the local folder.
4. **Natural has three bugs that matter to a calculator.** `1e2776511644261678567` parses, then compares as less than 1 (confirmed
   by a separate check). You can reach that one and a remainder bug by typing, and the third if binary display ever uses
   ApFloat's `"B"`. They come first, in session A2.

| Stage | Where | Sessions | What you have at the end |
|---|---|---|---|
| A. Ground | Natural | 2 | Natural committed, pushed and true to its docs, with the three bugs fixed |
| B. Engine | Calc | 12 | A console calculator: precision, rounding, exact fractions, history, clear, Stop |
| **C. Window** | Calc | 9 | **The first real release:** window, keypad, precision selector, history, long-digit viewer |
| D. Speed | Natural, then Calc | 2 + 1 | Whole operations 3 to 13 times faster; safe ceiling 16,384 bits |
| E. Science | Natural + Calc | 16 + 6 | Every scientific key, degrees included |
| F. Programmer | Calc | 5 | Any word size, any base, a clickable bit grid, number theory |
| G. Truth tools | Calc | 12 | Features nothing surveyed offers together |
| H. Comfort | Calc | 11 | Variables, functions, memory, autocomplete, saved sessions, RPN, units |
| I. Depth | Calc, last five in Natural | 22 | Complex numbers, matrices, statistics, solver, integration, plotting, gamma |

That's 98 sessions for everything, 23 to the first release, and 76 without the optional stage I. Stages F and H don't need E,
and in G only the two interval-function sessions do, so you can swap them in whenever a run of Natural sessions gets dull.

## What the research measured

Median timings on this laptop, ±25%. "Digits" is what every value at that precision is guaranteed to hold,
floor((p−1)·log10 2). The sin column is a rough prototype, on today's library and with the speed work added.

| Bits | Digits | Multiply | Divide | Sqrt | sin(x) today | sin(x) after stage D |
|---|---|---|---|---|---|---|
| 256 | 76 | 14 µs | 11 µs | 38 µs | 0.7 ms | |
| 1,024 | 307 | 154 µs | 290 µs | 481 µs | 8.4 ms | |
| 4,096 | 1,232 | 2.7 ms | 4.8 ms | 7.0 ms | 152 ms | |
| 16,384 | 4,931 | 41 ms | 82 ms | 121 ms | 4.3 s | 0.31 to 0.37 s |
| 65,536 | 19,728 | 864 ms | 1.24 s | 1.83 s | not run | 5.0 to 5.4 s |

The other numbers that drove decisions:

- **Stopping work:** `Thread.Abort` is gone and `Thread.Interrupt` does nothing, so a running multiply can't be stopped
  in-process. Killing a worker process mid-multiply at 2,000,000 bits took 2 to 3 ms, and the next answer came about 100 ms
  later. Stop adds a 150 ms grace period first, so it feels like about a quarter of a second. The window held 583 to 585 frames
  in 4 s throughout.
- **Long text:** a wrapped million-digit text box blocked WinForms for 23 to 24 s and WPF for 0.7 to 1.8 s.
- **Contamination:** 1/3 kept at 53 bits, times 3 at 256 bits, gives 0.99999999999999994 while claiming 256 bits.

## How it's put together

```
  ┌───────────────────────────────┐    text lines    ┌────────────────────────────────┐
  │  Calc.App      WPF window     │     on stdio     │  calc-worker   separate process│
  │  Calc.Client   session, state │ ───────────────▶ │  Calc.Engine   evaluator       │
  │  (no Natural anywhere here)   │ ◀─────────────── │  Natural                       │
  └───────────────────────────────┘                  └────────────────────────────────┘
```

| Project | Target | Holds | May reference |
|---|---|---|---|
| `Calc.Syntax` | net10.0 | Lexer, parser, tree, printer | Nothing |
| `Calc.Protocol` | net10.0 | Wire records, `EvalContext`, the digits formula | Nothing |
| `Calc.Engine` | net10.0 | Evaluator, value kinds, text, cost model | Syntax, Protocol, Natural |
| `Calc.Worker` | net10.0 exe | The stdio loop, plus `--eval` for agents | Engine, Protocol |
| `Calc.Client` | net10.0 | Session, history, clear rule, worker control, UI state | Syntax, Protocol |
| `Calc.Cli` | net10.0 exe | A console calculator on `Calc.Client` | Client, plus Worker at build time only |
| `Calc.App` | net10.0-windows | WPF views, the digit viewer, the bit grid, the plot | Client, plus Worker at build time only |

**WPF on .NET 10, with no NuGet packages.** It's the only stack tested that needs no packages, kept every text case under 2 s,
and can render a PNG with no window, which lets an agent check its work. You've also used it before, in Evolutionary Studio,
whose `--smoke` and `PlotCanvas` are worth copying.

**A separate worker process.** Stop sets a flag, waits 150 ms, then kills the process. This costs 18.5 MB and about 0.05 ms a call,
and it works whatever Natural is doing, including future code. A crash or a stack overflow takes out the worker and not your
history. The window can't freeze, because it has no Natural in it to freeze on.

**Text is the truth.** The client keeps expressions and definitions as text, and each history line keeps the settings it was worked
under, in one `EvalContext` record. Numbers are a cache, rebuilt on demand. That's what makes the precision clear lossless, a kill
harmless, and sessions easy to save.

**`Ans` is a line, not only a number.** The keypad writes `Ans` into every chained sum, so recomputing `Ans*4` has to mean recomputing
the line it came from first. Each history line records which line its `Ans` meant, and a recompute replays that chain.

**Calc references Natural as a sibling folder**, through a `NaturalRoot` property defaulting to `..\Natural\`. It's the only route
that sees your latest work. A clone needs both repos side by side; a submodule can replace this later.

**One evaluator, several number backends.** A value is an exact integer, an exact ratio, or a float, and later an interval, a complex
number or a matrix. Exact-first, plain float, IEEE format and interval modes are all the same evaluator with a different backend.

## Precision, and what setting it clears

The selector offers 53, 113, 256 (the default), 512, 1,024, 2,048 and 4,096 bits, plus 8,192 labelled slow, 16,384 once stage D lands,
and a custom box that takes bits (`300`) or digits (`100d`). Each shows both units: "256 bits, 76 digits". An exact answer stays exact
at any precision; there, precision only sets how many digits you see.

Clearing on a change isn't only tidy, it's needed. Natural's values each keep their own precision, so an old low-precision value
quietly spoils a new high-precision answer, and the answer still claims the new precision. So:

**Changing the precision or format drops every computed number and keeps all the text.**

| Thing | On a change | Why |
|---|---|---|
| Result, `Ans`, M registers, RPN stack | **Gone** | Numbers at the old precision |
| Flag lamps, running work | **Gone** | They describe the old calculation |
| Open views of a computed value (digit viewer, inspector, variable values) | **Blank** | Same numbers, shown elsewhere |
| Entry line | Kept | It's text. If it says `Ans`, Enter tells you that answer was cleared, and offers to recompute |
| History | Kept, dimmed, tagged with its old precision | One click recomputes a line, and the `Ans` chain behind it |
| Variables and user functions | Kept as text | Rebuilt at the new precision on next use |
| Cached constants | Kept | Natural keeps π as an enclosure that serves every precision |
| Settings | Kept | |

Picking the same precision again does nothing. Changing the rounding mode, Exact or Angle doesn't clear, but older lines dim, and a
recompute uses each line's own settings, with only the precision replaced. There are also the ordinary keys: **CE** clears the entry, **AC**
clears the entry, result and `Ans`, and a menu item wipes everything, history included. After a change, the status row offers
"Recompute last line at 1,024 bits".

That's "set it clearing the calculator" as you confirmed it on 2026-11-07: setting the precision clears the calculator, and history stays.

## The window

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│ Precision [256 bits, 76 digits ▾]  Rounding [nearest ▾]  Exact [on]  Angle [deg]│
│ ○ Inexact  ○ Overflow  ○ Underflow  ○ Div by zero  ○ Invalid                    │
├──────────────────────────────────────────┬──────────────────────────────────────┤
│ sqrt(2) * 3                              │ History                              │
│ = 4.242640687119285146405066172629094…   │ #12  sqrt(2)*3      4.2426406871…    │
│   float, 76 digits            [Show all] │ #11  1/3            0.3333…  = 1/3   │
├──────────────────────────────────────────┤ #10  2^1000         1.0715E+301      │
│ [Standard] [Scientific] [Programmer]     │ #9   1/7 (at 53 bits, dimmed)        │
│ [Inspector]                              │                                      │
│   AC   CE   ⌫    ÷                       │                                      │
│   7    8    9    ×      (the keypad)     │                                      │
│   ...                                    │                                      │
├──────────────────────────────────────────┴──────────────────────────────────────┤
│ ▓▓▓▓▓▓░░░░░░  1.2 s of about 3 s                                         [Stop] │
└─────────────────────────────────────────────────────────────────────────────────┘
```

## The extras

**The ones this calculator is unusually placed to do,** because Natural rounds correctly in all four modes and any format. Rump's and
Muller's examples run with Exact off; with it on, exact-first gets Rump right outright, as -54767/66192.

1. **Proven digits.** Directed rounding gives a true interval around every result, so the calculator can show how many digits are
   proven. Rump's famous example shows 0 proven digits at 121 bits, then 36 at 122.
2. **Digit-loss x-ray.** Bounds on every operation name the one that spent the digits. In Rump's example at 100 bits, operation 18 of 21, a
   `+`, drops 98 proven bits to 0. Nothing surveyed offers this.
3. **Format lab.** One expression run as binary16 to binary256, bfloat16 or any custom format, in four rounding modes, beside the true
   value with the exact error. It shows why 0.1 + 0.2 misbehaves, one format at a time.
4. **N proven digits on demand.** Ask for 40 proven digits; it raises the working precision until they're proven, or says why it can't.
5. **Fooled-by-precision sweep.** Rump gives the same wrong answer at 23 precisions in a row, from 99 to 121 bits, then turns right at 122.
   Muller's sequence reaches exactly 100 at 53 and 106 bits; the truth is 6.00000002. A plain "more digits agree" check would have been
   fooled, so the sweep says "stable", never "correct".
6. **Bit-exact receipts.** Expression, settings and hex significand, which replay to the same bits.

**Also valuable, and cheap here:** exact-first arithmetic; history that recomputes; rounding selector and IEEE flag lamps; the bit inspector
from the Playground; programmer mode at any word size, where Windows stops at 64.

**Ordinary features, all planned:** variables, functions, memory registers, autocomplete, saved sessions, export, RPN, units, statistics, sums
and products, complex numbers, matrices, a solver, numeric integration, plotting, gamma.

**Left out on purpose:**

- Live currency or stocks: needs a network, and a stale rate beside proven digits misleads.
- Natural-language input: `to` conversions cover the useful part.
- Symbolic algebra: bigger than the calculator.
- A scripting language: functions, recursion and `sum` cover calculator use.
- A base-10 float type: exact ratios give decimal exactness, and it would leave Natural.
- As-you-type results at high precision: ten operations cost about 44 ms at 4,096 bits, so above 1,024 bits it runs on Enter.
- Big-prime work past 1,024 bits before stage D: `ModPow` is 52 s at 4,096 bits today.

## Decisions

Taken, because the evidence settles them. Say if you disagree with any:

- **What "correctly rounded" means here.** Each operation is right for the value it receives, and in exact mode that's the exact value you
  typed: `ln(1+1e-80)` at 256 bits is 1e-80, not the 0 you'd get by rounding the argument first. A whole expression isn't rounded once,
  which is what proven digits are for.
- **No hidden guard bits.** What you set is what each operation uses, as Natural does.
- **Display digits** use floor((p−1)·log10 2), so 76 at 256 bits. `DecimalDigitsFor`'s 79 shows rounding noise (…3348).
- **Normal mode behaves like mathematics:** 1/0 is an error. **Format mode behaves like the hardware:** 1/0 is ∞ with its lamp lit.
- **Stop is a process kill.** Natural's jobs give progress, but aren't a Stop: one format slice overran 50 ms to 1.0 s.
- **Natural's API is used as it stands,** plus unit overloads with flags on the trig functions, added in T12 and T14. Everything else the
  calculator needs is public, apart from stopping arithmetic mid-operation, which the worker kill covers. Recipes are in
  `docs/research/natural-api/OnTop.cs`.

Settled by you on 2026-11-07, all six as recommended:

1. **The clear rule.** Numbers go, text stays, history stays dimmed. History isn't wiped.
2. **Exact-first is on by default.** 0.1 + 0.2 is exactly 3/10, and 1/3 × 3 is exactly 1, until something irrational forces a float. Precision then
   applies from that point. A switch turns it off, and the truth tools need it off, because exact answers leave nothing to prove.
3. **WPF with a worker process,** rather than growing the WinForms Playground.
4. **sin, log and π live in Natural, correctly rounded** (T1 to T15), rather than quick versions inside Calc.
5. **The parallel-prefix adder is within Natural's bitwise rule.** It's only XOR, AND, OR and shifts, the same as hardware. That answers the
   question in Natural's TODO, open since October.
6. **The order after the first release is D, E, F, G, H, I.** Science before the truth tools, because a calculator without sin isn't finished, while
   the truth tools are extras. Only G6 and G7 need E, so this is still easy to change.

Seven smaller questions about Natural's trig API are still parked at session T1, with recommendations (`TRANSCENDENTALS-PLAN.md` section 15).

## Risks

| Risk | What the plan does |
|---|---|
| A huge result eats the machine (`2^(10^12)`, `100000000!`) | Exact budgets fall back to float; a preflight refuses work past memory; the worker has a heap cap, the smaller of 4 GiB and free commit less 1 GiB, so running out kills only the worker |
| Values pile up in the client, which has no cap | Anything over 1 MiB of text stays in the worker and is replayed if lost |
| A pasted 10,000-term sum kills the worker | Tree walks run on a 256 MiB stack, and the parser refuses past 200,000 |
| `sin(1e1000000)` needs π to 3.3M bits, about 9 hours today | The library stays right however long it takes; the calculator warns first, using `TrigReductionBits`, and Stop always works |
| A float x isn't the x you typed | A second warning for float arguments. Exact arguments stay exact, so `sin(1e120)` in degrees is right at any precision |
| Ziv's loop never ends on an exact case (`log2(8)`, `16^0.75`) | Every exact case is listed per function in the Natural plan, with a test hook so a miss fails fast |
| A long stretch of Natural sessions with nothing visible | Each group ends in a wiring session that lights up keys, though the longest gap is five T sessions; stages F and H can be swapped in |
| Natural's sessions and Calc's collide on a build | One at a time, or Calc points `NaturalRoot` at a separate worktree |
| A window feature gets built but never tried | Each window session ends in a short try-this list, raised again until it's done |
| A brief turns out wrong | The code wins. Fix the brief, say so, and carry on. If a session runs long, stop at a green commit and split it |
