"""Calc's B6 mutation battery on result text and Digits, by ../Natural/CLAUDE.md's recipe.

usage: python tests/mutations/b6-text.py <first> <count> | check
Each mutation's old text must match exactly once. It's applied, the Engine's Text tests run (the only ones that read
this code), and the file is restored in `finally`. All 19 were predicted red. 18 were, first time; M7 survived because
straight E99 at 2^20 bits is about 100 ms, inside the 200 ms budget, until ThePreviewStaysQuickAt2To22Bits came; now all
19 are. Don't build Calc while it runs.
"""
import ctypes, re, subprocess, sys, time
from pathlib import Path

ROOT = str(Path(__file__).resolve().parents[2])
sys.stdout.reconfigure(encoding="utf-8")
DG = r"src\Calc.Protocol\Digits.cs"
RT = r"src\Calc.Engine\Text\ResultText.cs"

M = [
    # Digits
    ("M1 a 32-bit log10(2)", [(DG, "0x4D104D427DE7FBCCUL", "0x4D104D4200000000UL")]),
    ("M2 For can be 0", [(DG, "return Math.Max(1, (int)Math.BigMul((ulong)(bits - 1), Log10Of2, out _));", "return (int)Math.BigMul((ulong)(bits - 1), Log10Of2, out _);")]),
    ("M3 BitsFor one bit too many", [(DG, "Math.Ceiling(digits * 3.321928094887362) + 1)", "Math.Ceiling(digits * 3.321928094887362) + 2)"),
                                      (DG, "        while (bits > 1 && For(bits - 1) >= digits) bits--;\n", "")]),
    # Signs, words, limits
    ("M4 -0 not folded at bits", [(RT, "if (x.IsZero && x.IsNegative && !target.IsFormat) x = ApFloat.Abs(x);", "")]),
    ("M5 -0 folded at a format too", [(RT, "if (x.IsZero && x.IsNegative && !target.IsFormat)", "if (x.IsZero && x.IsNegative)")]),
    ("M6 the G limit at 1,200 digits", [(RT, "public const int MainLimit = 1300;", "public const int MainLimit = 1200;")]),
    ("M7 the preview not rounded to 1,024 bits first", [(RT, "ApFloat preview = x.WithPrecision(PreviewBits);", "ApFloat preview = x;")]),
    ("M8 Short past 39 characters", [(RT, "if (text.Length <= ShortWidth || k == 1) return text;", "return text;")]),
    ("M9 Main at bits by R", [(RT, "? x.ToString(\"G\" + digits, Invariant)", "? x.ToString(\"R\", Invariant)")]),
    # Formats
    ("M10 subnormals by R", [(RT, "string shortest = IsSubnormal(x, format) ? ShortestSubnormal(x, format) : x.ToString(\"R\", Invariant);", "string shortest = x.ToString(\"R\", Invariant);")]),
    ("M11 a format by G<d>", [(RT, "string shortest = IsSubnormal(x, format) ? ShortestSubnormal(x, format) : x.ToString(\"R\", Invariant);", "string shortest = IsSubnormal(x, format) ? ShortestSubnormal(x, format) : x.ToString(\"G\" + digits, Invariant);")]),
    ("M12 a ToString overflow unmapped", [(RT, "            throw ErrorMap.From(e, null);", "            throw;")]),
    # Notations
    ("M13 engineering truncates a negative exponent", [(RT, "e >= 0 ? e - e % 3 : -((-e + 2) / 3 * 3)", "e - e % 3")]),
    ("M14 aligned drops its zeros", [(RT, "notation == Notation.Aligned ? parts.Digits : TrimZeros(parts.Digits)", "TrimZeros(parts.Digits)")]),
    ("M15 fixed gives up at 50 places", [(RT, "if (places > FixedLimit)", "if (places > 50)")]),
    ("M16 the exponent off by one", [(RT, "checked(exponent + whole.Length - 1 - leadingZeros)", "checked(exponent + whole.Length - leadingZeros)")]),
    ("M17 two exponent digits", [(RT, "Math.Abs(exponent).ToString(\"000\", Invariant)", "Math.Abs(exponent).ToString(\"00\", Invariant)")]),
    # Grouping
    ("M18 groups counted from the left", [(RT, "if (i > 0 && (whole.Length - i) % 3 == 0)", "if (i > 0 && i % 3 == 0)")]),
    ("M19 the fraction always grouped", [(RT, "if (options.GroupFraction && i > 0 && i % 3 == 0)", "if (i > 0 && i % 3 == 0)")]),
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
            p = subprocess.run(["dotnet", "test", r"tests\Calc.Engine.Tests", "--nologo", "--filter", "FullyQualifiedName~Engine.Tests.Text",
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
