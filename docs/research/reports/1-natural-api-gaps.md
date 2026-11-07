**Verdict:** every listed feature can be built on the public API today, including the transcendentals, except stopping or stepping arithmetic while it runs. TODO item 8 puts the transcendentals in Natural, but they work on top, so where they live is Matthew's call. Three defects need fixing in Natural; Calc can guard against all three meanwhile.

Basis:
- 682 tests pass.
- The private copy matches the live tree (MD5, 56 files). Live Natural is unchanged and Calc is still empty.
- Timings are single runs on the two-speed machine.

## 1. Features

| Feature | Status | Call, or how |
|---|---|---|
| + − × ÷ negate | PRESENT | Operators; `ApFloat.Add/Subtract/Multiply/Divide(a,b,p or format,mode)`; `IeeeContext` |
| %, mod | PRESENT | `%` truncates (both types); `ApFloat.Ieee754Remainder`. Floored mod ON-TOP: add b if signs differ |
| Integer power | PRESENT / ON-TOP | `ApInt.Pow(x,int)`. ApFloat: `new ApFloat(ApInt.Pow(x.Significand,n), x.Exponent*n, p, mode)`, one rounding, 8/8 |
| sqrt | PRESENT | `ApFloat.Sqrt`, `ApInt.Sqrt/SqrtRem`, `ieee.Sqrt` |
| nth root, cbrt | ON-TOP | Newton on ApInt, sticky bit into the public constructor; 96/96 in all four modes |
| abs, sign | PRESENT | `Abs`; `ApInt.Sign` (property); `ApFloat.Sign(x)` |
| floor, ceil, trunc, round | PRESENT | `ApFloat.Floor/Ceiling/Truncate/Round`, `ieee.RoundToIntegral` |
| exp, ln, log10, log2, log b, real power | ON-TOP | Series plus Ziv on `Add/Multiply/Divide`; log b is ln ÷ ln; run |
| sin, cos, tan, atan, sinh | ON-TOP | Same; run |
| asin, acos, atan2, cosh, tanh, inverses | ON-TOP | Derive from those; not run |
| π, e, τ, φ, ln2 | ON-TOP | ApInt fixed point (Machin); φ by `Sqrt`; 20/20 |
| factorial, nCr, nPr | ON-TOP | ApInt loops, product tree |
| gamma | ON-TOP | Needs exp, ln, π; not run |
| gcd, lcm, modpow | PRESENT | `GreatestCommonDivisor`, `ModPow`; lcm ON-TOP |
| Modular inverse | ON-TOP | Extended Euclid on `DivRem`; 1213/1213 |
| Primality, factorisation | ON-TOP | Miller-Rabin on `ModPow`; Pollard rho |
| and, or, xor, not, shifts | PRESENT | `& \| ^ ~ << >>`, infinite two's complement; mask to a width |
| Rotates at word size | ON-TOP | `((x<<r)\|(x>>(w-r)))&mask`; not `RotateLeft` |
| Base 2, 16 out | ON-TOP | `ApInt.Abs(x).ToString("X").TrimStart('0')`; handle 0 and the sign |
| Base 2, 16 in | PRESENT | `ApInt.Parse("-0xFF")`, `"0b101"` |
| Base 8 | ON-TOP | Out: group `"B"` digits in threes. In: Horner. No octal text anywhere |
| Base 10 | PRESENT | `ToString()`, `Parse` |
| Any radix | ON-TOP | Chunked `DivRem`, Horner; 3000 round trips |
| ApInt ↔ ApFloat | PRESENT | Implicit, exact to 2^30 bits; explicit `(ApInt)x` truncates |
| Change precision | PRESENT | `WithPrecision`, `WithFormat`, `ieee.Convert` |
| ulp, next up/down | ON-TOP | Top = `Exponent + Significand.BitLength - 1`; `Add(x,tiny,p,TowardPositive)`; 19,734 doubles against `Math.BitIncrement`, 0 wrong. Next of 0 exists only in a format |
| Compare | PRESENT | Operators, `CompareTo`, `Max/Min` family, `Clamp` |
| ApFloat text | PRESENT | Default, E, F, G, G<n>, R, B, A, X, custom `0 # . ,`. No N, C, P, %, engineering |
| ApInt text | PRESENT | D<n>, G, R, X, x, B |
| Grouping | PRESENT | ApFloat `"#,##0.00"`; ApInt via `((ApFloat)x).ToString("#,##0")`. en-IN sizes NEEDS-NATURAL (cosmetic) |
| Parse, culture | PRESENT | Own syntax at any precision; `NumberStyles` at 256 bits only (`ParseStyled` is internal). Strip separators for other precisions |
| IEEE bytes | PRESENT | `ToIeeeBytes`, `FromIeeeBytes`; `ToMagnitudeBytes`, `FromMagnitude` |
| Flags, modes | PRESENT | `IeeeContext`, `IeeeFlags`, four `RoundingMode`s; `Flags` is settable |
| Text as jobs | PRESENT | `StartParse`, `StartFormat`; ApInt via `(ApFloat)x`; `Job.Start` for Calc's own loops |
| Arithmetic as jobs, cancel | NEEDS-NATURAL | Nothing in `Magnitude` yields or polls |

All 298 prototype results were correctly rounded, checked against exact integers and Python's `decimal`: 194 functions and constants, 96 roots, 8 powers. Functions ran at 53, 256 and 1024 bits. `exp(1.5)` at 1024 bits took 10 ms and `ln(10)` 36 ms.

## 2. Traps

| Trap | Measured | Rule |
|---|---|---|
| Operators widen | `x64*3` is 256 bits; `x64*3.0` stays 64 | One `new IeeeContext(p)` per precision: its target overrides operands |
| Contamination | `x1024+0.1` claims 1024 bits, 17 digits right; `x64.WithPrecision(1024)` 20 right | Clearing is correct; clear memories too. My own 512-bit inputs produced 6 wrong answers at 1024 bits |
| Hex is two's complement | 255 `0FF`, -255 `F01`, `Parse("0xF01")` 3841, HexNumber `FF` -1 | The plain route above is public; `ToRadixDigits` isn't. ApFloat `"X"` is hexfloat; its `"B"` is plain |
| Word quirks | `PopCount(-1)` 32; `RotateLeft(1,40)` 256, `(0x81,1)` 258; `LeadingZeroCount(1)` 31; width varies by value; 32 of 63 rotations wrong | Hold masked non-negatives; recipes 0 wrong of 140,000 against hardware |
| Unreachable members | Compiler refused `ToRadixDigits`, `Limbs`, `Target`, flagged cores, `ParseStyled`, `Magnitude` | `RoundExact` and `Top` are private, not internal, so `InternalsVisibleTo` wouldn't help. The public constructor plus a low sticky bit replaces `RoundExact` |
| Casts wrap | `(int)(ApInt)5000000000` 705032704; `Pow(2,(int)(2^32+3))` 8 | `int.CreateChecked`. `(long)2^(2^35)` is OOM at the cap; `long.CreateChecked` gives Overflow |
| Constructor rounds | `new ApFloat(apInt)` is 256 bits; implicit keeps all | Use implicit |
| Culture | de-DE `Parse("1.5")` throws; U+2212 refused in en-*; en-IN groups 1,234,567.5, .NET 12,34,567.5 | Invariant internally; pass the culture only for display |
| Display | `R` 32.4 s against `G19728` 0.87 s at 65,536 bits; `0*-1` prints -0; `"F999999999"` unfinished at 8 s; `"E1234567890"` prints `E1234567892` | Show `G(DecimalDigitsFor(p)-2)`; cap digits; fold -0 |

**Exceptions**

| Type | When |
|---|---|
| FormatException | Bad text, bad format, null to own-syntax `Parse` |
| OverflowException | Exponent past long; NaN, ∞ or out of range to integer; F longer than a string; `(ApInt)x` with exponent ≥ 2^36; ApInt styled exponent ≥ 10^9; printing at the very top of the exponent range |
| OutOfMemoryException | Any big allocation; catchable at the cap |
| DivideByZeroException | ApInt `/ % DivRem`, `ModPow` modulus 0 |
| ArithmeticException | `ApInt.Sqrt` negative, `ApFloat.Sign(NaN)` |
| ArgumentOutOfRangeException | Precision outside 1..2^30 (even `TryParse`), negative `Pow`/`ModPow` exponent, `Log2` negative, bad `IeeeFormat`, bad mode only if rounding happens |
| ArgumentException | Hex style on ApFloat (even `TryParse`), `Clamp` min > max, wrong byte length, `default(IeeeFormat)`, `CompareTo` wrong type |
| ArgumentNullException | Styled `Parse(null)`, `StartParse(null)` |
| InvalidOperationException | `Significand`/`Exponent` of ∞ or NaN, early `Job.Result` |
| ObjectDisposed, NotSupported | `Continue` after `Dispose`; `CreateChecked` from `Complex` |

## 3. Extremes (1 GiB cap, child processes)

| Case | Result |
|---|---|
| `Pow(2,10^9)` | 1.16 s; `Pow(2,int.MaxValue)` OOM |
| `Pow(3,10^5)` | 2.65 s; 3×10^5 unfinished at 22 s |
| x² from 3 (ApInt) | Press 17 3 s, press 18 12 s, about ×4 each |
| `One << int.MaxValue` | 0.25 s; heap read 512 MiB for a 256 MiB value; second shift OOM, caught, then fine |
| Decimal text | 2^18 bits 1.8 s; 2^20 unfinished at 10 s |
| MaxPrecision 2^30 | Beyond: ArgumentOutOfRangeException. `Divide(1,3,2^28)` 9.5 s; 2^30 unfinished at the 40 s limit |
| `Sqrt(2,p)` | 2^16 2 s, 2^18 32 s |
| `Multiply(x,x,64)`, x 2^18 bits | 7.9 s; instant after `WithPrecision(64)` |
| Exponent overflow | OverflowException at x² press 63-64, never ∞, even in `IeeeContext(Binary64)` |
| ÷0 | ApInt throws; ApFloat gives ±∞ or NaN, flagged only in a context |
| Interrupting arithmetic | `Thread.Interrupt` nothing; `Abort` unsupported; `ControlledExecution.Run` stopped 3 of 3 within 61 ms (SYSLIB0046 warns of corruption); process kill clean 11 of 11 |
| Job slices | F0 of 2^(2^24): worst `Continue(50 ms)` 50.5 ms. At 65,536 bits one step blocks 800 ms |
| Interactive ceiling (my inference) | 16,384 bits: Multiply 0.12 s, Sqrt 0.23 s, display 0.06 s. 65,536 bits: 0.54, 2.0, 0.87 s |

**Defects for Natural**

| Defect | Proof | Calc's guard |
|---|---|---|
| `Top` is unchecked: silent wrong answers | `1e2776511644261678567` parses, `> 1` false, `(double)` 0; 76 decimal exponents affected at 256 bits; plain doubling reaches it | Treat abs(Exponent + BitLength) ≥ 2^62, in Int128, as overflow (not run) |
| `ToRadixDigits` `int count`: silent wrong answer | `"B"` of a 4,294,967,329-digit value returned 33 zeros | Cap output length |
| `Ieee754Remainder` lacks `%`'s early-out | `(3, 2^(10^12))` OverflowException; should be 3 | Return x when Top(x) < Top(y) − 1 (not run) |

## 4. Docs against code

| Where | Says | Code |
|---|---|---|
| README 325, 348 | 666 tests | 682; TODO 523 agrees |
| README 253 | -255 is `-FF` | `F01`; the other 71 samples match |
| README 228 | Remainders exact however far apart | Throws, as above |
| README 295 | Refuses only exponents past a long | Accepts the wrapped window |
| `ApFloat.cs` 449 | Implicit from ApInt "Exact" | Rounds past 2^30 bits |
| TODO 120, CLAUDE.md 94 | .NET rounds ties away | `double` on 10.0.13 gives 0.12 and 2; only `decimal` goes away |
| TODO 523 | Mutations in WORKLOG | WORKLOG stops at 9a |

## Not verified

- **Hard cases:** no huge arguments such as sin(1e100), no near-ties, and functions not run at 4096 bits. Ziv's slack was chosen by eye, not proved.
- **Number theory:** factorisation only on one 60-bit semiprime; Miller-Rabin only on 8 numbers.
- **Not examined:** GitHub, the playground's UI, and `ControlledExecution` beyond three trials.
- **Machine load:** free RAM fell from 2.2 GB to 486 MB by the end, with none of my processes left running.

Everything is in `C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\api-probes\`:
- `notes.txt`: longer notes
- `OnTop.cs`: the recipes to copy
- `oracle.py`: the exact checker
- `surface-public.txt`: the full public surface