"""Calc's B5 mutation battery on functions, powers, mod, rem, % and the bit operators, by ../Natural/CLAUDE.md's recipe.

usage: python tests/mutations/b5-functions.py <first> <count> | check
Each mutation's old text must match exactly once. It's applied, the Engine tests run, and the file is restored in
`finally`. 29 were predicted red and 2 equivalent. First run: M2's edit didn't build (fixed), and M21, the guard as a
product, survived until `1.1^(2^62)` at binary64 was added; now 29 red. The equivalents: M28, both bounds rounded the same way, which only a true value within
2^-64 of a rounding boundary would show; and M29, Settle's extra Inexact, which converting differing ends raises anyway.
None touches a stack guard. Don't build Calc while it runs.
"""
import ctypes, re, subprocess, sys, time
from pathlib import Path

ROOT = str(Path(__file__).resolve().parents[2])
sys.stdout.reconfigure(encoding="utf-8")
FA = r"src\Calc.Engine\Eval\FloatArithmetic.cs"
PF = r"src\Calc.Engine\Eval\PowerFloat.cs"
FT = r"src\Calc.Engine\Eval\FunctionTable.cs"

M = [
    # mod, rem, %
    ("M1 floored mod as truncated", [(FA, "        else if (!r.IsNaN && r.IsNegative != y.IsNegative) r = _ieee.Add(r, y);\n", "")]),
    ("M2 mod's zero not given the divisor's sign", [(FA, "        if (r.IsZero) r = ApFloat.CopySign(r, y);\n        else if (!r.IsNaN", "        if (!r.IsNaN")]),
    ("M3 rem by zero not refused at bits", [(FA, "ApFloat y = F(b);\n        if (_mathematical && y.IsZero) throw DivisionByZero(at);\n        var before = _ieee.Flags;\n        return Checked(_ieee.Remainder(F(a), y), before, at);",
                                              "ApFloat y = F(b);\n        var before = _ieee.Flags;\n        return Checked(_ieee.Remainder(F(a), y), before, at);")]),
    ("M4 % divides by 10", [(FA, "return Checked(_ieee.Divide(F(a), 100), before, at);", "return Checked(_ieee.Divide(F(a), 10), before, at);")]),
    # The functions
    ("M5 fma as two operations", [(FA, "\"fma\" => _ieee.FusedMultiplyAdd(F(arguments[0]), F(arguments[1]), F(arguments[2])),", "\"fma\" => _ieee.Add(_ieee.Multiply(F(arguments[0]), F(arguments[1])), F(arguments[2])),")]),
    ("M6 sign backwards", [(FA, "x.IsNegative ? -1 : 1", "x.IsNegative ? 1 : -1")]),
    ("M7 max is min", [(FA, "\"max\" => _ieee.Convert(Fold(arguments, ApFloat.Max)),", "\"max\" => _ieee.Convert(Fold(arguments, ApFloat.Min)),")]),
    ("M8 round's ties away from zero", [(FA, "\"round\" => _ieee.Convert(ApFloat.Round(F(arguments[0]))),", "\"round\" => _ieee.Convert(ApFloat.Round(F(arguments[0]), MidpointRounding.AwayFromZero)),")]),
    ("M9 fma takes two arguments", [(FT, "new(\"fma\", 3, 3)", "new(\"fma\", 2, 3)")]),
    ("M10 exp marked available", [(FT, "new(\"exp\", 1, 1, \"W1\")", "new(\"exp\", 1, 1)")]),
    # The bit operators
    ("M11 no size cap", [(FA, "if (!x.IsZero && x.Exponent + x.Significand.BitLength > BitLimit)", "if (false)")]),
    ("M12 fractions taken as whole", [(FA, "private static bool IsWhole(ApFloat x) => !x.IsNaN && !x.IsInfinity && ApFloat.IsInteger(x);", "private static bool IsWhole(ApFloat x) => !x.IsNaN && !x.IsInfinity;")]),
    ("M13 a shift doesn't floor", [(FA, "return Checked(_ieee.Convert(ApFloat.Floor(scaled)), before, at);", "return Checked(_ieee.Convert(scaled), before, at);")]),
    ("M14 >> shifts left", [(FA, "left ? n : checked(-n)", "left ? n : n")]),
    ("M15 | is &", [(FA, "FromWhole(Whole(a, at) | Whole(b, at), at);", "FromWhole(Whole(a, at) & Whole(b, at), at);")]),
    # Powers: sign and parity
    ("M16 sign restored wrongly", [(PF, "bool negative = x.IsNegative && (n & 1) != 0;", "bool negative = x.IsNegative;")]),
    ("M17 parity wrong in the specials", [(PF, "bool odd = integer && y.Exponent == 0;", "bool odd = integer && y.Exponent == 1;")]),
    ("M18 (-1)^y ignores parity", [(PF, "if (IsExactly(x, -1)) return ieee.Convert(odd ? -1 : 1);", "if (IsExactly(x, -1)) return ieee.Convert(1);")]),
    ("M19 x^inf backwards", [(PF, "bool huge = (ApFloat.Abs(x).CompareTo(1) > 0) == !y.IsNegative;", "bool huge = (ApFloat.Abs(x).CompareTo(1) > 0) == y.IsNegative;")]),
    # Powers: the route guard and checked
    ("M20 exact route always", [(PF, "if (k <= (ulong)(limit / m.BitLength))", "if (true)")]),
    ("M21 the guard as a product", [(PF, "if (k <= (ulong)(limit / m.BitLength))", "if (k * (ulong)m.BitLength <= (ulong)limit)")]),
    ("M22 checked dropped", [(PF, "checked(magnitude.Exponent * (long)k)", "unchecked(magnitude.Exponent * (long)k)")]),
    # Powers: flags
    ("M23 0^-n without DivideByZero", [(PF, "if (y.IsNegative) return ieee.Divide(1, odd ? x : ApFloat.Zero);", "if (y.IsNegative) return ieee.Convert(odd && x.IsNegative ? ApFloat.NegativeInfinity : ApFloat.PositiveInfinity);")]),
    ("M24 negative ^ fraction without Invalid", [(PF, "                ieee.Flags |= IeeeFlags.Invalid;\n", "")]),
    # Powers: the format's edges
    ("M25 no early exit when growing", [(PF, "if (growing && top > overflowTop) { finalHuge = !inverse; return null; }", "")]),
    ("M26 the inverse forgotten at the edge", [(PF, "finalHuge = !inverse;", "finalHuge = true;")]),
    ("M27 the stand-in inside the format", [(PF, "new(1, huge ? Emax(format) + 2 : SmallestSubnormal(format) - 3, 1);", "new(1, huge ? Emax(format) - 2 : SmallestSubnormal(format) - 3, 1);")]),
    ("M28 both bounds rounded up (predicted equivalent)", [(PF, "ApFloat? down = Raise(magnitude, k, w, RoundingMode.TowardNegative,", "ApFloat? down = Raise(magnitude, k, w, RoundingMode.TowardPositive,")]),
    ("M29 Settle's Inexact dropped (predicted equivalent)", [(PF, "ieee.Flags = lowFlags | highFlags | (low.CompareTo(high) == 0 ? IeeeFlags.None : IeeeFlags.Inexact);", "ieee.Flags = lowFlags | highFlags;")]),
    # Powers: y past a long, and y = 1/2
    ("M30 the near-1 band too wide", [(PF, "if (magnitude.CompareTo(2) < 0 && magnitude.CompareTo(0.5) > 0)", "if (magnitude.CompareTo(3) < 0 && magnitude.CompareTo(0.5) > 0)")]),
    ("M31 ^0.5 not sqrt", [(PF, "if (y.CompareTo(0.5) == 0) return ieee.Sqrt(x);", "")]),
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
            p = subprocess.run(["dotnet", "test", r"tests\Calc.Engine.Tests", "--nologo", "--logger", "console;verbosity=normal"],
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
