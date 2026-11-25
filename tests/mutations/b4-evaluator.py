"""Calc's B4 mutation battery on the evaluator and float core, by ../Natural/CLAUDE.md's recipe.

usage: python tests/mutations/b4-evaluator.py <first> <count> | check
Each mutation's old text must match exactly once. It's applied, the Engine tests run, and the file is restored in
`finally`. 27 were predicted red and M9 equivalent: at bits only a division raises DivideByZero, and division checks for
a zero divisor first. None touches a stack guard, so none can overflow the stack and crash the test host, which on
Windows also pops up a dialog. Don't build Calc while it runs.
"""
import ctypes, re, subprocess, sys, time
from pathlib import Path

ROOT = str(Path(__file__).resolve().parents[2])
sys.stdout.reconfigure(encoding="utf-8")  # the names hold − × ÷, which Windows' default code page can't print
FA =r"src\Calc.Engine\Eval\FloatArithmetic.cs"
EV = r"src\Calc.Engine\Eval\Evaluator.cs"
NM = r"src\Calc.Engine\Eval\Names.cs"
EM = r"src\Calc.Engine\Eval\ErrorMap.cs"
TG = r"src\Calc.Engine\Values\Target.cs"

M = [
    # An operator used instead of the context
    ("M1 + by ApFloat's operator", [(FA, "return Checked(_ieee.Add(F(a), F(b)), before, at);", "return Checked(F(a) + F(b), before, at);")]),
    ("M2 × by ApFloat's operator", [(FA, "return Checked(_ieee.Multiply(F(a), F(b)), before, at);", "return Checked(F(a) * F(b), before, at);")]),
    ("M3 ÷ by the static method, mode lost", [(FA, "return Checked(_ieee.Divide(F(a), y), before, at);", "return Checked(ApFloat.Divide(F(a), y, _ieee.Precision), before, at);")]),
    ("M4 − adds", [(FA, "return Checked(_ieee.Subtract(F(a), F(b)), before, at);", "return Checked(_ieee.Add(F(a), F(b)), before, at);")]),
    ("M5 negate as 0 − x", [(FA, "return Checked(_ieee.Convert(-F(a)), before, at);", "return Checked(_ieee.Subtract(0, F(a)), before, at);")]),
    # A mode or target ignored
    ("M6 the mode ignored", [(FA, "private readonly IeeeContext _ieee = target.NewContext(mode);", "private readonly IeeeContext _ieee = target.NewContext(RoundingMode.ToNearestEven);")]),
    ("M7 a format's range ignored", [(TG, "Format is IeeeFormat format ? new IeeeContext(format, mode) : new IeeeContext(_bits, mode);", "new IeeeContext(_bits, mode);")]),
    ("M8 Mathematical at a format too", [(FA, "private readonly bool _mathematical = !target.IsFormat;", "private readonly bool _mathematical = true;")]),
    # A flag not checked
    ("M9 DivideByZero flag unchecked (predicted equivalent)", [(FA, "if ((raised & IeeeFlags.DivideByZero) != 0) throw new CalcError(ErrorKind.DivideByZero, \"Division by zero.\", at);", "")]),
    ("M10 Invalid flag unchecked", [(FA, "if ((raised & IeeeFlags.Invalid) != 0) throw new CalcError(ErrorKind.Domain, \"That has no answer.\", at);", "")]),
    ("M11 no zero-divisor check", [(FA, "if (_mathematical && y.IsZero) throw new CalcError(ErrorKind.DivideByZero, \"Division by zero.\", at);", "")]),
    # The wrong error kind
    ("M12 ÷0 is Domain", [(FA, "if (_mathematical && y.IsZero) throw new CalcError(ErrorKind.DivideByZero,", "if (_mathematical && y.IsZero) throw new CalcError(ErrorKind.Domain,")]),
    ("M13 Invalid is Overflow", [(FA, "throw new CalcError(ErrorKind.Domain, \"That has no answer.\", at);", "throw new CalcError(ErrorKind.Overflow, \"That has no answer.\", at);")]),
    ("M14 ErrorMap: DivideByZeroException is Domain", [(EM, "DivideByZeroException => new(ErrorKind.DivideByZero,", "DivideByZeroException => new(ErrorKind.Domain,")]),
    ("M15 ErrorMap: OverflowException is TooBig", [(EM, "OverflowException => new(ErrorKind.Overflow,", "OverflowException => new(ErrorKind.TooBig,")]),
    ("M16 an operator's error loses its span", [(EV, "throw ErrorMap.From(e, b.OpSpan);", "throw ErrorMap.From(e, null);")]),
    # A literal parsed then converted, or misread
    ("M17 decimal parsed at 256 bits, then converted", [(FA, "x = _ieee.Parse(s, CultureInfo.InvariantCulture);", "x = _ieee.Convert(ApFloat.Parse(s, CultureInfo.InvariantCulture));")]),
    ("M18 whole number parsed as a float, then converted", [(FA, "x = _ieee.Convert(ApInt.Parse(s, CultureInfo.InvariantCulture));", "x = _ieee.Convert(ApFloat.Parse(s, CultureInfo.InvariantCulture));")]),
    ("M19 underscores kept", [(FA, "string s = text.Replace(\"_\", \"\");", "string s = text;")]),
    ("M20 octal read as decimal", [(FA, "n = n * 8 + (c - '0');", "n = n * 10 + (c - '0');")]),
    ("M21 1e5 taken for a whole number", [(FA, ": s.AsSpan().IndexOfAny(\".eE\") < 0;", ": s.AsSpan().IndexOfAny(\".\") < 0;")]),
    # Evaluation order and operands
    ("M22 right evaluated before left", [(EV, "Value left = Evaluate(b.Left);\n                return Binary(b, left, Evaluate(b.Right));",
                                           "Value right = Evaluate(b.Right);\n                return Binary(b, Evaluate(b.Left), right);")]),
    ("M23 − with operands swapped", [(EV, "BinaryOp.Subtract => arithmetic.Subtract(left, right, b.OpSpan),", "BinaryOp.Subtract => arithmetic.Subtract(right, left, b.OpSpan),")]),
    ("M24 unary + negates", [(EV, "PrefixOp.Plus => operand,", "PrefixOp.Plus => arithmetic.Negate(operand, p.OpSpan),")]),
    ("M25 to evaluates its operands first", [(EV, "            case BinaryNode { Op: BinaryOp.To } b:\n                // Its right side names a format or unit (F1, H10), so it's refused before either side is evaluated.\n                throw NotYet(b);\n", "")]),
    # Names, Ans and compare
    ("M26 an Ans from another target let in", [(FA, "if (x.Precision != _ieee.Precision)", "if (false)")]),
    ("M27 π an unknown name", [(NM, "[\"π\", \"pi\",", "[\"pi\",")]),
    ("M28 NaN on the right compares", [(FA, "return x.IsNaN || y.IsNaN ? null : Math.Sign(x.CompareTo(y));", "return x.IsNaN ? null : Math.Sign(x.CompareTo(y));")]),
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

# Should a test host crash anyway, ask Windows not to show its error dialog: children inherit this.
if sys.platform == "win32":
    ctypes.windll.kernel32.SetErrorMode(0x0001 | 0x0002)  # SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX

first, count = int(sys.argv[1]), int(sys.argv[2])
for name, edits in M[first:first + count]:
    try:
        print(run(name, edits), flush=True)
    except ValueError as e:
        print(f"BAD MUT    {name}: {e}", flush=True)
print(f"done {first}..{first + count - 1} of {len(M)}", flush=True)
