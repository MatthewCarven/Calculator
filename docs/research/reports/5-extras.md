Tested on a scratch copy of the local tree (session 9b); Natural and Calc untouched. Timings are medians of interleaved rounds. Multiply takes 21 µs at 256 bits, 293 µs at 1,024, 4.4 ms at 4,096 and 1.1 s at 65,536, and one multiply can't be interrupted.

Cost: S one session, M two or three, L four or more. T is Natural's transcendentals (item 8); Speed is item 7. Phases: 1 core, 2 truth tools, 3 everyday, 4 depth, 5 after T, 6 after Speed.

## Ranked features

| # | Feature | Value and evidence | Cost | Depends on | Phase |
|---|---|---|---|---|---|
| 1 | Exact-first (ApInt ratios) | 0.1+0.2 is 3/10; Muller n=100 exact in 261 bits, float needed 848. Needs a size budget (gcd 4.5 ms at 4,096 bits, 64 ms at 16,384) and a literal guard: 1e(10^18) would take 3.3×10^18 bits. Off in format mode | M | Parser | 1 |
| 2 | History stores source, recompute | Makes the clear lossless; stored `Ans` becomes a line reference | S | Parser | 1 |
| 3 | One evaluator, swappable backends, job runner | Float, interval and exact backends shared one evaluator here. Any Calc loop can be a `Natural.Jobs` job: longest 16 ms slice took 27 ms at 2,203 bits | M | None | 1 |
| 4 | Display digits, notation, grouping, aligned column | Show floor((p−1)·log10 2), 76 at 256 bits: `DecimalDigitsFor`'s 79 makes 1/3 end …3348. Engineering notation isn't in ApFloat | S | None | 1 |
| 5 | Proven digits (intervals) | Rump: 0 proven at 121 bits, 36 at 122. 4.8× float with naive products. Zero proven means unproven, not wrong | M | 3 | 2 |
| 6 | Format mode, side by side with truth | Top five; binary64 matched hardware bit for bit | S | 1, 3 | 2 |
| 7 | Rounding selector, flag lamps | Per line, not sticky. Overflow and Underflow need format mode: 1e300² at 256 bits raises only Inexact | S | 6 | 2 |
| 8 | Bit inspector, exact stored value | Port `FloatReport`. 0.1 in binary64 is 3602879701896397/36028797018963968 | S | 6 | 2 |
| 9 | Variables, functions, memory, recursion | Stored as text; memory is short-named variables | M | 2 | 3 |
| 10 | Keyboard shortcuts, autocomplete | Daily comfort | M | 9 | 3 |
| 11 | Sessions, export, receipts | `A` text round-tripped 640 of 640 random values, specials too | S | 2 | 3 |
| 12 | Programmer mode, any word size, bases | Wrap, sign and rotate correct at 8 and 1,024 bits; Windows stops at 64. Mask ourselves: `PopCount(-1)` is 32, `RotateLeft(1,40)` is 256 | S | 3 | 3 |
| 13 | Digit-loss x-ray | Top five | S | 5 | 4 |
| 14 | N proven digits on demand | Top five; needs a round cap | M | 5 | 4 |
| 15 | Precision sweep | Top five; say "stable", never "correct" | S | 5 | 4 |
| 16 | Number theory | ModPow 1 s at 1,024 bits, 7.1 s at 2,048: run as jobs. Miller-Rabin, 12 bases: 1.1 s at 521 bits. Rho: 62-bit semiprime 0.5 s, 80-bit 18 s | M | 3 | 4 |
| 17 | Sum, product, sequences, descriptive statistics | Exact H_1000 0.48 s; float 9 ms | S | 1, 9 | 4 |
| 18 | Proven solver | x³−x−2: sign change proven, 76 digits, 120 ms at 256 bits. Polynomials now | M | 5, 9 | 4 |
| 19 | Uncertainty input, identify fraction | 0.142857142857142857 gives 1/7; π gives 245850922/78256779, so cap denominators | S | 1, 5 | 4 |
| 20 | Units | Exact rational factors, local table | M | 1 | 4 |
| 21 | Line references, live recalculation | Soulver's model; reruns must be jobs | M | 2, 9 | 4 |
| 22 | RPN mode | Second input model, same evaluator | M | 3 | 4 |
| 23 | Matrices | Exact Bareiss determinant, 40×40: 434 ms. Entry UI is the cost | M | 1 | 4 |
| 24 | Scientific functions | Rough sin 1.2 ms at 256 bits, 29 ms at 1,024. Must take a rounding mode, or intervals can't use them | L | T | 5 |
| 25 | Complex numbers | Arithmetic and √ now; polar and functions need T | M | 24 | 5 |
| 26 | Plotting | 800 points: 1.05 s at 256 bits, 129 ms at 64, 0.017 ms in double. Pan in double, re-render deep zooms in Natural | L | 24 | 5 |
| 27 | Numeric integration | Tanh-sinh at 256 bits: 2,489 evaluations, 6.4 s; an estimate, not proof | M | 24 | 5 |
| 28 | Long constants, huge demos | π to 10,000 digits 9.4 s; 20000! 8.3 s | S | Speed | 6 |

## What "setting precision clears the calculator" should mean

Natural makes the clear necessary. `Ans` at 53 bits, times 3 at 256, gave a 256-bit value with 16 good digits. `Ans` at 256 bits plus 0.2 at 53 gave 0.3; fresh gave 0.30000000000000004. The `+` operator kept 256 bits with the setting at 53, so the evaluator must use only static or `IeeeContext` calls. Python's decimal and bc also keep per-value digits.

Rule: changing the target (precision or format) drops every approximate value and keeps all text. The same target is a no-op; rounding mode alone doesn't clear.

- **Gone:** result, pending operation, RPN stack and `Ans` (numbers at the old target); flags; running jobs.
- **Kept:** the entry line (text, no precision in it), settings, and cached constants (keyed by bits, never mixed, costly to rebuild).
- **Kept, dimmed:** history, tagged with its old target. Recompute is on request, because a rerun can be slow.
- **Kept as definitions:** variables, functions and memory; values rebuild on next use.

Matthew's call: should the clear wipe history too? I recommend no. Keeping it can't corrupt anything; wiping can't be undone.

## Top five "only this calculator can do that"

1. **Digit-loss x-ray.** Bounds on every operation name the one that spent the digits. Rump at 100 bits: op 16 of 19, a `+`, drops 98 proven bits to 0. Nothing surveyed lists this.
2. **Format lab.** One expression in binary16 to binary256, bfloat16 or any custom format, in four rounding modes, with exact error. 0.1+0.2: −0.8 ulp in binary16, +0.4 in binary32, +0.8 in binary64.
3. **N proven digits.** Natural's Ziv rule over the whole expression: both ends must print alike. Rump, of 40 digits: 0 at 64 bits, 37 at 128, 40 at 256. √2²−2 straddles 0 through 4,096 bits, so it must report a bound. Rare, not unique: Qalculate has intervals, and Android's reals hit the same equality wall.
4. **Fooled-by-precision sweep.** Rump gives identical wrong digits at six precisions from 80 to 121 bits, then is right at 122. Muller n=100 gives exactly 100 at 53, 106 and 212 bits; the truth is 6.00000002. Survival alone proves nothing.
5. **Bit-exact receipt.** Expression, target, mode and hex significand, replaying to the same bits. The library's arithmetic calls no floating-point maths (checked by search).

## Skip

- **Live currency or stocks:** needs a network, and a stale rate beside proven digits misleads.
- **Natural-language input:** Wolfram's is a server-side knowledge engine; `to` conversions cover the useful part.
- **Symbolic algebra:** a bigger project than the calculator.
- **Scripting language:** functions, recursion and `sum` cover calculator use.
- **Base-10 float type:** exact ratios give decimal exactness, and it would leave Natural.
- **Certificate as a headline:** 0.1 gives 0 rounds and no bounds; 1e5000 gives one round. One inspector line.
- **As-you-type at high precision:** ten operations cost about 44 ms at 4,096 bits. Above that, run on Enter.
- **Big-prime work past 1,024 bits, before Speed:** ModPow grows roughly cubically.

## Not verified

- Competitors come from published pages or search; none was run. Wolfram and TI are general knowledge. Qalculate's word-size limit and Android's scrolling are unconfirmed.
- Transcendental timings are rough prototypes with unproven rounding.
- Intervals cover + − × ÷ √ only; directed transcendentals are untested.
- Receipt replay is reasoned, on one machine only.
- `FloatReport` was read, not compiled. No UI was built; Natural's tests weren't run.
- ModPow at 64 and 256 bits was inconsistent, so it's omitted. 4,096 bits missed its time cap.

Notebook candidate: repeating one operand pair flattered multiply 3.3× at 256 bits (branch prediction, my inference). Benches need fresh operands.

Experiments: C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\extras\demo.cs

Sources: [Qalculate](https://qalculate.github.io/features.html), [SpeedCrunch](https://speedcrunch.org/), [Python decimal](https://docs.python.org/3/library/decimal.html), [bc](https://www.gnu.org/software/bc/manual/html_mono/bc.html), [Free42](https://thomasokken.com/free42/), [Android reals](https://blog.acolyer.org/2020/10/02/toward-an-api-for-the-real-numbers/), [Windows programmer mode](https://www.mintlify.com/microsoft/calculator/features/programmer-calculator), [Soulver](https://soulver.app), [NumWorks](https://numworks.com/manual/calculation).