# Natural speed bench: report

**Headline:** on today's library, plan on 4096 bits (1,233 digits) as the safe ceiling. Speed can follow transcendentals, but I recommend keeping the TODO's order.

**Machine:** i9-11900H, 8 cores/16 threads, 15.8 GB RAM, mains power. Runtime 10.0.13, Release, JIT warmed. It was idle at the start (under 7% CPU, no dotnet or python). Sibling jobs then shared it: total CPU median 17.5%, peak 47%. My calibration loop stayed at 28–39 ms, so no ×3 slow phase appeared. Treat figures as ±25%: E's old arm ran about 20% faster than B.

## A. Tests
682 tests, 682 passed, 18.5 s. The README's "666" is stale.

## B. ApFloat (bits; median of 3)
`*` = one timing, past 20 s. `—` = series stopped or predicted past the cap.

| | 53 | 256 | 1024 | 4096 | 16384 | 65536 | 262144 | 1048576 |
|---|---|---|---|---|---|---|---|---|
| Add | 244 ns | 389 ns | 650 ns | 2.1 µs | 8.1 µs | 35 µs | 175 µs | 800 µs |
| Multiply | 1.2 µs | 14 µs | 154 µs | 2.7 ms | 41 ms | 864 ms | 15.4 s | — (274 s) |
| Divide | 1.9 µs | 11 µs | 290 µs | 4.8 ms | 82 ms | 1.24 s | 20.5 s* | — |
| Sqrt | 2.0 µs | 38 µs | 481 µs | 7.0 ms | 121 ms | 1.83 s | 29.9 s* | — |
| ÷25 (extra) | 2.8 µs | 8.3 µs | 34 µs | 137 µs | 576 µs | 2.2 ms | 8.8 ms | 34 ms |
| "R" | 39 µs | 429 µs | 7.3 ms | 132 ms | 3.04 s | — (70 s) | — | — |
| E, all digits | 2.9 µs | 9.1 µs | 226 µs | 4.4 ms | 68 ms | 1.23 s | 21.5 s* | — |
| E, 50 digits (extra) | 5.1 µs | 6.5 µs | 15 µs | 122 µs | 521 µs | 2.2 ms | 8.6 ms | 34 ms |
| Parse, all digits | 3.0 µs | 11 µs | 324 µs | 4.9 ms | 93 ms | 1.43 s | 23.9 s* | — |

The harness built the operands, and they matched ApFloat.Divide and Sqrt exactly up to 4096 bits.

## C. ApInt

| | Value | Decimal ToString |
|---|---|---|
| 1000! | 9.2 ms | 1.5 ms |
| 10000! | 1.74 s | 281 ms |
| 100000! | 297 s (one run) | not run, ~45 s predicted |
| Pow(3, 100000) | 2.31 s | 499 ms |
| Pow(2, 10⁷) | 9.5 ms | not run, ~33 min predicted |

- **ModPow:** 256 bits 16.7 ms, 1024 867 ms, 2048 6.79 s, 4096 52.3 s (one run).
- **Others:** GCD at 4096 bits 3.2 ms. Sqrt at 100000 bits 979 ms. ToString at 1M bits 19.8 s.
- **Cap overruns:** 100000! and ModPow 4096 passed the 20 s cap. I predicted 8 s for the factorial and put no guard on ModPow, and the bench can't abort an operation in flight.

## D. Prototypes, public API only (median of 3)

| | 256 | 1024 | 4096 | 16384 | 65536 |
|---|---|---|---|---|---|
| π Machin | 2.3 ms | 24 ms | 334 ms | 5.6 s | — |
| π Machin, shrinking precision | 1.5 ms | 13 ms | 176 ms | 2.6 s | — |
| π Chudnovsky, binary splitting (extra) | 208 µs | 2.1 ms | 28 ms | 440 ms | 8.2 s |
| ln 2 atanh, shrinking | 1.7 ms | 16 ms | 196 ms | 3.2 s | — |
| e Taylor, r=8, squaring | 453 µs | 4.6 ms | 58 ms | 911 ms | 15 s |
| e binary splitting (extra) | 111 µs | 826 µs | 10 ms | 133 ms | 2.3 s |
| sin(1) | 282 µs | 2.0 ms | 21 ms | 257 ms | 3.7 s |
| sin²1+cos²1−1 | 672 µs | 4.8 ms | 51 ms | 648 ms | 10.2 s |
| sin(x), full x, plain Taylor | 1.5 ms | 28 ms | 1.09 s | — | — |
| sin(x), concurrent series (Smith) | 712 µs | 8.4 ms | 152 ms | 4.3 s | — |
| exp(x), concurrent series | 885 µs | 10 ms | 186 ms | 4.8 s | — |
| ln(x), Halley on exp | 1.6 ms | 12 ms | 165 ms | 4.1 s | — |
| x^y | 2.5 ms | 22 ms | 348 ms | 9.4 s | — |

**Checks:** every result agreed with a BigInteger oracle or an inverse check to within 2 bits of p. The first 50 digits of π, e and ln 2 were right. sin²+cos²−1 left zero or a few ulps of working precision.

**What dominates:**
- With x=1, or on Machin and atanh, ÷small takes 97–99%, at a flat 33 ns per bit.
- With a full x, Multiply takes 83–94%. So sin(1) understates trig: sin(x) costs 7× more at 4096 bits.

**Exploiting smallness (π at 16384 bits):**

| Route | Time |
|---|---|
| Plain Machin | 5.6 s |
| Shrinking precision | 2.6 s |
| Bitwise short division on limbs | 1.08 s |
| CPU divide (breaks the rule, a ceiling only) | 18 ms |
| Binary splitting (no small divides) | 440 ms |

## E. Old against new: an estimate, not a proposal
This is a throwaway copy with 82 added lines in Magnitude.cs. All 682 tests passed in the default setup, and again with the Karatsuba threshold forced to 4 limbs and short division on. The threshold is flat from 8 to 16 limbs, so I used 12. I also added short division (extra). Arms alternated process by process over three passes.

| | Old | Adder | + Karatsuba | + short divide |
|---|---|---|---|---|
| Multiply 1k | 115 µs | 89 µs | 48 µs | 45 µs |
| Multiply 10k | 10.7 ms | 8.9 ms | 2.2 ms | 2.1 ms |
| Multiply 100k | 1.91 s | 883 ms | 89 ms | 85 ms |
| Multiply 1M | ~300 s (predicted) | ~90 s (predicted) | 3.48 s | 3.31 s |
| π Machin 256 | 1.36 ms | 491 µs | 485 µs | 379 µs |
| π Machin 1024 | 15 ms | 4.4 ms | 4.0 ms | 2.7 ms |
| π Machin 4096 | 169 ms | 52 ms | 52 ms | 36 ms |
| π Machin 16384 | 2.72 s | 847 ms | 778 ms | 511 ms |
| π Machin 65536 | not run | 13.5 s | 13.4 s | 8.5 s |
| π Chudnovsky 16384 | 457 ms | 149 ms | 90 ms | 84 ms |
| sin(x) 16384 | 4.02 s | 1.21 s | 367 ms | 312 ms |
| sin(x) 65536 | not run | not run | 5.42 s | 5.03 s |
| Divide 65536 | 1.03 s | 379 ms | 394 ms | 334 ms |
| E all digits 65536 | 960 ms | 353 ms | 153 ms | 134 ms |
| Parse 65536 | 1.17 s | 402 ms | 359 ms | 356 ms |

Machin has no Multiply, so Karatsuba can't help it.

**Division:** restoring division gains only the adder's ×3. Newton on the fast Multiply took 129 ms against 470 ms at 65536 bits, and 0.97 s against 5.9 s at 262144. It overtakes restoring near 5,000 bits. It isn't correctly rounded yet, though it matched at three sizes.

## F. Recommendations

| Limit | Today | Throwaway |
|---|---|---|
| All basic ops under 50 ms | 11,200 bits (3,380 digits; Sqrt limits) | 19,500 bits |
| "R" under 50 ms | 2,600 bits | not computed (124 ms at 8192) |
| sin, cos under 500 ms | 6,900 bits (2,070 digits) | 19,600 bits |
| exp / ln under 500 ms | 6,300 / 6,700 bits | 17,700 / 16,400 bits |
| x^y under 500 ms | 4,800 bits | ~11,700 bits |

These trig figures are prototypes with x in (0,1). A real build adds argument reduction, π on first use (75–100 ms there) and correct rounding, which is why the safe ceiling is 4096 bits.

**Presets:**
- 53, 113, 256 (default), 512, 1024, 2048, 4096. Everything stays under 350 ms.
- 8192, labelled slow: sin 0.75 s, x^y 1.7 s.
- 16384 once speed lands: sin 0.36 s, x^y 0.93 s.

**Display:** show short E or G text (34 ms even at 1M bits) and give full digits on request. Avoid "R" above about 2,000 bits.

**Order:** speed need not come first. Up to 4096 bits, transcendentals work on today's arithmetic if they use concurrent series (7–12× over plain Taylor) and binary splitting for constants. I'd still keep the TODO's order:
1. Adder + Karatsuba + short division: one small session, one file, strong oracles, sin ×11–13 at 16384 bits, and nearly three times the trig ceiling.
2. Newton Divide and Sqrt, correctly rounded.
3. Decimal text: Parse stays quadratic, so step 1 lifts the basic-op ceiling only to 19,500 bits. Step 2 alone doesn't fix it.

**Open or surfaced:**
- The TODO's "Ask Matthew" on the parallel-prefix adder and the rule is still open.
- ApFloat has no ScaleB.
- Arithmetic can't be cancelled mid-operation, so higher precisions need a worker thread, and integer extras need limits.

## Not verified
- Speed on an idle machine, in the ×3 slow phase, or on the desktop. A slow phase would cut these ceilings by about 1.7× in bits.
- Worst-case carry patterns.
- Cold-start JIT cost.
- Correct rounding of the prototypes, and x outside (0,1).
- Predicted figures are extrapolations.

The real Natural repo and Calc are untouched: Natural's newest file is dated 2026-10-15 and Calc is empty.

**Bench files**, all in `C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\bench\`:
- `bench.cs` (commands in its header; first line is an absolute `#:project` path)
- `E-setup.ps1`, `E-compare.ps1`, `E-256.ps1`, `E-threshold.ps1`, `E-threshold2.ps1`, `F-refine.ps1`, `monitor.ps1`
- `analyse.py`
- `natural-fast\`, the throwaway copy