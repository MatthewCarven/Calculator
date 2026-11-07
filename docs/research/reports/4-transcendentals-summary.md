**Natural order item 8: design and sessions**

Every function can be correctly rounded in all four modes, and the cost is small. I built enough of the design as a prototype to test that. Full briefs, formulas and the exact-case table: `C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\trans\TRANSCENDENTALS-PLAN.md`. The prototype `C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\trans\Proto.cs`, the oracle `C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\trans\oracle.py`, the brute force `C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\trans\thresholds.py` and the interface lister `C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\trans\dotnetapi.cs` die with the scratchpad unless copied.

**Verified by running**
- Prototype (exp, ln, sin, cos, atan, the constants): 2,516 results, 8 to 1024 bits, all four modes, against a Python oracle using `decimal` and integer series: 0 differ. Corrupted results are caught.
- binary32: 2,682 to 4,000 values per function, all modes: 0 wrong. Every Half in nearest, a seventh in each directed mode: 0 wrong.
- Shortcut rules: 13 rules, 448,568 exact-fraction checks: 0 wrong. Six negative controls all fired.
- An undetected ln(1) hung a probe. `log_2(8)`, `log_4(8)`, `4^0.5` and `16^0.75` never settle in a directed mode.

**Not verified:** asin, acos, hyperbolics and general pow (simple compositions only); the nudge in C#; the cache under threads; job plumbing; Payne-Hanek; bit-burst. The 3.3M-bit times are extrapolated. I did not run Natural's suite on my adder patch, and my Karatsuba is a quick probe. Arb papers: abstracts and slides only.

**1. Contract.** Correctly rounded everywhere, in precisions and formats, with flags. A result is what `target.Round` gives for both ends of a certified enclosure, so "rounded exactly once" holds. A 32-bit guard fails about 2^-32 of the time (measured about 2^-g at g = 4, 8, 12).

Error bounds: every function computes a `Ball` (mid, radius). Rounding, input and constant errors propagate mechanically, each radius operation rounded up at 32 bits. Only each series' tail is proved by hand: one inequality, added with `Widen`. Radius costs 5 to 15% at 53 bits. A wrong guess of working bits costs time, never correctness.

Loop: W = p + g, then g = 2g + lost, where lost is how far the ball fell short of W. In a format the ends must also agree on `RangeOf`. A ball containing 0, or an infinite radius, is simply unsettled. Doubling g, not W, avoids a fourfold retry. sin of 256-bit pi has a 63-bit first ball and took 4 rounds without `lost`.

**2. Termination.** There is no exact route to fall back on, so detection is the only obligation.
- Lindemann-Weierstrass, no other exact cases: exp(0); ln(1); sin, tan, sinh, tanh, asin, atan, asinh, atanh at 0; cos, cosh at 0; acos(1); acosh(1).
- Unique factorisation: log2 at 2^k (significand 1); log10 at 10^k, k ≥ 0 only (k is read off the exponent); 2^x and 10^x at integer x.
- Niven: SinPi, CosPi, TanPi, and degrees: sin(30°) = ½ is exact from a non-dyadic angle, and tan(45°) = 1. The inverses likewise. Detected by exact `Ieee754Remainder`. .NET's `TanPi(0.25)` is 0.9999999999999999.
- Settled, not exact: asin(±1), acos(0), atan(±1), from the π ball.
- Cbrt, Hypot, RootN for small n: integer root with remainder as sticky, no loop. Huge n: lazily, as below.
- `x^y` and `log_b x`: lazily. When a dyadic of at most p + 1 bits lies in the enclosure, test it exactly. This must run in every mode: in nearest an exact result settles at once and Inexact would be falsely raised.

Near-representable values take a nudge, not the loop: `target.Round` of the value with a sticky bit on the known side. exp when top(x) ≤ −p − 2; cos and cosh when 2·top(x) + p + 2 ≤ 0; odd functions when 3·top(x) + 3 ≤ min(lowest bit of x, top(x) − p − 2). `sin(2^-4000)` otherwise exhausted my six-round cap. The lowest bit matters: dropping it gave 236 wrong roundings on long inputs.

**3. Algorithms.** Division is only about 1.25 multiplies and sqrt 1.8, so routes land close together. Simplicity decides.
- π: Chudnovsky by binary splitting. 431 ms at 16384 bits, against 2,206 Machin split, 2,645 fixed-point, 3,090 AGM.
- e: splitting. ln 2 = 18 atanh(1/26) − 2 atanh(1/4801) + 8 atanh(1/8749). ln 10 = 46 atanh(1/31) + 34 atanh(1/49) + 20 atanh(1/161). One P, Q, B, T engine, bottom-up.
- exp: reduce by ln 2, halve, expm1 by rectangular splitting, then `e = 2e + e²`. Plain squaring would lose s bits.
- sin, cos: reduce by π/2, cos−1 series, `c = 2c(c + 2)`, sin by sqrt.
- ln: e·ln 2 + atanh series after r roots (4, or 8 above 2048 bits).
- atan: h halvings (4, then 8), alternating series.
- Everything else composes: Log2, Log10, Pow, Tan, Atan2, Asin and Acos via Atan2, hyperbolics via expm1 and LogP1 forms that don't cancel.
- Roots: Cbrt digit by digit, as Sqrt; RootN by Newton on `ApInt`; `exp(ln x / n)` for huge n.
- AGM rejected: 15.5 s against Newton's 6.7 s at 65536 bits with Karatsuba. MPFR states its bound only as little-o.

Dependency order: Ball, loop, splitting, constants, exp, ln, their families, roots, pow; sincos, angle units, atan, asin and acos; hyperbolics after both.

Cost per function, first round, constants cached, ms, prefix adder: 53 bits 0.07; 256 bits 0.3; 1024 bits 3; 4096 bits 60; 16384 bits 1,300; 65536 bits 30,000. Today's adder is about 3 times slower; Karatsuba gives 450 and 5,500 at the top two.

**4. Huge arguments.** sin(x) needs π to W + top(x) bits. sin(1e1000000): about 9 hours today, 2.5 with the adder, 45 minutes with Karatsuba. The library is always right, refusing only past `MaxPrecision`, with `OverflowException`, never NaN. `TrigReductionBits(x)` lets a caller ask first. The calculator warns on cost, and also when ulp(x) ≥ 2π: the stored x is then not what was typed. Degrees need no π at any exponent.

**5. Cache.** One immutable entry per constant, swapped by compare-and-exchange, publishing only if longer. No locks: a job may move threads or be abandoned. A miss computes W + W/4 + 64. It stores an enclosure, so precision changes never invalidate it. Entries over 2^24 bits aren't kept; `ClearConstants()` frees the rest.

**6. Jobs.** Cores are iterators from the first session, as `TimesPowerOfTenSteps`, and one-shots drain them. Steps: one merge, giant step, doubling, root or round. `Start(UnaryFunction.Sin, x, precision, mode)` gives `Job<ApFloat, RoundingCertificate>`. Units are limb steps from sizes. `Natural.Jobs` is unchanged. Grain is one multiply.

**7. API.** Sqrt's pattern with .NET's names: unary, precision, format, internal flagged core, context method. `Pi`, `E`, `Tau` at 256 bits, plus `GetPi(precision, mode)`. `AngleUnit` on trig. Each interface extends only `IFloatingPointConstants`, so declare it when its session ends. Trap: CS0535 misses defaults, and `ExpM1` as `Exp(x) − 1` rounds twice; use the reflection list from `dotnetapi.cs` (path above).

**8. Oracle.** Layers:
- BigInteger series in the tests, as rational stand-ins fed to `FloatOracle`.
- Enclosure containment for each core; a short series fails here at once.
- Offline pins from `tests/reference/transcendental_reference.py`, stdlib only. Chudnovsky and Machin agree at 65,536 bits; `decimal` manages about 10k digits a value.
- Hardware: `Math.*` was within 1 ulp in 20,000 samples, so binary64 can only assert that. At binary32 and Half, rounding d ± 2 ulp in the mode certifies correct rounding.
- Identities: p bits toward zero equals 2p rounded down to p; mirrored modes.
- Hard vector: ln(0x1AC50B409C8AEE·2^-44), published, confirmed here, takes 2 rounds in directed modes.

Mutations: below, plus each exact case removed, which must fail fast through `MaxZivRounds`.

**Sessions.** All run the battery: specials, exact cases, layers above, a tiny format exhaustively, flags predicted.

1. **T1 Ball.** Goal: mid-radius enclosure, radii always rounded up. Files: `Ball.cs`, `BallTests.cs`. Depends: none. Tests: exact rationals, W 1 to 300, weighted to tiny W. Mutations: half-ulp dropped; radius rounded to nearest; Mul's cross term. Pitfalls: ∞ × 0 gives a NaN radius; multiply radii by a 32-bit truncation of the mid.
2. **T2 Ziv loop.** Goal: `ZivSteps`, `Settle`, nudge, certificate, hooks `ForcedGuard`, `MaxZivRounds`. Files: `ApFloat.Ziv.cs`, `ZivTests.cs`. Depends: T1. Tests: x√2 as a delegate from the tests, whose exact value `FloatOracle.SqrtOf` gives in every mode and format. Mutations: one end accepted; `RangeOf` skipped; candidate check in directed modes only; nudge direction. Pitfalls: force Inexact, since an end may be representable; negative mids.
3. **T3 Splitting, π, cache.** Goal: bottom-up P, Q, B, T engine, Chudnovsky, `Pi`, `Tau`, `GetPi`. Files: `ApFloat.Constants.cs`, `ConstantTests.cs`, the Python script. Depends: T2. Tests: pins, binary128 π, clear-and-compare, 8 threads. Mutations: a term short; hit test `>=` to `>`; shorter published over longer. Pitfalls: a hit must hold W after its radius; truncate T and Q to W + 16 before dividing.
4. **T4 e, ln 2, ln 10.** Goal: the same engine, `IFloatingPointConstants`. Files: as T3. Depends: T3. Tests: pins, ln 10 against decimal. Mutations: each coefficient; e's leading 1. Pitfalls: bits per term differ per series; ln 2 is the slow first call of Exp.
5. **T5 Exp.** Goal: `ExpOf`, `Exp`, overloads, context. Files: `ApFloat.Exp.cs`, `ExpTests.cs`, `TranscendentalOracle.cs`. Depends: T4. Tests: battery, `Exp(1)` against `GetE`, binary64's limits. Mutations: `2e + e²`; tail; threshold ±1; early-out margin. Pitfalls: format early-outs before W-bit work; ln 2 at W + bits(k).
6. **T6 Log, LogP1.** Goal: `LogOf`, near-1 route. Files: `ApFloat.Log.cs`, `LogTests.cs`, oracle. Depends: T5. Tests: battery, hard vector, 1 ± 2^-k. Mutations: ln(1) removed; e at √2; block boundary. Pitfalls: f in [√½, √2] compared exactly; fewer roots near 1.
7. **T7 Exp family.** Goal: ExpM1, Exp2, Exp10, the M1s, `IExponentialFunctions`. Files: `ApFloat.Exp.cs`, `ExpFamilyTests.cs`. Depends: T6. Tests: `Exp10(−2)` against `Parse`, every mode. Mutations: exact branches; ExpM1's zero sign. Pitfalls: Exp10 at integers goes through `FromDecimalSteps`.
8. **T8 Log family.** Goal: Log2, Log10, Log(x, b), the P1s, lazy check, `ILogarithmicFunctions`. Files: `ApFloat.Log.cs`, `LogFamilyTests.cs`. Depends: T7. Tests: `log_4(8)`, `log_8(4)`, Log10(10^k), flags. Mutations: check absent in nearest; bit-length guard; 10^−k as exact. Pitfalls: guard before powering; .NET's Log(5, 1).
9. **T9 Roots.** Goal: `Magnitude.CubeRoot`, `ApInt.Root`, Cbrt, RootN, Hypot, `IRootFunctions`. Files: `Magnitude.cs`, `ApInt.cs`, `ApFloat.Operations.cs`, `RootTests.cs`. Depends: T4, T6 for huge n. Tests: r³ ≤ n < (r + 1)³ by BigInteger, hardware. Mutations: sticky; exponent residue; digit carry. Pitfalls: .NET's table, RootN(−0, −3) = −∞; Hypot's gap.
10. **T10 Pow.** Goal: IEEE's table, integer y exactly, general route, `IPowerFunctions`. Files: `ApFloat.Pow.cs`, `PowTests.cs`. Depends: T8, T9. Tests: specials against `double.Pow`, `16^0.75`, hardware. Mutations: table rows; parity; lazy check off. Pitfalls: pass the sign to `target.Round`, never adjust the mode.
11. **T11 Sin, Cos, Tan.** Goal: `SinCosOf`, `SinCos`, `TrigReductionBits`. Files: `ApFloat.Trig.cs`, `TrigTests.cs`, oracle. Depends: T4. Tests: battery, sin(1e22), cos(6381956970095103·2^797), sin of rounded π. Mutations: quadrants; `2c(c + 2)`; π at W only. Pitfalls: q may be huge and negative.
12. **T12 Angle units.** Goal: SinPi family, degrees, gradians. Files: `ApFloat.Trig.cs`, `AngleUnitTests.cs`. Depends: T11. Tests: every 15° in every mode, sin(1e1000000°). Mutations: table entries; quadrant; pole flag. Pitfalls: reduce exactly to [−U/4, U/4]; signs of zero.
13. **T13 Atan, Atan2.** Goal: `AtanOf`, `AtanPi`, `Atan2Pi`. Files: `ApFloat.Atan.cs`, `AtanTests.cs`, oracle. Depends: T11. Tests: specials against `double.Atan2`, atan(1) against π. Mutations: table rows; inversion; halving. Pitfalls: y/x isn't dyadic; exponent gap in formats.
14. **T14 Asin, Acos.** Goal: both, inverse units, `ITrigonometricFunctions`. Files: `ApFloat.Atan.cs`, `InverseTrigTests.cs`. Depends: T12, T13. Tests: hardware, acos(1 − 2^-k). Mutations: rounded product; swapped arguments. Pitfalls: (1 − x)(1 + x) exact; defaults hide missing members.
15. **T15 Sinh, Cosh, Tanh.** Goal: expm1 forms. Files: `ApFloat.Hyperbolic.cs`, `HyperbolicTests.cs`. Depends: T7. Tests: battery, cosh² − sinh² = 1. Mutations: forms; far nudge. Pitfalls: huge x; Tanh(±∞) is exact.
16. **T16 Asinh, Acosh, Atanh.** Goal: LogP1 forms, `IHyperbolicFunctions`. Files: as T15. Depends: T15, T8. Tests: battery. Mutations: domain edges; DivideByZero. Pitfalls: forms that cancel near 0 and 1.
17. **T17 Start API.** Goal: `Start`, `StartConstant`, certificate. Files: `ApFloat.Jobs.cs`, `TranscendentalJobTests.cs`. Depends: whatever exists. Tests: as `ParseJobTests`. Mutations: step run twice; merge lost across a yield. Pitfalls: hooks read at start; cap test loops.
18. **T18 Item 9.** Goal: byte members, `IFloatingPointIeee754`. Files: `ApFloat.GenericMath.cs`, tests. Depends: T16. Tests: generic code on ApFloat and double. Mutations: per member. Pitfalls: Epsilon and ILogB need Matthew.
19. **T19 Tuning.** Adaptive series (−23 to −33%), Newton on exp and sincos above about 8,000 bits. After Karatsuba. Old against new; tests unchanged.
20. **T20 Payne-Hanek.** Cached 2/π window, so huge reductions cost a short multiply. Containment against T11's route.
21. **T21 Bit-burst.** exp and sincos past 64k bits. After Newton division.
22. **T22 Streaming digits, million-digit constants.** Needs divide-and-conquer radix conversion.

**Launch.** T1 to T14; T15 and T16 are small and belong on a "fully featured" calculator. T17 only past 16k bits, or for a stop button. T18 to T22 can wait. Stages work: T6 gives exp and ln, T11 radians, T12 degrees.

**Item 7.** Do the prefix adder first: about ten lines, multiply 2.4 times faster at 64 bits and 3 to 4 from 256 bits up, whole functions 2.3 to 3 times, batteries included. Karatsuba blocks nothing and is blocked by nothing: little gain at 1024 bits, 5.7 times at 65536. Do it before offering more than 16k bits. Newton division and root come before T21 and million-digit constants: π at 1M bits took 286 s with Karatsuba, 240 of them in sqrt and division.

**For Matthew.**
1. Correct rounding everywhere?
2. Huge arguments: right however long, calculator warns?
3. Follow IEEE where `double` doesn't (ExpM1(−0), LogP1(−0), TanPi(¼))?
4. Is the prefix adder within the rule?
5. exp(1e30) at a precision throws `OverflowException`, not ∞?
6. `ApFloat.Pi` stays 256 bits in generic code, documented?
7. `AngleUnit`, or `SinDegrees`?

Recommended: yes to all, and `AngleUnit`.

Bench note: repeated operands train the branch predictor and flatter small sizes threefold; use a pool.

Sources: [MPFR algorithms](https://www.mpfr.org/algorithms.pdf), [Lefèvre's slides](https://GNU.org/ghm/2011/paris/slides/vincent-lefevre-mpfr.pdf), [Muller on worst cases](https://perso.ens-lyon.fr/jean-michel.muller/Intro-to-TMD.htm), [Johansson, multi-prime reduction](https://arxiv.org/abs/2207.02501v1), [Lindemann-Weierstrass](https://en.wikipedia.org/wiki/Lindemann%E2%80%93Weierstrass_theorem), [Niven's theorem](https://en.wikipedia.org/wiki/Niven%27s_theorem).