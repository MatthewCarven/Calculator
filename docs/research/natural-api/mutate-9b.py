# Recovered 2026-11-07 from the 9b chat's transcript (36ea4d6b, Write at 14:25:44 plus two Edits).
# State when that chat stopped: batch 1 (15 mutations) ran, 14 red, S13 a real hole since closed in
# StyledTextFuzz.cs. To do: rerun S13 to confirm it goes red, then the other 44. Paths inside point at
# that chat's scratchpad and at Natural: check them before running. Session A1 in Calc/TODO.md uses this.
"""Session 9b mutation battery. usage: python mutate.py <first> <count>"""
import re, subprocess, sys, time

ROOT = r"C:\Users\Admin\Desktop\-=Programming=-\Natural"
SN = r"src\Natural\StyledNumber.cs"
AT = r"src\Natural\ApInt.Text.cs"
FT = r"src\Natural\ApFloat.Text.cs"
GM = r"src\Natural\ApFloat.GenericMath.cs"
OP = r"src\Natural\ApFloat.Operations.cs"

M = [
    # StyledNumber: .NET's reading
    ("S1 white space after a sign always", [(SN, "&& ((state & Sign) == 0 || (state & Currency) != 0 || nfi.NumberNegativePattern == 2))",
                                             "&& (true || (state & Currency) != 0 || nfi.NumberNegativePattern == 2))")]),
    ("S2 the '- n' pattern ignored", [(SN, "|| nfi.NumberNegativePattern == 2))", "|| false))")]),
    ("S3 no number decimal separator fallback", [(SN, "(parsingCurrency && (state & Currency) == 0 && (next = Match(s, i, nfi.NumberDecimalSeparator)) >= 0)", "false")]),
    ("S4 group fallback after a currency symbol", [(SN, "(parsingCurrency && (state & Currency) == 0 && (next = Match(s, i, nfi.NumberGroupSeparator)) >= 0)",
                                                    "(parsingCurrency && (next = Match(s, i, nfi.NumberGroupSeparator)) >= 0)")]),
    ("S5 group separator before any digit", [(SN, "(style & NumberStyles.AllowThousands) != 0 && (state & AnyDigits) != 0", "(style & NumberStyles.AllowThousands) != 0")]),
    ("S6 group separator after the point", [(SN, "(state & AnyDigits) != 0 && (state & Point) == 0", "(state & AnyDigits) != 0")]),
    ("S7 trailing NULs refused", [(SN, "s[i..].IndexOfAnyExcept('\\0') >= 0", "i < s.Length")]),
    ("S8 trailing white needs AllowLeadingWhite", [(SN, "if (IsWhite(ch) && (style & NumberStyles.AllowTrailingWhite) != 0) continue;",
                                                     "if (IsWhite(ch) && (style & NumberStyles.AllowLeadingWhite) != 0) continue;")]),
    ("S9 a space doesn't match a no-break space", [(SN, "if (c != v && !(v is '\\u00A0' or '\\u202F' && c == ' ')) return -1;", "if (c != v) return -1;")]),
    ("S10 no hyphen for U+2212", [(SN, "return next < 0 && AllowsHyphen(nfi) && At(s, i) == '-' ? i + 1 : next;", "return next;")]),
    ("S11 exponent's positive sign not taken", [(SN, "if ((next = Match(s, j, nfi.PositiveSign)) >= 0) j = next;", "if (false) j = next;")]),
    ("S12 'e' with no digits kept", [(SN, "            if (char.IsAsciiDigit(At(s, j)))\n            {", "            i = j;\n            if (char.IsAsciiDigit(At(s, j)))\n            {")]),
    ("S13 exponent wraps instead of saturating", [(SN, "exponent = exponent < ExponentLimit / 10 ? Math.Min(exponent * 10 + (s[j] - '0'), ExponentLimit) : ExponentLimit;",
                                                    "exponent = Math.Min(exponent * 10 + (s[j] - '0'), ExponentLimit);")]),
    ("S14 an unclosed parenthesis accepted", [(SN, "if ((state & Parens) != 0 || s[i..]", "if (s[i..]")]),
    ("S15 the currency symbol twice", [(SN, "currency = \"\";   // only one", "// currency stays")]),
    # ApInt's values
    ("I1 a huge exponent isn't an overflow", [(AT, "if (number.Exponent >= 1_000_000_000) return StyledOutcome.Overflow;", "if (number.Exponent >= 1_000_000_000) return StyledOutcome.NotANumber;")]),
    ("I2 zero not settled first", [(AT, "if (!number.IsNonZero) return StyledOutcome.Parsed;", "")]),
    ("I3 a huge negative exponent gives 0", [(AT, "if (number.Exponent <= -1_000_000_000) return StyledOutcome.NotANumber;", "if (number.Exponent <= -1_000_000_000) return StyledOutcome.Parsed;")]),
    # ApFloat's text
    ("T1 the words untrimmed", [(FT, "ReadOnlySpan<char> t = s.Trim();", "ReadOnlySpan<char> t = s;")]),
    ("T2 the words case-sensitive", [(FT, "const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;", "const StringComparison ignoreCase = StringComparison.Ordinal;")]),
    ("T4 -0 loses its sign", [(FT, "ParseOutcome outcome = DecimalValue(number.Negative, number.Digits,", "ParseOutcome outcome = DecimalValue(number.Negative && number.IsNonZero, number.Digits,")]),
    ("T5 zero with a huge exponent too large", [(FT, "if (digits.AsSpan().IndexOfAnyExcept('0', '_') >= 0) return ParseOutcome.TooLarge;", "return ParseOutcome.TooLarge;")]),
    ("T6 the early TooLarge back", [(FT, "if (exponentOutcome == ParseOutcome.NotANumber) return exponentOutcome;\n\n        string separator",
                                     "if (exponentOutcome == ParseOutcome.NotANumber || (exponentOutcome == ParseOutcome.TooLarge && target.Format is null)) return exponentOutcome;\n\n        string separator")]),
    ("T7 the float style unchecked", [(FT, "StyledNumber.ValidateFloatStyle(style);\n        NumberFormatInfo nfi", "NumberFormatInfo nfi")]),
    # Generic members
    ("G1 Max's zero tie", [(GM, "if (x != y) return x.IsNaN ? x : y < x ? x : y;\n        return y._negative ? x : y;", "if (x != y) return x.IsNaN ? x : y < x ? x : y;\n        return x;")]),
    ("G2 Min drops NaN", [(GM, "if (x != y) return x.IsNaN ? x : x < y ? x : y;", "if (x != y) return x < y ? x : y;")]),
    ("G3 MaxNumber keeps NaN", [(GM, "if (x != y) return y.IsNaN ? x : y < x ? x : y;", "if (x != y) return y < x ? x : y;")]),
    ("G4 MinNumber's zero tie", [(GM, "if (x != y) return y.IsNaN ? x : x < y ? x : y;\n        return x._negative ? x : y;", "if (x != y) return y.IsNaN ? x : x < y ? x : y;\n        return y;")]),
    ("G5 MaxMagnitude's tie", [(GM, "int cmp = y.IsNaN ? -1 : CompareAbs(x, y);\n        if (cmp > 0) return x;\n        return cmp == 0 ? (x._negative ? y : x) : y;",
                                "int cmp = y.IsNaN ? -1 : CompareAbs(x, y);\n        if (cmp > 0) return x;\n        return cmp == 0 ? x : y;")]),
    ("G6 MinMagnitude ignores a NaN y", [(GM, "int cmp = y.IsNaN ? 1 : CompareAbs(x, y);", "int cmp = CompareAbs(x, y);")]),
    ("G7 MaxMagnitudeNumber keeps NaN", [(GM, "if (y.IsNaN) return x;\n        if (x.IsNaN) return y;\n        int cmp = CompareAbs(x, y);\n        if (cmp > 0)",
                                          "if (y.IsNaN) return y;\n        if (x.IsNaN) return x;\n        int cmp = CompareAbs(x, y);\n        if (cmp > 0)")]),
    ("G8 CompareAbs without zeros", [(GM, "a.IsZero || b.IsZero ? (a.IsZero ? 0 : 1) - (b.IsZero ? 0 : 1) : CompareMagnitudes(a, b);", "CompareMagnitudes(a, b);")]),
    ("G9 Sign(NaN) is 0", [(GM, "value.IsNaN ? throw new ArithmeticException(\"NaN has no sign.\") : value.SignOf();", "value.SignOf();")]),
    ("G10 CopySign keeps value's sign", [(GM, "new(value._kind, sign._negative, value._mant", "new(value._kind, value._negative, value._mant")]),
    ("G11 an odd integer even", [(GM, "(value._kind == Kind.Finite && value._exp > 0);", "(value._kind == Kind.Finite && value._exp >= 0);")]),
    ("G12 zero odd", [(GM, "value._kind == Kind.Finite && value._exp == 0;", "value._kind != Kind.NaN && value._exp == 0;")]),
    ("G13 NaN not positive", [(GM, "public static bool IsPositive(ApFloat value) => !value._negative;", "public static bool IsPositive(ApFloat value) => !value._negative && !value.IsNaN;")]),
    ("G14 Clamp passes NaN bounds", [(GM, "return Min(Max(value, min), max);", "return min.IsNaN || max.IsNaN ? value : Min(Max(value, min), max);")]),
    ("G15 ++ at DefaultPrecision", [(GM, "Add(x, s_one, x.Precision)", "Add(x, s_one, Math.Max(x.Precision, DefaultPrecision))")]),
    ("G16 One at 53 bits", [(GM, "new(Kind.Finite, false, [1], 0, DefaultPrecision);", "new(Kind.Finite, false, [1], 0, 53);")]),
    ("G17 zero normal", [(GM, "public static bool IsNormal(ApFloat value) => value._kind == Kind.Finite;", "public static bool IsNormal(ApFloat value) => value.IsFinite;")]),
    # Conversions
    ("C1 truncating checks", [(GM, "result = mode == ConversionMode.Checked ? TOther.CreateChecked(whole) : TOther.CreateSaturating(whole);",
                               "result = mode != ConversionMode.Saturating ? TOther.CreateChecked(whole) : TOther.CreateSaturating(whole);")]),
    ("C2 NaN to an integer always throws", [(GM, "if (value.IsNaN) return mode == ConversionMode.Checked ? throw new OverflowException(\"NaN has no integer value.\") : ApInt.Zero;\n        if (value.IsInfinity ||",
                                             "if (value.IsNaN) throw new OverflowException(\"NaN has no integer value.\");\n        if (value.IsInfinity ||")]),
    ("C3 UInt128's quirk dropped", [(GM, "if (typeof(TOther) == typeof(UInt128) && mode == ConversionMode.Checked && value._kind == Kind.Finite && value._negative)", "if (false)")]),
    ("C4 the stand-in from 2^64", [(GM, "(value._kind == Kind.Finite && value.Top >= 128))", "(value._kind == Kind.Finite && value.Top >= 64))")]),
    ("C5 ApInt from NaN always 0", [(GM, "if (value.IsNaN) return mode == ConversionMode.Checked ? throw new OverflowException(\"NaN has no integer value.\") : ApInt.Zero;\n        return (ApInt)value;",
                                     "if (value.IsNaN) return ApInt.Zero;\n        return (ApInt)value;")]),
    ("D1 decimal rounds toward zero", [(GM, "RoundExact(false, scaled, checked(value._exp + s), false, MaxPrecision, RoundingMode.ToNearestEven, minExp: 0);",
                                        "RoundExact(false, scaled, checked(value._exp + s), false, MaxPrecision, RoundingMode.TowardZero, minExp: 0);")]),
    ("D2 decimal keeps trailing zeros", [(GM, "while (scale > 0 && c.Length > 0)", "while (false)")]),
    ("D3 decimal starts a scale low", [(GM, "(96 - top) * 30103 / 100000 + 1", "(96 - top) * 30103 / 100000 - 1")]),
    ("D4 decimal NaN is 0 not 0.0", [(GM, ": 0.0m;", ": 0m;")]),
    ("D5 negative overflow to MaxValue", [(GM, "return value._negative ? decimal.MinValue : decimal.MaxValue;", "return decimal.MaxValue;")]),
    ("D6 from decimal, scale's sign", [(GM, "return FromDecimal(bits[3] < 0, c, -scale, DefaultPrecision", "return FromDecimal(bits[3] < 0, c, scale, DefaultPrecision")]),
    ("D7 decimal zero too soon", [(GM, "if (top < -96) return true;", "if (top < -90) return true;")]),
    ("D8 decimal overflow too soon", [(GM, "if (top >= 96) return false;", "if (top >= 95) return false;")]),
    # C#'s %
    ("R1 a zero remainder positive", [(OP, "return rem.Length == 0 ? ZeroOf(x._negative, precision)", "return rem.Length == 0 ? ZeroOf(false, precision)")]),
    ("R2 |x| == |y| returns x", [(OP, "CompareMagnitudes(x, y) < 0) return x.WithPrecisionExactly(precision);", "CompareMagnitudes(x, y) <= 0) return x.WithPrecisionExactly(precision);")]),
    ("R3 the grid's k off by one", [(OP, "ulong k = (ulong)(x._exp - e);", "ulong k = (ulong)(x._exp - e + 1);")]),
    ("R4 % raises no Invalid", [(OP, "            flags |= IeeeFlags.Invalid;\n            return NaNOf(precision);\n        }\n        // |x| < |y| truncates",
                                 "            return NaNOf(precision);\n        }\n        // |x| < |y| truncates")]),
    ("R5 % at x's precision", [(OP, "internal static ApFloat Remainder(ApFloat x, ApFloat y, ref IeeeFlags flags)\n    {\n        int precision = Math.Max(x.Precision, y.Precision);",
                                "internal static ApFloat Remainder(ApFloat x, ApFloat y, ref IeeeFlags flags)\n    {\n        int precision = x.Precision;")]),
]


def apply(text, old, new):
    for o, n in ((old, new), (old.replace("\n", "\r\n"), new.replace("\n", "\r\n"))):
        if text.count(o) == 1:
            return text.replace(o, n)
    raise ValueError(f"not found exactly once: {old[:70]!r} (count {text.count(old)}, crlf {text.count(old.replace(chr(10), chr(13) + chr(10)))})")


def run(name, edits):
    originals = {}
    try:
        for path, old, new in edits:
            full = ROOT + "\\" + path
            if full not in originals:
                originals[full] = open(full, "rb").read()
            text = open(full, "rb").read().decode("utf-8")
            open(full, "wb").write(apply(text, old, new).encode("utf-8"))
        t0 = time.time()
        try:
            p = subprocess.run(["dotnet", "test", "Natural.slnx", "--nologo", "--logger", "console;verbosity=normal"],
                               cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=240)
        except subprocess.TimeoutExpired:
            return f"TIMEOUT    {name}"
        out = p.stdout + p.stderr
        dt = time.time() - t0
        if "error CS" in out:
            err = re.search(r"error CS\d+: [^\[]*", out)
            return f"BUILD ERR  {name}: {err.group(0) if err else ''}"
        failed = sorted(set(re.findall(r"Failed Natural\.Tests\.(\S+)", out)))
        total = re.search(r"Total tests:\s+(\d+)", out)
        if failed or p.returncode != 0:
            return f"RED        {name} ({dt:.0f} s, exit {p.returncode}): {', '.join(f.split('.')[-1] for f in failed[:4])}"
        return f"green      {name} ({dt:.0f} s, {total.group(1) if total else '?'} tests)"
    finally:
        for full, data in originals.items():
            open(full, "wb").write(data)


if sys.argv[1] == "check":
    for name, edits in M:
        for path, old, new in edits:
            try:
                apply(open(ROOT + "\\" + path, "rb").read().decode("utf-8"), old, new)
            except ValueError as e:
                print(f"BAD MUT    {name}: {e}")
    print(f"checked {len(M)}")
    sys.exit(0)

first, count = int(sys.argv[1]), int(sys.argv[2])
for name, edits in M[first:first + count]:
    try:
        print(run(name, edits), flush=True)
    except ValueError as e:
        print(f"BAD MUT    {name}: {e}", flush=True)
print(f"done {first}..{first + count - 1} of {len(M)}", flush=True)
