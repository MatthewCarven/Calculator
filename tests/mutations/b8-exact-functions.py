"""Calc's B8 mutation battery on exact functions, RoundedCall and exact display, by ../Natural/CLAUDE.md's recipe.

usage: python tests/mutations/b8-exact-functions.py <first> <count> | check
Each mutation's old text must match exactly once. It's applied, the Exact*, RoundedCall and Function tests run, and the
file is restored in `finally`. All 38 were predicted red, and all 38 were (2026-11-27). Don't build Calc while it runs.
"""
import ctypes, re, subprocess, sys, time
from pathlib import Path

ROOT = str(Path(__file__).resolve().parents[2])
sys.stdout.reconfigure(encoding="utf-8")
EA = r"src\Calc.Engine\Eval\ExactArithmetic.cs"
EF = r"src\Calc.Engine\Eval\ExactFunctions.cs"
RC = r"src\Calc.Engine\Eval\RoundedCall.cs"
FA = r"src\Calc.Engine\Eval\FloatArithmetic.cs"
RT = r"src\Calc.Engine\Text\ResultText.cs"

M = [
    # The brief's five
    ("M1 SqrtRem's remainder ignored", [(EA, "return (remainder.IsZero ? root << (int)(twos / 2) : null, true);", "return (root << (int)(twos / 2), true);")]),
    ("M2 a ratio floor on negatives", [(EF, "return r.IsNegative ? q - ApInt.One : q;", "return q;")]),
    ("M3 the helper taking one end", [(RC, "Value[] high = Rounded(arguments, width, RoundingMode.TowardPositive);", "Value[] high = Rounded(arguments, width, RoundingMode.TowardNegative);")]),
    ("M4 the bound off by one", [(RC, "long bound = Math.Min(checked(start + sizes), ApFloat.MaxPrecision);", "long bound = Math.Min(checked(start + sizes - 1), ApFloat.MaxPrecision);")]),
    ("M5 an erroring end accepted", [(RC, "                floats.Flags = before;\n                return null;\n            }\n            IeeeFlags raised", "                continue;\n            }\n            IeeeFlags raised")]),
    # RoundedCall
    ("M6 infinity accepted as an answer", [(RC, "                         || result is FloatValue { Number: { IsFinite: false } };", ";")]),
    ("M7 absolute ignores the whole part", [(RC, "if (absolute) top = Math.Max(top, n.BitLength - d.BitLength);", "if (false) top = Math.Max(top, n.BitLength - d.BitLength);")]),
    ("M8 widths not doubling", [(RC, "width = Math.Min(checked(width * 2), bound)", "width = Math.Min(checked(width + 1), bound)")]),
    ("M9 the unsettled hook ignored", [(RC, "return unsettled is not null ? unsettled() : f(", "return f(")]),
    ("M10 corners disagreeing accepted", [(RC, "if (erred || (agreed is not null && !Same(agreed, result)))", "if (erred)")]),
    # Mixed operators
    ("M11 a mixed product rounding first", [(EA, "        return _floats.Quotient(Times(n, y), d, at);\n", "        return _floats.Multiply(a, b, at);\n")]),
    ("M12 a mixed quotient upside down", [(EA, "return _floats.Quotient(n, Times(d, y), at);", "return _floats.Quotient(Times(d, y), n, at);")]),
    ("M13 a mixed sum rounding first", [(EA, "        if (Fraction(a) is not (ApInt n1, ApInt d1) || Fraction(b) is not (ApInt n2, ApInt d2))\n            return RoundedCall.Of(_floats, [a, b], Floats);",
                                         "        if (Fraction(a) is not (ApInt n1, ApInt d1) || Fraction(b) is not (ApInt n2, ApInt d2))\n            return Floats([a, b]);")]),
    ("M14 Times rounding", [(EA, "long width = n.IsZero || !y.IsFinite || y.IsZero ? 1 : n.BitLength + y.Significand.BitLength;", "long width = n.IsZero || !y.IsFinite || y.IsZero ? 1 : n.BitLength;")]),
    # Past a budget
    ("M15 a sum past the ratio budget rounding first", [(EA, "if (WithinWork(SumBits(n1, d1, n2, d2), d1.BitLength + d2.BitLength)) return TooLarge(Unreduced());",
                                                        "if (WithinWork(SumBits(n1, d1, n2, d2), d1.BitLength + d2.BitLength)) return TooLarge(Floats([a, b]));")]),
    ("M16 a product past the budget rounding first", [(EA, "                return TooLarge(Unreduced());\n            return TooLarge(RoundedCall.Of(_floats, [a, b], v => _floats.Multiply(",
                                                       "                return TooLarge(_floats.Multiply(a, b, at));\n            return TooLarge(RoundedCall.Of(_floats, [a, b], v => _floats.Multiply(")]),
    ("M17 the chip dropped past the work budget", [(EA, "return TooLarge(RoundedCall.Of(_floats, [a, b], v => _floats.Divide(v[0], v[1], at), absolute: false, unsettled: Unreduced));",
                                                    "return RoundedCall.Of(_floats, [a, b], v => _floats.Divide(v[0], v[1], at), absolute: false, unsettled: Unreduced);")]),
    ("M18 the remainder's jump not settled exactly", [(EA, "RoundedCall.Of(_floats, [a, b], Floats, unsettled: Exactly);", "RoundedCall.Of(_floats, [a, b], Floats);")]),
    # mod, rem
    ("M19 mod truncated", [(EA, "if (floored && !r.IsZero && r.IsNegative != divisor.IsNegative) r += divisor;", "if (false) r += divisor;")]),
    ("M20 a float in mod not taken exactly", [(EA, "if (Fraction(AsExact(a)) is not (ApInt n1, ApInt d1) || Fraction(AsExact(b)) is not (ApInt n2, ApInt d2))\n            return RoundedCall.Of(_floats, [a, b], Floats);\n        if (n2.IsZero)",
                                               "if (Fraction(a) is not (ApInt n1, ApInt d1) || Fraction(b) is not (ApInt n2, ApInt d2))\n            return RoundedCall.Of(_floats, [a, b], Floats);\n        if (n2.IsZero)")]),
    ("M21 a mixed remainder left exact", [(EA, "            return mixed ? _floats.Rounded(result, at) : result;\n        }", "            return result;\n        }")]),
    ("M22 FloatArithmetic.Mod rounding twice", [(FA, "r = _ieee.Add(x % y, y);", "r = _ieee.Add(_ieee.Remainder(x, y), y);")]),
    # Functions and comparisons
    ("M23 a tie not going to the exact one", [(EA, "if (c == want || (c == 0 && best is FloatValue && arguments[i] is not FloatValue)) best = arguments[i];", "if (c == want) best = arguments[i];")]),
    ("M24 a mixed comparison's sign not flipped", [(EA, "return flipped ? -c : c;", "return c;")]),
    ("M25 the size gap deciding at 1", [(EF, "int magnitude = gap >= 2 ? 1", "int magnitude = gap >= 1 ? 1")]),
    ("M26 round's ties away", [(EF, "bool away = half > 0 || (half == 0 && !(q & ApInt.One).IsZero);", "bool away = half >= 0;")]),
    ("M27 ceil on negatives", [(EF, "return !r.IsZero && !r.IsNegative ? q + ApInt.One : q;", "return !r.IsZero ? q + ApInt.One : q;")]),
    # sqrt and ^
    ("M28 an odd count of twos not refused", [(EA, "        if ((twos & 1) != 0) return (null, true);\n", "")]),
    ("M29 an irrational root chipped too large", [(EA, "bool irrational = (topKnown && top is null) || (bottomKnown && bottom is null);", "bool irrational = false;")]),
    ("M30 ^(1/2) not sqrt", [(EA, "        if (b is RatioValue half && half.Numerator == ApInt.One && half.Denominator == 2) return Sqrt(a, at);\n", "")]),
    ("M31 an exact base rounded first", [(EA, "return RoundedCall.Of(_floats, [a], v => _floats.Power(v[0], b, at), absolute: false, extraBits: exponentBits);", "return _floats.Power(a, b, at);")]),
    # Bit operators
    ("M32 an integer rounded first for a bit operator", [(EA, "        IntValue i => new FloatValue(i.Number),\n", "        IntValue i => i,\n")]),
    ("M33 a left shift with no size budget", [(EA, "if (places.BitLength < 63 && x.BitLength + long.CreateChecked(places) <= _budgets.IntegerBits)", "if (places.BitLength < 31)")]),
    ("M34 a negative shifted right away gives 0", [(EA, "return new IntValue(x.IsNegative ? ApInt.MinusOne : ApInt.Zero);", "return new IntValue(ApInt.Zero);")]),
    # Display
    ("M35 a fraction's digits rounding ties up", [(RT, "            if (half > 0 || (half == 0 && !(q & ApInt.One).IsZero))\n            {\n                q += ApInt.One;",
                                                   "            if (half >= 0)\n            {\n                q += ApInt.One;")]),
    ("M36 the G layout's switch at -5", [(RT, "if (x < -5 || x >= k) return sign + Point(digits, 1) + ExponentText(x);", "if (x <= -5 || x >= k) return sign + Point(digits, 1) + ExponentText(x);")]),
    ("M37 the n/d chip one too wide", [(RT, "r.ToString() is { Length: <= FractionWidth } fraction", "r.ToString() is { Length: <= FractionWidth + 1 } fraction")]),
    ("M38 a terminating fraction's caption", [(RT, "string caption = chip != \"exact\" ? chip : exactly ? \"exact\" : ", "string caption = chip != \"exact\" ? chip : false ? \"exact\" : ")]),
]


def apply(text, old, new):
    for o, n in ((old, new), (old.replace("\n", "\r\n"), new.replace("\n", "\r\n"))):
        if text.count(o) == 1:
            return text.replace(o, n)
    raise ValueError(f"not found exactly once: {old[:70]!r} (count {text.count(old)})")


def run(name, edits):
    originals = {}
    try:
        for path, old, new in edits:
            full = ROOT + "\\" + path
            if full not in originals:
                originals[full] = open(full, "rb").read()
            text = open(full, "rb").read().decode("utf-8")
            text = apply(text, old, new)
            open(full, "wb").write(text.encode("utf-8"))
        t0 = time.time()
        try:
            p = subprocess.run(["dotnet", "test", r"tests\Calc.Engine.Tests", "--nologo", "--filter",
                                "FullyQualifiedName~Exact|FullyQualifiedName~RoundedCall|FullyQualifiedName~FunctionTests",
                                "--logger", "console;verbosity=normal"],
                               cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300)
        except subprocess.TimeoutExpired:
            return f"TIMEOUT    {name}"
        out = p.stdout + p.stderr
        dt = time.time() - t0
        if "error CS" in out:
            err = re.search(r"error CS\d+: [^\[]*", out)
            return f"BUILD ERR  {name}: {err.group(0) if err else ''}"
        if "Test Run Aborted" in out:
            return f"ABORTED    {name} ({dt:.0f} s): the test host crashed"
        failed = sorted(set(re.findall(r"Failed Calc\.Engine\.Tests\.(\S+)", out)))
        if failed or p.returncode != 0:
            return f"RED        {name} ({dt:.0f} s, {len(failed)} failed): {', '.join(f.split('.')[-1][:60] for f in failed[:3])}"
        return f"green      {name} ({dt:.0f} s)"
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

if sys.platform == "win32":
    ctypes.windll.kernel32.SetErrorMode(0x0001 | 0x0002)  # no crash dialog, should a test host die anyway

first, count = int(sys.argv[1]), int(sys.argv[2])
for name, edits in M[first:first + count]:
    try:
        print(run(name, edits), flush=True)
    except ValueError as e:
        print(f"BAD MUT    {name}: {e}", flush=True)
print(f"done {first}..{first + count - 1} of {len(M)}", flush=True)
