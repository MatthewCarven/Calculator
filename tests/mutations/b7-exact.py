"""Calc's B7 mutation battery on exact values, their budgets and the lossless form, by ../Natural/CLAUDE.md's recipe.

usage: python tests/mutations/b7-exact.py <first> <count> | check
Each mutation's old text must match exactly once. It's applied, the Exact and Lossless tests run, and the file is
restored in `finally`. All 22 were predicted red. 21 were; M3 timed out instead, which counts as caught: computing before
checking really tries to build 7^(10^9), 2.8 billion bits. Don't build Calc while it runs.
"""
import ctypes, re, subprocess, sys, time
from pathlib import Path

ROOT = str(Path(__file__).resolve().parents[2])
sys.stdout.reconfigure(encoding="utf-8")
RV = r"src\Calc.Engine\Values\RatioValue.cs"
EA = r"src\Calc.Engine\Eval\ExactArithmetic.cs"
FA = r"src\Calc.Engine\Eval\FloatArithmetic.cs"
EV = r"src\Calc.Engine\Eval\Evaluator.cs"
LL = r"src\Calc.Engine\Text\Lossless.cs"

M = [
    # The brief's six
    ("M1 gcd skipped", [(RV, "if (!g.IsZero && g != ApInt.One)", "if (false)")]),
    ("M2 sign left on d", [(RV, "        if (denominator.IsNegative)\n        {\n            numerator = -numerator;\n            denominator = -denominator;\n        }\n", "")]),
    ("M3 budget checked after computing", [(EA, "if (numeratorBits > _budgets.IntegerBits || !WithinWork(numeratorBits / 2 + 1, numeratorBits / 2 + 1)) return null;",
                                            "{ _ = ApInt.Pow(magnitude, k); if (numeratorBits > _budgets.IntegerBits || !WithinWork(numeratorBits / 2 + 1, numeratorBits / 2 + 1)) return null; }")]),
    ("M4 the chip not set on fallback", [(EA, "floatResult is FloatValue f ? f with { ExactTooLarge = true } : floatResult;", "floatResult;")]),
    ("M5 d = 1 not folded", [(RV, "return denominator == ApInt.One ? new IntValue(numerator) : new RatioValue(numerator, denominator);", "return new RatioValue(numerator, denominator);")]),
    ("M6 a lossless tag crossed", [(LL, "IntValue i => \"i:\" + Signed(i.Number),", "IntValue i => \"r:\" + Signed(i.Number),")]),
    # Lossless
    ("M7 two's-complement hex (rule 6)", [(LL, "string hex = ApInt.Abs(n).ToString(\"X\", Invariant).TrimStart('0');", "string hex = n.ToString(\"X\", Invariant).TrimStart('0');")]),
    ("M8 a ratio not in lowest terms read", [(LL, "if (ratio is not RatioValue r || r.Numerator != numerator || r.Denominator != denominator) throw Bad(text);", "")]),
    # The operations
    ("M9 no DivRem first", [(EA, "                if (remainder.IsZero) return new IntValue(quotient);\n", "")]),
    ("M10 ratio subtraction adds", [(EA, "negateB ? -cross : cross", "cross")]),
    ("M11 no size budget on a sum", [(EA, "if (Math.Max(x.Number.BitLength, y.Number.BitLength) + 1 <= _budgets.IntegerBits)", "if (true)")]),
    ("M12 no work budget on a product", [(EA, "if (bitsX + bitsY <= _budgets.IntegerBits && WithinWork(bitsX, bitsY))", "if (bitsX + bitsY <= _budgets.IntegerBits)")]),
    ("M13 no ratio denominator budget", [(EA, "numeratorBits <= _budgets.RatioBits && denominatorBits <= _budgets.RatioBits", "numeratorBits <= _budgets.RatioBits")]),
    # Powers
    ("M14 powers of two not exempt", [(EA, "if (IsPowerOfTwo(magnitude))", "if (false)")]),
    ("M15 0, 1 and -1 not settled first", [(EA, "if (d == ApInt.One && ApInt.Abs(n) == ApInt.One) return new IntValue(n.IsNegative && odd ? ApInt.MinusOne : ApInt.One);", "")]),
    ("M16 the estimate as bitlen(m)·k", [(EA, "double log2 = Math.Log2(ulong.CreateChecked(magnitude >> drop)) + drop;", "double log2 = magnitude.BitLength;")]),
    ("M17 the float path's exponent rounded", [(FA, "        IntValue i => i.Number,\n", "        IntValue i => F(b),\n")]),
    # Literals
    ("M18 a decimal's scale one too many", [(EA, "return RatioValue.Of(d, ApInt.Pow(10, (int)-scale));", "return RatioValue.Of(d, ApInt.Pow(10, (int)-scale + 1));")]),
    ("M19 a hex fraction's shift in digits", [(EA, "shift = checked(exponent - fractionDigits * bitsPerDigit);", "shift = checked(exponent - fractionDigits);")]),
    ("M20 a literal's fallback unchipped", [(EA, "?? TooLarge(_floats.Literal(text, at));", "?? _floats.Literal(text, at);")]),
    # Exact off, and formats
    ("M21 an exact Ans not converted with Exact off", [(FA, "if (value is not FloatValue) return new FloatValue(F(value));", "if (value is not FloatValue) return value;")]),
    ("M22 exact at a format", [(EV, "exact && !target.IsFormat ?", "exact ?")]),
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
            p = subprocess.run(["dotnet", "test", r"tests\Calc.Engine.Tests", "--nologo", "--filter", "FullyQualifiedName~ExactTests|FullyQualifiedName~LosslessTests",
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
