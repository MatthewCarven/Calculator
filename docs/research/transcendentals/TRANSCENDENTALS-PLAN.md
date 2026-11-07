# Natural, order item 8: the transcendentals

Design and session briefs for exp, log, trig, real power and the constants, which also unlock item 9
(`IFloatingPoint<ApFloat>` and the rest). Written so that each session below can be run by one agent in one
fresh chat, from its brief. Dates and paths: 2026-11-07, against the working tree with 9a and 9b in it.

## Amendments from the plan review (2026-11-07)

Five critics reviewed the Calc plan built on this document. These override the text below:

- **T9 splits:** T9 is `Magnitude.CubeRoot`, `ApInt.Cbrt` and `ApFloat.Cbrt`. T9b is `ApInt.Root`, `RootN`, `Hypot` and `IRootFunctions`, and T10 needs T9b. Three algorithms with their own oracles is past one session.
- **T15 and T16 merge** into T15: the six hyperbolics and `IHyperbolicFunctions`. It needs T8.
- **Dependencies:** T11 needs T5, because `TranscendentalOracle.cs` and the recursion start there. T13 needs T6 and T11, because the block sum comes from T6.
- **T12 and T14 add flag-carrying unit overloads.** Section 10's units form is static only, and a static call has no flags. Calc needs them to tell an exact result: `ieee.Sin(x, unit)`, `Cos`, `Tan`, `SinCos` and the SinPi family in T12, with a format overload; `ieee.Asin(x, unit)`, `Acos`, `Atan` and `Atan2` in T14. Each gets a test: sin(30, Degrees) raises no Inexact in the context.
- **Section 7 is wrong that degrees "don't have this problem at all".** Degrees need no π, but reduction is exact only on the stored x. `sin(10^23)` in degrees, stored at 53 bits, gives 0.53 where the truth is -0.98. The calculator keeps exact arguments whole; a library caller has to do the same.
- **Section 7's calculator messages are replaced by W4 in Calc's TODO:** an accuracy warning for float arguments only, from ulp(x) at 2^-10 of the period, because ulp(x) ≥ 2π fires too late; and "about 3.32M bits" in the cost message.
- **T17 to T20 stay out of launch.** T21 and T22 are Calc's I21 and I22.

## 0. What rests on what

**Run, on a prototype (`Proto.cs`, a Ball on ApFloat's public API, with exp, ln, sin, cos, atan and the
constants; about 600 lines):**
- 2,516 correctly rounded results (5 functions, 8 to 1024 bits, 4 modes) agree with an independent oracle
  (Python `decimal` and integer series, nothing from Natural or .NET). 0 differ. Corrupted results are caught.
- binary32: 2,682 to 4,000 values per function across all 4 modes, 0 wrong against the hardware interval (section 11).
  Every Half input in nearest and a seventh of them in each directed mode: 0 wrong.
- Starved guard (g0 = 2): 35 rounds failed and retried, 0 results changed.
- The published ln worst case takes 2 rounds in directed modes at binary64 with no test hook.
- `log_2(8)`, `log_4(8)`, `4^0.5` and `16^0.75` never settle in a directed mode; in nearest they settle in one
  round without the loop noticing they are exact. An undetected ln(1) hung a probe outright.
- All 13 shortcut rules of section 5, brute-forced with exact fractions (`thresholds.py`): 448,568 rounded checks,
  p from 2 to 53, short and long x, every mode, 0 wrong. Six negative controls all fired, so the test has teeth.
- All timings below, and .NET's interface lists and `double`'s special cases.

The prototype and the oracle live in this folder (`Proto.cs`, `oracle.py`, `thresholds.py`, `dotnetapi.cs`): copy them
beside this plan if it is kept, because the scratchpad dies with the chat.

**Read or derived, not run:** the nudge through `RoundExact` itself (its logic was checked by hand and in fractions, not
in C#), everything for asin, acos, the hyperbolics and general `pow` (only simple compositions were tried),
Payne-Hanek, bit-burst, the cache under threads, the job plumbing. The prefix adder is checked on 2,000,242
word triples and ran the prototype correctly, but I did not run Natural's test suite on it (the bench agent's
files show it ran one). Python's `decimal` is trusted as documented ("correctly rounded") for exp, ln, log10;
the oracle only assumes one unit of error.

**Sources:** the MPFR algorithms document (read: 4.1 to 4.5, 4.15, 4.21 to 4.24, 5.1, 5.3), MPFR's sin.c fast path,
Lefevre's slides on Ziv increments, Johansson's Arb and multi-prime papers (abstracts and slides only), Niven's
theorem and Lindemann-Weierstrass (Wikipedia), Muller's worst-case page.

## 1. Decisions

1. **Correctly rounded everywhere, all four modes, into precisions and formats, with flags.** No faithful
   rounding anywhere. It costs a guard of 32 bits, and a failed round 2^-32 of the time.
2. **The rule for error bounds: no hand analysis of rounding.** Every function computes a `Ball` (mid, radius).
   Rounding error, input error and constants' error propagate mechanically through the ball operations. The
   only thing proved by hand is each series' tail: one inequality, in a comment, added with `Widen`.
3. **A wrong guess of working bits costs time, never correctness.** Only a Ball bug, a tail bug, a reduction bug,
   a shortcut threshold or a missing exact case can give a wrong result, and each has its own tests.
4. **Exact cases come in two kinds.** Isolated points are caught eagerly, before the loop. `x^y` and `log_b x`
   are caught lazily: when a dyadic of at most p + 1 bits lies inside the enclosure, test that one candidate
   with exact integers. It must run in every mode: in nearest an exact result settles without any failure,
   and Inexact would be raised falsely.
5. **Six cores, everything else is composition:** `ExpOf`, `Expm1Of`, `LogOf`/`LogP1Of`, `SinCosOf`, `AtanOf`, and binary
   splitting for constants.
6. **Binary splitting with Chudnovsky for pi; no AGM.** AGM loses to Newton on exp as soon as Karatsuba exists.
7. **Cores are iterators from the first session**, the pattern of `TimesPowerOfTenSteps`, so that no session has to
   rewrite them. `Parse`'s rule holds: one-shot calls drain the same steps.
8. **Do the prefix adder (order item 7's first piece) before all of this.** About ten lines, multiply 2.4 times faster at 64 bits and 3 to 4 from 256 bits up, whole functions 2.3 to 3 times, the batteries included. Nothing in this plan depends on it.

## 2. Measurements

This machine, .NET 10 Release, operands from a pool (a repeated operand trains the branch predictor and flatters
small sizes threefold; note that in any bench). Min of rounds, arms interleaved, A B A B across builds.
"ripple" is today; "prefix" adds the parallel-prefix FullAdd; "+Kar" adds my quick Karatsuba (threshold 24 limbs, a
probe, not a design; the bench agent's own figures are 86 ms at 100k bits and 3.24 s at 1M bits).

Primitives, microseconds:

| bits | mul ripple | mul prefix | mul +Kar | div 2n/n prefix | sqrt 2n prefix | short div prefix |
|---|---|---|---|---|---|---|
| 256 | 19 | 6.1 | | 6.7 | 8.8 | 2.5 |
| 1024 | 262 | 82 | 65 | 102 | 132 | 10 |
| 4096 | 4,500 | 1,135 | 604 | 1,436 | 2,069 | 40 |
| 16384 | 67,800 | 18,000 | 5,500 | 23,100 | 32,200 | 158 |
| 65536 | 1,050,000 | 283,000 | 50,000 | 354,000 | 507,000 | 649 |

Division costs about 1.25 multiplies and a square root about 1.8. That is far cheaper than the textbook ratios, because
both are restoring and multiply is shift-and-add: it is why every route below lands within about 1.5 of the others up
to 4096 bits.

One function, first round at p + 32 bits, Ball included, constants already cached, milliseconds:

| p | digits | exp ripple | exp prefix | exp +Kar | sin prefix | ln prefix (r=4) | atan prefix (h=4) |
|---|---|---|---|---|---|---|---|
| 53 | 16 | 0.16 | 0.069 | | 0.058 | 0.055 | 0.094 |
| 256 | 77 | 0.92 | 0.31 | | 0.25 | 0.28 | 0.44 |
| 1024 | 308 | 9.2 | 3.3 | | 2.7 | 3.4 | 5.7 |
| 4096 | 1,233 | 185 | 66 | | 52 | 66 | 100 |
| 16384 | 4,932 | | 1,410 | 460 | 1,150 | 1,540 (r=8) | 2,020 (h=8) |
| 65536 | 19,728 | | 34,200 | 6,400 | 29,700 | 48,000 (r=8) | 50,800 (h=8) |

At 16384 / 65536 with prefix: ln by AGM 1,190 / 24,800, ln by Newton on exp 1,210 / 31,200, atan by Newton on sincos
1,480 / 34,100. With +Kar: AGM 881 / 15,500, Newton 485 / 6,720, atan Newton 518 / 7,520, ln r=8 707 / 12,000.
Radius bookkeeping is 5 to 15% of the time at 53 bits and under 10% at 256 and 1024 bits (one 4096-bit reading of 25% looks like noise: single runs, and sin showed 2%). Adaptive series precision takes
23 to 33% off exp from 256 bits up. Rectangular splitting beats plain Horner 1.1 / 1.3 / 1.8 / 3.3 times at 53 / 256 /
1024 / 4096 bits.

Constants, cold, milliseconds, ripple (prefix):

| bits | pi Chudnovsky | pi Machin split | pi fixed-point | pi AGM | e | ln 2 three-term | ln 2 MPFR's |
|---|---|---|---|---|---|---|---|
| 256 | 0.7 (0.3) | 1.9 | 4.9 | 4.0 | 0.4 | 1.5 | 1.4 |
| 1024 | 2.0 (0.7) | 6.8 | 11.8 | 9.8 | 0.8 | 4.1 | 5.2 |
| 4096 | 28 (9) | 126 | 179 | 169 | 9 | 63 | 105 |
| 16384 | 431 (147) | 2,206 | 2,645 | 3,090 | 141 | 1,110 | 2,164 |

pi at 65536 bits: 3.6 s prefix, 1.6 s +Kar. At 262,144 bits +Kar: 19 s (split 3.8, sqrt 8.8, divide 6.1). At 1,048,576
bits +Kar: 286 s (split 41, sqrt 140, divide 100, multiply 5). Sqrt and divide dominate, and Karatsuba can't touch them:
that is what Newton division and root (order item 7's third piece) would fix. For scale, Python's own integers give pi to
262,144 bits in 0.15 s: this is what building arithmetic from bits costs.

## 3. The contract and the loop

The promise is "rounded exactly once". A transcendental result is the value `target.Round` gives for **both** ends of a
certified enclosure of the exact value. Rounding is monotone, so the exact value rounds the same way: that is the
exact value rounded once, as session 4's engine argued. Nothing changes for formats: the ends must agree on `RangeOf`
as well.

Termination: if the exact value is not a dyadic of at most p + 1 bits, a tight enough enclosure excludes every rounding
boundary, so the loop ends. So exact-case detection is the only obligation (section 4). There is no exact route to fall
back on, as session 4 had. Cost is unbounded in theory and tiny in practice: a round fails about 2^-g of the time
(measured 5.9e-2, 3.4e-3, 1.8e-4 at g = 4, 8, 12).

```
g = 32 + core's extra
loop:
    W = p + g
    b = enclose(W)
    if lazy check exists and a dyadic v of <= p + 1 bits lies in [lo, hi] and isExact(v):
        result = target.Round(v), flags from that rounding. Done.
    if lo and hi have the same sign, round to the same value, and agree on RangeOf:
        result = that value; flags Inexact, plus Overflow or Underflow from RangeOf. Done.
    lost = max(0, W - b.Bits)             // how far the ball fell short of W: cancellation shows up here
    g = 2g + lost
    rounds > MaxZivRounds: throw InvalidOperationException, as the engine does
```
Doubling g, not W: W doubling costs four times a round at high precision, for a retry that almost never happens; MPFR
adds a word and then grows by half. `lost` matters: sin(pi rounded to 256 bits) has a first ball of 63 bits and took 4
rounds on doubling alone; with `lost` it should take 2 (predicted, not run).

`lo = Subtract(M, R, W + 8, TowardNegative)`, `hi = Add(M, R, W + 8, TowardPositive)`. A radius of +infinity or a ball
containing 0 is "not settled", never an error. Flags on the settled path are forced, as `SettledFlags` does: an end may
happen to be representable, so `target.Round` alone can miss Inexact.

Test hooks, thread-static, read at the start on the caller's thread as `ForcedRoute` is: `ForcedGuard` (start small, so later
rounds run), `MaxZivRounds` (a hole fails fast instead of doubling to 131,072 bits), `ForcedRoute = Interval` (skip shortcuts, to
compare them with the loop). `LastRounds` and `LastExact` as now.

**The nudge.** For a value strictly between v and a neighbouring boundary on a known side: `target.Round` of
`(m << g) - 1` at exponent `e - g` with sticky set, for "a little below in magnitude", or of m with sticky, for "a little
above", with g = max(2, p + 2 - bitlen(m)). All 14 shortcuts share this one helper. Inexact and subnormal rounding then come from
`target.Round`.

## 4. Exact cases, per function

Theorems: **Lindemann-Weierstrass**: e^a is transcendental for algebraic a != 0. So sin, cos, tan, sinh, cosh, tanh of a
non-zero dyadic are transcendental; ln x is for x != 1 (else e^(ln x) = x contradicts it), and asin, acos, atan, asinh,
acosh, atanh likewise except at the row's listed point. Those functions have no other exact cases. **Niven**: at
rational multiples of pi, sin and cos are rational only at 0, +-1/2, +-1, and tan only at 0, +-1. **Unique factorisation** does the
rest (log2, log10, log_b, 2^x, 10^x, x^y); Gelfond-Schneider says nothing useful here, since y is rational.

| function | exact (or settled) cases | detected by |
|---|---|---|
| Exp | ±0 → 1; +∞ → +∞; −∞ → +0 | kind |
| ExpM1 | ±0 → ±0; −∞ → −1 | kind |
| Exp2 | x an integer → 2^x | `IsInteger`, then `target.Round([1], x)` |
| Exp2M1 | ±0; integer x → 2^x − 1 (exact add: Add's gap shortcut settles it) | `IsInteger` |
| Exp10 | x an integer → exact rational 10^x, not dyadic for x < 0 | the certified engine, `FromDecimalSteps` |
| Exp10M1 | ±0; small integer x (10^x fits 2W bits) → exact integer minus 1 | `IsInteger` and size |
| Log | 1 → +0; ±0 → −∞ (DivideByZero); x < 0 → NaN (Invalid); +∞ → +∞ | compare |
| LogP1 | ±0 → ±0; −1 → −∞ (DivideByZero); x < −1 NaN (Invalid) | compare |
| Log2 | x = 2^k → k (k may need rounding) | significand is 1 |
| Log10 | x = 10^k, k ≥ 0 only → k. 10^−k isn't dyadic | k is read off the exponent (x = 5^k·2^k, m odd), then bit length, then compare with 5^k (`FloorLog2Of5Times`) |
| Log2P1 | 1 + x = 2^k | exact add, when top(x) ≥ −1 |
| Log10P1 | 1 + x = 10^k, k ≥ 0 | exact add, then as Log10 |
| Log(x, b) | x = b^v with v dyadic of ≤ p + 1 bits; .NET's specials | lazy: b^i == x^(2^a) for v = i/2^a, with bit lengths first. b = 2^j and x = 2^k: directly, k/j |
| Pow | table in T10. y integer ≥ 0 with m^y ≤ p + 1 bits; m = 1 with e·y integer; y = r/2^a, m a perfect 2^a-th power, r > 0 | integer y: exact; else lazy: v^(2^a) == x^r. r < 0 can only be exact with m = 1 |
| Sqrt, Cbrt | none needed | integer root, remainder as sticky: no loop |
| RootN | n ≤ about 64: as Cbrt. Larger n: lazy, v^n == x | remainder, or lazy |
| Hypot | none needed; (±∞, NaN) → +∞ | exact sum, integer root, sticky |
| Sin, Tan | ±0 → ±0 | kind |
| Cos | ±0 → 1 | kind |
| ±∞ | Sin, Cos, Tan → NaN (Invalid) | kind |
| SinPi | integer n → +0 for n > 0, −0 for n < 0; n + ½ → ±1 | exact x mod 2 |
| CosPi | integer → ±1; n + ½ → +0 | exact x mod 2 |
| TanPi | n → ±0 by parity; n + ½ → ±∞ (DivideByZero); n ± ¼ → ±1 (.NET gives 0.9999999999999999 at ¼; Natural gives 1) | exact x mod 1 |
| Sin(x, Degrees) | x mod 360 in {0, 90, 180, 270} → 0, ±1; {30, 150, 210, 330} → ±½ | exact `Ieee754Remainder` |
| Cos(x, Degrees) | {0, 90, 180, 270} → ±1, 0; {60, 120, 240, 300} → ±½ | same |
| Tan(x, Degrees) | multiples of 45: 0, ±1, poles | same |
| Gradians | as degrees with 400 a turn; ±½ is unreachable (33.3 gon isn't dyadic) | same |
| Asin | ±0 → ±0; ±1 → ±π/2 (settled from the pi ball: no series, and no singularity); abs > 1 NaN | compare |
| Acos | 1 → +0; 0 → π/2; −1 → π | compare |
| Atan | ±0; ±1 → ±π/4; ±∞ → ±π/2 | compare |
| Atan2 | IEEE's table (T13) | kinds |
| AsinPi, AcosPi, AtanPi, Atan2Pi | 0, ±½, ±¼, 1 where Niven allows; ±½ inputs give 1/6, 1/3, non-dyadic, so the loop ends | compare |
| Asin(x, Degrees) etc. | ±½ → ±30; acos ±½ → 60, 120; atan ±1 → ±45 | compare |
| Sinh, Asinh | ±0 → ±0; ±∞ → ±∞ | kind |
| Cosh | ±0 → 1; ±∞ → +∞ | kind |
| Tanh | ±0; ±∞ → ±1, exact | kind |
| Acosh | 1 → +0; below 1 NaN; +∞ → +∞ | compare |
| Atanh | ±0; ±1 → ±∞ (DivideByZero); abs > 1 NaN | compare |
| DegreesToRadians, RadiansToDegrees | ±0, ±∞ only | kind |

Degree mode is the one the calculator meets daily, and sin(30) with x = 30 is an exact 1/2 reached from a rational, non-dyadic
angle: nothing but the table stops the loop. And `Pow(8, 1/3.0)` isn't 2: the exponent was rounded. The calculator's root key must
call `RootN`.

`Inexact` is raised by every result except an exact one, and an exact one that doesn't fit the target rounds with Inexact as usual
(`log2` of 2^(2^60 + 1) at 53 bits).

## 5. Shortcuts near a representable value

Without them the loop still ends, but `sin(2^-1000)` costs 7 rounds and W past 2,000 bits, and `exp(2^-1000000)` W past a million. T =
top(x) (2^T ≤ |x| < 2^(T+1)), e = x's exponent (lowest set bit), p the target's precision.

- **Near 1:** Exp, Exp2, Exp10: `T ≤ −p − 2` → nudge 1 up for x > 0, down for x < 0. Cos: `2T + p + 2 ≤ 0` → down. Cosh: same → up. CosPi and
  others that scale x by π: add 2 to the bound. Pow: when the low-precision ball of y·ln x satisfies Exp's rule.
- **Near x, cubic:** if `3T + 3 ≤ min(e, T − p − 2)`: Sin, Atan, Asinh, Tanh → magnitude a little below x. Tan, Asin, Sinh, Atanh → a little above.
- **Near x, quadratic:** if `2T + 2 ≤ min(e, T − p − 2)`: ExpM1 above x in signed sense, LogP1 below x in signed sense. Log(x) near 1 goes through LogP1 with t = x − 1, exact.
- **Near ±1 at the far end:** Tanh, for x ≥ (p + 2)/2 → ±1 nudged toward zero.
- **Near ½:** AcosPi at tiny x, AtanPi at huge x.

Each bound comes from |f − x| < |x|³ (or x²) for |x| ≤ ½ and must clear both the half-ulp and x's own lowest bit, since a long x need not sit on a boundary. That `min` is
not decoration: dropping `e` gave 236 wrong roundings in the brute force, on long inputs. All brute-forced except the CosPi, Pow, AcosPi and AtanPi lines. Each session's tests must still straddle its
bounds by ±4 places against the loop (`ForcedRoute = Interval`), as the gap shortcut's tests did.

Format range: Exp and friends compute k first, at low precision. Past emax + 1 it's `target.Round` of a value past the top; below the smallest subnormal by 2, of a value with sticky under it, as `SettlesEarly` does. At a
precision, a binary exponent that won't fit a long throws `OverflowException`, as `Parse` and `checked` do.

## 6. Algorithms

Rejected, with numbers: **AGM** (10 lines, and competitive only before Karatsuba, since digit-by-digit sqrt doesn't speed up: 15.5 s against
Newton's 6.7 s at 65536 bits; also needs a rigorous bound on the formula, and MPFR's document gives only little-o). **CORDIC** and other tables
of W-bit constants: a table per W. **Fixed-point Machin** (6 times Chudnovsky at 16384 bits). **Hardware seeds** (`Math.Log`): breaks the bitwise rule.

Series run in Ball, so each is relative-precision and tiny arguments need no special care.

**pi:** Chudnovsky by binary splitting: 47.11 bits a term, then sqrt(10005) and one division. Term a: P = (6a−5)(2a−1)(6a−1), Q = a³·10939058860032000,
T = ±P(13591409 + 545140134a). pi = 426880·sqrt(10005)·Q/T. Tail: terms fall by more than 2^47.
**e:** Sum 1/k!, Q and T only. **ln 2** = 18 atanh(1/26) − 2 atanh(1/4801) + 8 atanh(1/8749). **ln 10** = 46 atanh(1/31) + 34 atanh(1/49) + 20 atanh(1/161). Both identities checked numerically to 250 bits here.
Tau, pi/2, pi/4 are shifts.

One engine: Haible and Papanikolaou's P, Q, B, T form, `P = Pl·Pr`, `Q = Ql·Qr`, `B = Bl·Br`, `T = Br·Qr·Tl + Bl·Pl·Tr`, value T/(B·Q), done bottom-up as a binary counter of merges (one merge a step, no recursion, so it sits well in an iterator).
Exact integers, so the only error is the tail, and truncating T and B·Q to W + 16 bits before the one division (`Ball.Of`).

**ExpOf(Ball v, W):** k = round(v/ln 2), r = v − k·ln 2 with ln 2 at W + bits(k), h = max(0, s + top(r) + 1), t = r/2^h. `e = expm1 t` by rectangular splitting, then h times `e = 2e + e²` (**not** squaring 1 + e: the expm1 form keeps its relative error, so s bits aren't lost), then (1 + e)·2^k. Radius of v: widen by 2δ·result.

Rectangular splitting for expm1, with p[i] = t^i, m about sqrt(N), tm = t^m (the form I ran):
```
B = p[N mod m]
for k = N-1 down to 1:
    B = B / (k+1)                     // scalar: DivSmall
    if (k+1) mod m == 0: B = B * tm   // the only non-scalar steps, about N/m of them
    B = B + p[k mod m]
```
N: `n = 1; bits = 0; while bits < w + 2: n++; bits += -topT + floorlog2(n)`. Tail: `Widen(2^(-bits + topT + 1))`, from |tail| ≤ 2|t|^(N+1)/(N+1)! for |t| ≤ ½.

**SinCosOf(Ball v, W):** q = round(v/(π/2)), r = v − q·π/2, h as above, t = r/2^h. `c = cos t − 1` by the same recursion on v = −t², dividing by (2k+1)(2k+2), and halving at the end; tail: alternating, first omitted term. Then h times `c = 2c(c + 2)`. cos r = 1 + c, sin r = sign(r)·sqrt(−c(c + 2)); quadrant from q mod 4.
**LogOf(x, W):** x = 2^e·f with f in [√½, √2), so e·ln 2 + ln f never cancels. t = f − 1 is exact. r roots (r = max(0, r0 + top(t) + 1), r0 = 4 below 2048 bits, 8 above), z = (f−1)/(f+1), u = z², 2^(r+1)·z·Sum u^k/(2k+1). Blocks: `sum = sum·u^m + Sum_{i<m} p[i]/(2(jm+i)+1)`. Tail: 2u^n/(2n+1).
**AtanOf(Ball t, W):** |t| > 1: π/2 − atan(1/t). h halvings `t = t/(1 + sqrt(1 + t²))` (h = 4 below 2048 bits, 8 above), then the same block sum in −t², times t, times 2^h. Tail: alternating.

Above about 8,000 bits, once Karatsuba is in: **Newton on the forward function** (T19): `ln x = y + ln(1 + d)`, `d = x·exp(−y) − 1`, with y good to W/3 bits, three terms and a d⁴ bound; `atan x = y + atan(d)`, `d = (x cos y − sin y)/(cos y + x sin y)`. Rigorous as written: y is just a number, and only the correction needs a bound.

**Built on those:**
Log2 = e + ln f/ln 2 (keep e out of the ball). Log10 = ln x/ln 10. Log(x, b) = ln x/ln b. LogP1 = series at t, when t is small; else Log(1 + t). Log2P1, Log10P1.
Exp2 = reduce x = k + r exactly, then `ExpOf(r·ln 2)`. Exp10 = `ExpOf(x·ln 10)`. ExpM1 = `2^k·e + (2^k − 1)`, with e kept from ExpOf's reduced form.
Pow = `ExpOf(y·ln |x|)`, W + bits(y·ln x) + 8; integer y by exact product, or ball powering past 4(p + 64) bits. x = 2^e: Exp2(e·y).
Tan = sin/cos. Sinh = e(e + 2)/(2(e + 1)), Cosh = 1 + e²/(2(e + 1)), Tanh = (e² + 2e)/(e² + 2e + 2), with e = expm1 x: none cancels.
Asin = Atan2(x, sqrt((1−x)(1+x))), Acos = Atan2(sqrt((1−x)(1+x)), x): the product is exact and acos near 1 keeps relative precision. Atan2 = quadrant table + AtanOf(y/x).
Asinh = LogP1(x + x²/(1 + sqrt(x² + 1))), Acosh = LogP1(t + sqrt(t(t + 2))), t = x − 1, Atanh = ½ LogP1(2x/(1 − x)).
Hypot: exact x² + y², with `Add`'s gap shortcut at 2(p + 2) bits and sticky, then the Sqrt core. Cbrt: digit by digit, 3 bits at a time, keeping r and r²: (2r + 1)³ = 8r³ + 12r² + 6r + 1, trial 12r² + 6r + 1, no multiply. RootN: Newton on `ApInt` (`((n−1)x + N/x^(n−1))/n`, from above) for n ≥ 4.

Dependency order: Ball → loop → splitting → pi, e, ln 2, ln 10 → ExpOf → LogOf → exp and log families → Roots → Pow; SinCosOf → units → AtanOf → Asin, Acos; hyperbolics after both. Integer roots need nothing; float roots want ExpOf and LogOf only for huge n.

Tuning, integer only (no `Math.Cbrt`; the library has no floating log and should stay that way): s = smallest with 4s³ ≥ 3W (about 0.9·∛W; ±half made ±15% difference). m = integer sqrt of N. First W: 32 + core's extra, about 2·bitlen(W) + 8.

## 7. Huge arguments

sin(x) needs pi to about W + top(x) bits: cost follows x's exponent. sin(1e1000000) needs pi to 3.32M bits. Extrapolated, not run: about 9 hours today, 2.5 hours with the prefix adder (quadratic from 3.6 s at 65536 bits), 45 minutes with Karatsuba (from the 286 s run at 1M bits), nearly all of it in digit-by-digit sqrt and restoring division. Then each call needs one multiply as long as x's exponent: about 20 s with Karatsuba, until T20.

Policy:
- **The library is always right or refuses, and only refuses when it can't be done.** No cap: Matthew dropped `Parse`'s cap for the same reason, and long work belongs in a job. Past `MaxPrecision` (top(x) + W > 2^30) it throws `OverflowException`, naming the bits needed. Never NaN, never a guess.
- **`ApFloat.TrigReductionBits(x)`** gives the bits of pi the call needs, in no time, so the caller can ask first.
- **The calculator** has two messages. "Needs pi to 3,321,933 bits, about N minutes: continue?", above a limit it sets. And the one people need more: if ulp(x) at the working precision is at least 2π, the stored x isn't the x that was typed, so the answer is exact for a number nobody entered: 1e1000000 at 256 bits is not 10^1000000.
- **Degrees and gradians don't have this problem at all**: `Ieee754Remainder(x, 360)` is exact and instant at any exponent, through `GridModulo`'s ModPow. sin(1e1000000 degrees) takes microseconds.
- **T20, later:** a Payne-Hanek window. Chudnovsky gives 1/pi as cheaply as pi, so cache 2/π, take the window of bits at x's exponent, and multiply by m: one short multiply per call after the first. Not needed at launch.

## 8. Constant cache

One immutable entry per constant, (Ball, bits), in a field swapped by compare-and-exchange. Readers take no lock.
- A request at W returns the longest entry if it holds W bits; a miss computes at `W + W/4 + 64`, so Ziv's small steps don't each recompute.
- Publish only if longer. Two threads may both compute: harmless, and locks across a job's steps would be wrong anyway (a job may move threads, and may be abandoned mid-way).
- It stores an enclosure, so one entry serves every precision and mode. **Precision changes never invalidate anything:** a constant is a fact. The calculator's "set precision and clear" needs nothing here.
- Memory: 125 KB per Mbit. Entries over 2^24 bits (2 MB) are used and not kept, so one sin(huge) can't pin gigabytes. `ApFloat.ClearConstants()` for the rest.
- A cache must never change a result: tests clear it and compare, and hammer it from 8 threads at mixed precisions.
- `sqrt(10005)` and intermediate splitting state aren't cached: extending from N to 2N terms saves at most a quarter on schoolbook, for twice the memory.

## 9. Jobs

`Natural.Jobs` doesn't change. Every core is an iterator that yields the units it just did and calls plain methods per step (hot loops stay out of iterators). Steps: one merge in splitting, one giant step in a series, one doubling, one root or halving, one round. The one-shot calls drain the same steps.

`ApFloat.Start(UnaryFunction.Sin, x, precision, mode)` and `Start(BinaryFunction.Pow, x, y, ...)` and `StartConstant(Constant.Pi, precision, mode)`, also with a format, return `Job<ApFloat, RoundingCertificate>`. One entry, not 40 `StartSin`s: a calculator maps keys to an enum anyway.
`RoundingCertificate(Rounds, WorkingBits, Exact, Low, High)`: Low ≤ exact value ≤ High, which is also what a calculator's "how sure" display wants.

Units are limb steps, estimated from sizes as now: a non-scalar product w²/64, a scalar step w/4, a splitting merge from the operands' sizes. `ExpectedWork`: the sum for the first round, plus the constants if they aren't cached. Progress stays monotone and capped at 0.99 by `Job`.

Grain limit: the biggest single multiply is one step (75 s at 1M bits on schoolbook, 3 s with Karatsuba). Parked 5c, or item 7, closes that. A calculator should run jobs on a worker thread and stop calling Continue.

## 10. API shape and item 9

Exactly Sqrt's pattern, for each F, with .NET's names:
```csharp
public static ApFloat Sin(ApFloat x) => Sin(x, x.Precision);
public static ApFloat Sin(ApFloat x, int precision, RoundingMode mode = RoundingMode.ToNearestEven)
public static ApFloat Sin(ApFloat x, IeeeFormat format, RoundingMode mode = RoundingMode.ToNearestEven)
internal static ApFloat Sin(ApFloat x, Target target, RoundingMode mode, ref IeeeFlags flags)
// and IeeeContext.Sin(x)
```
Two operands take the larger precision, as the operators do. `RootN(x, int n)`, `Atan2(y, x)`, `Log(x, newBase)`, `Pow(x, y)`, `Hypot(x, y)`.
Constants: `ApFloat.Pi`, `E`, `Tau` (required by the interface, so properties, at `DefaultPrecision`); `GetPi(precision, mode)` and `GetPi(format, mode)`, the same for E, Tau, Ln2, Ln10; `ieee.Pi` and friends raising Inexact.
Units: `AngleUnit { Radians, Degrees, Gradians, HalfTurns }`, `Sin(x, AngleUnit unit, precision, mode)`; `SinPi` is `HalfTurns`.

Item 9 stops being one big last step. `IFloatingPointConstants` needs only E, Pi, Tau, and each family then extends only that (checked by reflection): so declare each interface at the end of its own session: `IRootFunctions` T9, `IPowerFunctions` T10, `IExponentialFunctions` T7, `ILogarithmicFunctions` T8, `ITrigonometricFunctions` T14, `IHyperbolicFunctions` T16. What's left for T18: `IFloatingPoint`'s byte members and `IFloatingPointIeee754`'s Atan2Pi, BitDecrement, BitIncrement, Epsilon, ILogB, Lerp, ScaleB, the estimates.

**The compiler's list is not enough here.** The TODO's recipe (read CS0535) misses every member with a default implementation, and those defaults are wrong for ApFloat: `ExpM1` is `Exp(x) − 1` (two roundings, at the wrong precision), and likewise Exp2M1, Exp10M1, LogP1, Log2P1, Log10P1, DegreesToRadians, RadiansToDegrees, Lerp, Round(x, digits), ConvertToInteger. Use the reflection list in `dotnetapi.cs`.

Where `double` is wrong, Natural follows IEEE 754 (Matthew's call, since 9b copied `double` member by member): `double.ExpM1(-0)`, `LogP1(-0)` and `Exp2M1(-0)` give +0, `TanPi(0.25)` gives 0.9999999999999999.

The trap in `T.Pi`: 256 bits always, so generic code at 4096 bits silently gets a 256-bit pi. Nothing in the interface can know the precision. Document it; `GetPi(precision)` is the way round.

## 11. Oracles

Layers, each independent of the last:
1. **`TranscendentalOracle.cs`** (tests, new): BigInteger fixed-point series for exp, ln, sincos, atan, with no line shared with the library. It gives a rational stand-in, as `FloatOracle.SqrtOf` does: the midpoint of an enclosure at 2p + 100 bits, checked to round alike at both ends. Then `FloatOracle.Round`, `Encode` and `RoundingFlags` do the rest: every mode, every format, flags, random biased inputs, p from 1 to 300.
2. **Enclosure containment**, stronger than comparing rounded results: for each core, random x and W from 8 to 2000: the oracle's value at W + 200 must lie inside the ball, and `Bits ≥ W − slack`. A short series is caught at once by this and hardly ever by rounding.
3. **Pins**, offline, checked in as C# data, so nothing needs Python at run time: `tests/reference/transcendental_reference.py`, standard library only. decimal's exp, ln, log10 (±1 unit around a correctly rounded result); sin, cos, atan from integer series with a counted error (`oracle.py` is the working prototype: 2,516 values in 0.4 s). Both ends rounded by exact `Fraction` long division; if they disagree the bits double: Ziv's loop written a second time, independently. High-precision pi from Chudnovsky and Machin on Python ints, which must agree (0 units apart at 65,536 bits, 0.02 s and 0.2 s). decimal is good for about 10k digits a value (exp 2.6 s, ln 6.9 s) and a few at 30k (15 s, 42 s, log10 63 s); at 100k digits one value took minutes, so keep those to a handful. Long values as SHA-256 of the hex plus the first and last 64 digits.
4. **Hardware.** `Math.Sin` isn't correctly rounded, so at binary64 it can only say: within 1 ulp (measured: never 2 in 20,000; differing in the last place 0.3% exp, 0.15% ln, 2.6% sin, 3.1% cos, 1.9% atan). But **at binary32 and Half it certifies correct rounding, in every mode**: take d = Math.F(x), round d ± 2 ulp to the small format in the mode; if both ends agree, Natural must equal it. 0 wrong, and almost nothing skipped: a double rounds differently only within 2^-27 of a float boundary. Every Half, 20,000 random floats. Measure each new function's ulp spread first. .NET's `SinPi` family is not an oracle (`TanPi(0.25)`).
5. **Identities.** Directed rounding composes: f at 2p toward zero, rounded to p toward zero, must equal f at p toward zero. No oracle needed, and strong. sin(−x) = −sin(x) in the mirrored mode. lo ≤ nearest ≤ hi with hi − lo one ulp. Ball-level: |s² + c² − 1| inside its radius, exp(a + b) overlapping exp(a)exp(b). Each constant two ways: pi against 4·atan(1), ln 2 against Log(2), e against Exp(1).
6. **Exhaustive tiny formats**: every value of an 8-bit format, every mode, flags predicted. Cheap, and it finds sign and subnormal bugs.
7. **Hard cases:** ln(0x1AC50B409C8AEE·2^-44), published, verified here: 53 bits then 60 ones, so directed modes take 2 rounds with no hook; assert `LastRounds`. sin(1e22) = −0.8522008497671888017727. cos(6381956970095103·2^797) = −4.6871659242546276e-19 (the double nearest a multiple of π/2). Both ran correctly.

## 12. Sessions

The brief format is the TODO's. Every session also: start from `git status`, keep the suite green, add a WORKLOG entry, tick the TODO, update CLAUDE.md's layout and README's status and count, and run the mutation battery by CLAUDE.md's recipe, filtered by **class** names. Cap every `while` in tests. Default-suite tests stay under 2,000 bits, with pins to 64k bits as single cases.

Common test set ("the battery"): specials table against `double` where it's right and IEEE where not; exact cases in every mode with no Inexact; oracle layers 1 and 2; hardware layer 4 where the function exists in `Math`; identities; a tiny format exhaustively; flags predicted. In a format, always; on the context, once.

### Order item 7, first piece: the adder (before T1; the bench agent's brief)
Replace `HalfAddAll`'s loop with the prefix form. Answers the TODO's "ask Matthew first". multiply 2.4 times faster at 64 bits and 3 to 4 from 256 bits up, whole functions 2.3 to 3 times. Worth it for the batteries alone.

### T1. Ball
**Goal:** `internal readonly struct Ball`: Exact, Of(x, w), Neg, Scale(k), Add, Sub, Mul, MulSmall, DivSmall, Div, Sqrt, Trim(w), Widen, Bits, ContainsZero. **Files:** `src/Natural/Ball.cs`, `tests/Natural.Tests/BallTests.cs`. **Depends:** nothing.
**Baseline:** radius costs at most 15% at 53 bits in the prototype.
**Rules:** mid rounded to nearest at w; |err| ≤ 2^(top(m) − w). Radius is an ApFloat of 32 bits, every operation TowardPositive; magnitudes rounded up, denominators down. Div: `(ra + |m|·rb)/(|b| − rb)`. Sqrt: `R/√a`, with √a bounded below. Radius may be +∞, meaning unknown: a divisor ball containing 0, a root of a ball reaching 0.
**Pitfalls:** ∞ × 0 in a radius is NaN: guard it, and treat NaN as unknown. Use the rounded mid's top. A mid that cancels to exact 0 still keeps its radius. Multiply the radius by a 32-bit truncation of the mid, never the whole mid (16 passes over W limbs). No `Math.*`.
**Tests:** containment against exact rationals on biased operands, W from 1 to 300, weighted to tiny W, where rounding error dominates; random expression trees; a divisor containing 0.
**Mutations:** radius rounded to nearest; half-ulp dropped in each op; cross term dropped in Mul; denominator rounded up; Sub's sign; Scale forgetting the radius; Sqrt's bound using the mid unrounded; ∞ guard removed.

### T2. Ziv's loop
**Goal:** `ZivSteps`, `Settling` (as `Pinning`), `Settle`, `Nudge`, `RoundingCertificate`, the hooks. **Files:** `ApFloat.Ziv.cs`, `ZivTests.cs`. **Depends:** T1.
**Baseline:** none. **Plugs in:** `Target.Round`, `Target.RangeOf`, the `SettledFlags` logic.
**Test function:** no toy in src. Tests pass a delegate for x√2 (Ball), whose exact value is √(2x²), so `FloatOracle.SqrtOf` is a perfect oracle in every mode and format, flags included.
**Pitfalls:** section 3's list: ball containing 0, negative mids, forced Inexact, ends agreeing on value but not `RangeOf`, candidate check in every mode. Hooks read on the caller's thread.
**Tests:** that oracle; `ForcedGuard` small so later rounds run; an enclosure with an exact case and a check (flags right in all modes, Inexact absent in nearest too); the same with no check throws after `MaxZivRounds = 6` and doesn't hang; `lost` reaches the target in 2 rounds on a value that cancels; Nudge against rational stand-ins, including subnormals in a format.
**Mutations:** accept on one end; skip `RangeOf`; g not growing; `lost` ignored (time only: assert rounds); candidate check in directed modes only; flags from `target.Round` alone; nudge's direction; sticky lost.

### T3. Binary splitting and pi
**Goal:** the P, Q, B, T engine, bottom-up, as steps; Chudnovsky; the cache; `Pi`, `Tau`, `GetPi`, `GetTau`; `ieee.Pi`. **Files:** `ApFloat.Constants.cs`, `ConstantTests.cs`; start `tests/reference/transcendental_reference.py`. **Depends:** T2.
**Baseline:** pi cold 0.7 / 2.0 / 28 / 431 ms at 256 / 1024 / 4096 / 16384 bits, ripple; 0.3 / 0.7 / 9 / 147 prefix.
**Pitfalls:** term count from 47 bits, rounded up; the sign on T's leaf; leaf products through ApInt, not ulong; a cache hit must hold W bits **after** its radius, not before; truncate T and Q to W + 16 before dividing, or division costs the 2W-bit size.
**Tests:** binary128 and binary256 pi already pinned in `IeeeBytesTests`; every mode of the first 1,000 bits against pins; 64k bits by hash; Ziv on GetPi in all modes; cache: clear and compare, 8 threads, publish-only-if-longer; a shorter request after a longer.
**Mutations:** a term short; merge's cross term swapped; sign alternation; radius of cached value not carried; `>=` becoming `>` on the hit test; publish shorter over longer.

### T4. e, ln 2, ln 10, and the constants interface
**Goal:** the same engine for e and the two atanh sums; `E`, `GetE`, `GetLn2`, `GetLn10`; `IFloatingPointConstants<ApFloat>`. **Files:** `ApFloat.Constants.cs`, `ConstantTests.cs`, the Python script. **Depends:** T3.
**Baseline:** e 141 ms, ln 2 1,110 ms at 16384 bits, ripple.
**Pitfalls:** ln 2 costs 2.5 times pi, so it's the slow first call of Exp; bits per term differ per series; signs in the three-term sum. Tail for atanh: 2u^n/(2n+1).
**Tests:** pins; ln 10's identity against ln 2 + ln 5 from decimal; mode bracketing. **Mutations:** a coefficient (18, 8, 46, 34, 20); term count of the shortest series; e's T missing the leading 1.

### T5. Exp
**Goal:** `ExpOf`, `Exp` with all overloads and `ieee.Exp`. Start `TranscendentalOracle.cs` with exp. **Files:** `ApFloat.Exp.cs`, `ExpTests.cs`, `TranscendentalOracle.cs`. **Depends:** T4.
**Baseline:** 0.16 / 0.92 / 9.2 / 185 ms at 53 / 256 / 1024 / 4096 bits, ripple; 0.07 / 0.31 / 3.3 / 66 prefix. Beat them.
**Pitfalls:** `2e + e²`, not squaring. h from top(r), so tiny r skips halvings. k's rounding at low precision; ln 2 at W + bits(k). Format early-outs before any W-bit work; `checked` exponents at a precision. The shortcut's threshold. r's mid can cancel to 0 even though r isn't 0: the ball then holds only radius, and that round fails.
**Tests:** the battery; `Exp(1)` against `GetE`; exp(2^-1000000) instant; overflow and underflow straddling binary64's limits in every mode; subnormal results; exp near 709.78. Containment at W from 8 to 2000.
**Mutations:** series a term short; tail dropped (containment catches it); `2e + e²` as `e² + e`; k off by one (radius explodes: rounds); ln 2 at W bits only; nudge direction; threshold ±1; early-out margin; sign of Underflow path.

### T6. Log
**Goal:** `LogOf`, `LogP1Of`, `Log`, `LogP1`, `ieee.Log`. **Files:** `ApFloat.Log.cs`, `LogTests.cs`, oracle. **Depends:** T5.
**Baseline:** 0.13 / 0.81 / 9.8 / 200 ms, ripple, at r = 4. **Pitfalls:** f in [√½, √2): compare exactly, with no double. Roots lose accuracy on tiny t, so r falls with top(t). e·ln 2 at W + bits(e). Near 1, t is exact: `x − 1` at full length, not rounded to W.
**Tests:** the battery; the published worst case with `LastRounds == 2`; exp(ln x) within enclosures; x = 1 ± 2^-k for k around p, p/2, 2p; x near 2^k on both sides. **Mutations:** e off by one at the √2 edge; r not falling; z's sign; a block boundary in the sum; ln(1) check removed (fails fast through `MaxZivRounds`); LogP1 direction.

### T7. Exp family
**Goal:** ExpM1, Exp2, Exp2M1, Exp10, Exp10M1, DegreesToRadians later; `IExponentialFunctions<ApFloat>`. **Files:** `ApFloat.Exp.cs`, `ExpFamilyTests.cs`. **Depends:** T6.
**Pitfalls:** defaults hide missing members. Exp10 at an integer goes through `FromDecimalSteps`; x = 1e30 mustn't reach W-bit work. ExpM1(−0) is −0 (IEEE, not .NET). Exp2M1 at huge integers uses Add's gap shortcut.
**Tests:** battery; `Exp2(n)` exact; `Exp10(−2)` against `Parse("0.01")`, every mode; hardware via `double.Exp2`, after measuring. **Mutations:** each exact branch removed; ExpM1's k = 0 branch; sign of zero.

### T8. Log family, and the lazy check
**Goal:** Log2, Log10, Log(x, b), Log2P1, Log10P1, the candidate check; `ILogarithmicFunctions<ApFloat>`. **Files:** `ApFloat.Log.cs`, `LogFamilyTests.cs`. **Depends:** T7.
**Pitfalls:** log_4(8) = 3/2 is exact and non-integer. Inexact in nearest. `x^(2^a)` needs the bit-length guard first; a ≤ log2(bitlen(m_b)). b = 2^j directly. .NET's Log(5, 1), Log(1, 0) = −0, Log(5, 0).
**Tests:** `log_2(8)`, `log_4(8)`, `log_8(4)`, `log_10(1000)`, every mode, flags; Log10(10^k) for k to 400; x just off 10^k. **Mutations:** Log2 exact branch; Log10's k from the exponent; candidate check in nearest; guard on bit lengths; Log10 treating 10^−k as exact.

### T9. Roots
**Goal:** `Magnitude.CubeRoot`, `ApInt.Cbrt`, `ApInt.Root`, `ApFloat.Cbrt`, `RootN`, `Hypot`; `IRootFunctions<ApFloat>`. **Files:** `Magnitude.cs`, `ApInt.cs`, `ApFloat.Operations.cs`, `RootTests.cs`. **Depends:** T4 (the interface needs E, Pi, Tau); T6 for huge n, through ExpOf and LogOf. The integer roots need nothing and could go first.
**Baseline:** Sqrt's cost. **Pitfalls:** exponent multiple of n; the long-x shift keeps floor correct, as Sqrt's does; RootN(x, 0) NaN; negative n; .NET's table (RootN(−0, −3) = −∞, DivideByZero); Hypot's gap; Hypot(∞, NaN) = ∞.
**Tests:** r³ ≤ n < (r + 1)³ against BigInteger; `double.Cbrt`, `double.Hypot` by hardware interval; perfect cubes, in every mode, exact with no Inexact; rational stand-in. **Mutations:** digit step's carry; exponent's residue; sticky dropped; odd-root sign; Hypot's gap threshold.

### T10. Pow
**Goal:** `Pow`, `IPowerFunctions<ApFloat>`. **Files:** `ApFloat.Pow.cs`, `PowTests.cs`. **Depends:** T8, T9.
**The table:** pow(x, ±0) = 1 even for NaN; pow(1, y) = 1 even for NaN; pow(−1, ±∞) = 1; pow(±0, y < 0 odd) = ±∞, even or non-integer +∞, both DivideByZero; pow(±0, y > 0 odd) = ±0, else +0; pow(x, −∞) and pow(x, +∞) by |x| against 1; pow(−∞, y) by y's sign and oddness; pow(x < 0, non-integer) = NaN, Invalid.
**Routes:** in order: table; y integer: exact if `|y|·bitlen(m) ≤ 4(p + 64)`, else ball powering; x = 2^e; y = ½ to Sqrt; `ExpOf(y·ln|x|)`, lazy check, sign by y's parity.
**Pitfalls:** a negative result: the sign goes to `target.Round`, not by hand on the mode. Overflow from a 64-bit ball of y·ln x. x near 1, y huge. Result near 1.
**Tests:** every pair of specials against `double.Pow`; `4^0.5`, `16^0.75`, `2^10`, `(−2)^3`, `3^−1` as exact rational; hardware interval; lazy exact flags. **Mutations:** each table row; parity; exact-size guard; lazy check off; y·ln x at too few bits.

### T11. Sin, Cos, Tan
**Goal:** `SinCosOf`, `Sin`, `Cos`, `Tan`, `SinCos`, `TrigReductionBits`. **Files:** `ApFloat.Trig.cs`, `TrigTests.cs`, oracle. **Depends:** T4.
**Baseline:** sin 0.13 / 0.69 / 7.8 / 156 ms, ripple. **Pitfalls:** quadrant of a negative q; q as ApInt, since it can be huge; pi at W + top(x) + 8; tiny reduced r; cancellation shows as `lost`; x past `MaxPrecision` throws with the bits named. `SinCos` settles both from one ball, retrying until both settle.
**Tests:** battery; sin(1e22); the 6381956970095103·2^797 vector; sin of pi rounded to p bits, which is π − x: 2 rounds; x near kπ/2. Containment; s² + c² = 1. **Mutations:** quadrant table; q mod 4 of a negative; `2c(c + 2)`; sign of sin; π at W only; Tan's division; ±0.

### T12. Angle units
**Goal:** `AngleUnit`, SinPi, CosPi, TanPi, SinCosPi, `Sin(x, unit, ...)` and so on. **Files:** `ApFloat.Trig.cs`, `AngleUnitTests.cs`. **Depends:** T11.
**Plugs in:** exact `Ieee754Remainder(x, 2U)`, then exact reduction to [−U/4, U/4] with a quadrant, so nothing cancels; then `SinCosOf(π·r/U)`.
**Pitfalls:** the table in section 4; signs of zero (`double.SinPi`, `TanPi` measured); gradians have no ±½. No Ziv on an exact case. Huge x is instant.
**Tests:** every multiple of 15° to ±720°, every mode, Inexact as predicted; sin(1e1000000) in degrees; exact rational reduction; oracle at non-special angles. **Mutations:** each table entry; residue sign; quadrant; a pole's flag.

### T13. Atan, Atan2
**Goal:** `AtanOf`, `Atan`, `Atan2`, `AtanPi`, `Atan2Pi`. **Files:** `ApFloat.Atan.cs`, `AtanTests.cs`, oracle. **Depends:** T11.
**Table:** Atan2(±0, +x) = ±0; (±0, −x) = ±π; (±0, −0) = ±π; (±0, +0) = ±0; (y, ±0) = ±π/2; (±∞, +∞) = ±π/4; (±∞, −∞) = ±3π/4; (y, +∞) = ±0; (y, −∞) = ±π.
**Pitfalls:** the ratio isn't dyadic, so tiny-ratio shortcut only for an exact ratio. Exponent gap: underflow early-out in a format. |t| > 1 inversion. Quadrant sign.
**Tests:** every pair of specials against `double.Atan2`; atan(1) against pi; hardware interval; tan(atan x) inside enclosures. **Mutations:** each table row; invert's sign; halving formula; tail.

### T14. Asin, Acos
**Goal:** Asin, Acos, AsinPi, AcosPi, inverse units; `ITrigonometricFunctions<ApFloat>`, DegreesToRadians, RadiansToDegrees. **Files:** `ApFloat.Atan.cs`, `InverseTrigTests.cs`. **Depends:** T12, T13.
**Pitfalls:** (1 − x)(1 + x) exact, never x² subtracted; ±1 and ±½ tables; the default interface members. **Tests:** hardware interval; acos(1 − 2^-k); sin(asin x). **Mutations:** swapped arguments; exact product replaced by rounded; table rows.

### T15. Sinh, Cosh, Tanh
**Goal:** the three via `Expm1Of`. **Files:** `ApFloat.Hyperbolic.cs`, `HyperbolicTests.cs`. **Depends:** T7.
**Pitfalls:** huge x (use E alone: Ball's Add has the gap shortcut); Tanh's far nudge; overflow by k. **Tests:** battery; cosh² − sinh² = 1. **Mutations:** formulas; thresholds; sign.

### T16. Asinh, Acosh, Atanh
**Goal:** the three via LogP1; `IHyperbolicFunctions<ApFloat>`. **Files:** `ApFloat.Hyperbolic.cs`, `HyperbolicTests.cs`. **Depends:** T15, T8.
**Pitfalls:** oddness and the mode (sign to `target.Round`); Acosh near 1; Atanh(±1). **Mutations:** domain edges; DivideByZero; forms that cancel.

### T17. Starting a function as a job
**Goal:** `UnaryFunction`, `BinaryFunction`, `Constant`, `Start`, `StartConstant`; certificate; progress worth reading. **Files:** `ApFloat.Jobs.cs`, `TranscendentalJobTests.cs`. **Depends:** whatever exists; repeat for later functions.
**Pitfalls:** hooks read at start; result bit-identical however stepped; `Continue` overshoots by a step; a job continued on another thread; the cache across jobs. **Tests:** as `ParseJobTests`: any budget gives the one-shot result; capped loops; progress monotone. **Mutations:** a step run twice; a merge lost across a yield; certificate of the wrong round.

### T18. Item 9: IFloatingPoint and IFloatingPointIeee754
**Goal:** the byte members, Atan2Pi, BitIncrement, BitDecrement, ILogB, ScaleB, Lerp, Epsilon, the estimates, Round(x, digits), and every default the reflection list shows. **Files:** `ApFloat.GenericMath.cs`, tests. **Depends:** T16.
**Ask Matthew:** what `Epsilon` is with an unbounded exponent (no smallest value: 2^long.MinValue?); ILogB past an int (saturate, as double's ILogB of 0 and NaN do); Round(x, digits) on a binary value. **Tests:** generic code run on both ApFloat and double. **Mutations:** per member.

### Later, none needed at launch
- **T19 Tuning:** adaptive precision in the series (−23 to −33%); Newton on exp for ln, on sincos for atan, above about 8,000 bits, after Karatsuba. Old against new, every test unchanged.
- **T20 Payne-Hanek:** 2/π window, section 7.
- **T21 Bit-burst** for exp and sincos, past 64k bits, after Newton division: O(M(n) log² n) against the present n^(1/3) M(n); uses T3's engine. MPFR switches near 10,000 bits on GMP; measure first.
- **T22 Streaming digits, million-digit constants:** the TODO's words. Double dabble knows no digit until the end, so streaming needs radix conversion by divide and conquer, after Newton division.

## 13. What a calculator needs

At launch: T1 to T14, for every key on a scientific calculator, degrees included. T15 and T16 are small, and hyperbolics are expected on "fully featured". T17 only if it offers more than about 16k bits, or wants a stop button at any length.
Can wait: T18 (the calculator uses the static API, not generic math), T19 to T22.

Shipping in stages works: after T6 it has exp and ln; after T11, trig in radians; after T12, degrees.

Precision limits for the launch, per function, prefix adder: 256 bits 0.3 ms; 1024 bits 3 ms; 4096 bits 60 ms; 16384 bits 1.2 s; 65536 bits 30 s. With Karatsuba: 16384 bits 0.4 s; 65536 bits 5.5 s. First use of a constant adds 0.15 s at 16384 bits.

## 14. Against order item 7

- **7a, the adder: before T1.** Ten lines, multiply 3 to 4 times from 256 bits up, whole functions 2.3 to 3 times, batteries included.
- **7b, Karatsuba and squaring: any time; it blocks nothing and nothing blocks it.** No session here touches `Magnitude.Multiply`. Little gain at 1024 bits (82 against 65 µs, and the 65 used a repeated operand, so it flatters), about twice at 4096, 3.3 at 16384, 5.7 at 65536. Do it before offering more than 16k bits.
- **7c, Newton division and root: before T21 and million-digit constants.** At 1M bits sqrt and division dominate pi, and they don't gain from Karatsuba.
- T19's Newton routes want 7b. T21 wants 7b and 7c.

## 15. Questions for Matthew

1. Correct rounding everywhere (recommended), rather than faithful for the dear functions?
2. Huge trig arguments: always right, however long, refusing only past `MaxPrecision`, with the calculator warning (recommended)?
3. Natural follows IEEE where `double` doesn't: ExpM1(−0) is −0, LogP1(−0) is −0, TanPi(¼) is 1 (recommended)? 9b copied `double` member by member.
4. Is the prefix adder within the rule? The TODO already asks.
5. exp(1e30) at a precision throws `OverflowException`, as `Parse` and `checked` do (recommended), rather than ∞?
6. `ApFloat.Pi` is 256 bits whatever generic code is doing, documented. Any objection?
7. `AngleUnit` on the trig functions (recommended), or `SinDegrees` and so on?
