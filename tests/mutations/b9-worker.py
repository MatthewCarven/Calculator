"""Calc's B9 mutation battery on the worker's loop, the wire messages and the context mapping, by ../Natural/CLAUDE.md's recipe.

usage: python tests/mutations/b9-worker.py <first> <count> | check
Each mutation's old text must match exactly once. It's applied, the worker's tests run (and, for an Engine file, the
Contexts tests too), and the file is restored in `finally`. None of these crashes the worker: a crash could pop a dialog
on Matthew's screen. All 25 were predicted red, and all 25 were (2026-11-29). Don't build Calc while it runs.
"""
import ctypes, re, subprocess, sys, time
from pathlib import Path

ROOT = str(Path(__file__).resolve().parents[2])
sys.stdout.reconfigure(encoding="utf-8")
LP = r"src\Calc.Worker\Loop.cs"
PG = r"src\Calc.Worker\Program.cs"
MS = r"src\Calc.Protocol\Messages.cs"
CX = r"src\Calc.Engine\Eval\Contexts.cs"
EV = r"src\Calc.Engine\Eval\Evaluator.cs"

M = [
    # The reader
    ("M1 ping queued, not answered", [(LP, "                case PingRequest ping:\n                    Send(new PongReply(ping.Id, Wire.Version));\n                    break;\n", "")]),
    ("M2 stop ignored while running", [(LP, "if (_runningId == id) _runningStop?.Cancel();", "if (false) _runningStop?.Cancel();")]),
    ("M3 stop ignored while queued", [(LP, "            else if (_queued.ContainsKey(id)) _queued[id] = true;\n", "")]),
    ("M4 a stopped queued evaluation runs", [(LP, "if (stopped) return Error(request.Id, new CalcError(ErrorKind.Stopped, \"Stopped.\", null));", "if (false) return Error(request.Id, new CalcError(ErrorKind.Stopped, \"Stopped.\", null));")]),
    ("M5 a malformed line's id not read", [(LP, "Send(new ErrorReply(IdIn(line), ErrorKinds.BadRequest,", "Send(new ErrorReply(null, ErrorKinds.BadRequest,")]),
    ("M6 an error's span dropped", [(LP, "new(id, error.Kind.ToString(), error.Message, error.Span?.Start, error.Span?.End);", "new(id, error.Kind.ToString(), error.Message, null, null);")]),
    ("M7 stdin's end doesn't end it", [(LP, "IsBackground = true, Priority = ThreadPriority.BelowNormal", "IsBackground = false, Priority = ThreadPriority.BelowNormal"),
                                         (PG, "    Environment.Exit(0);\n}", "}")]),
    # Ans and held values
    ("M8 a float Ans from another target accepted", [(LP, "if (value is FloatValue && from != target)", "if (false)")]),
    ("M9 a held Ans looked up wrongly", [(LP, "if (_held.Get(heldId) is not var (heldValue, heldTarget))", "if (_held.Get(heldId + 1) is not var (heldValue, heldTarget))")]),
    ("M10 the lossless limit four times too high", [(LP, "EstimatedLength(outcome.Value) <= Wire.LosslessLimit", "EstimatedLength(outcome.Value) <= 4 * Wire.LosslessLimit"),
                                                    (LP, "lossless.Length > Wire.LosslessLimit", "lossless.Length > 4 * Wire.LosslessLimit")]),
    ("M11 nothing ever evicted", [(LP, "while (_bytes > limit && _order.Count > 1)", "while (false)")]),
    ("M12 half the heap cap, not a quarter", [(LP, "long limit = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 4;", "long limit = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 2;")]),
    ("M13 the held id misreported", [(LP, "                held = request.Id;\n", "                held = request.Id + 1;\n")]),
    # The result
    ("M14 understood dropped", [(LP, "string? understood = Printer.Understood(tree);", "string? understood = null;")]),
    ("M15 flags dropped", [(LP, "Contexts.FlagsOf(outcome.Flags), clock.Elapsed.TotalMilliseconds", "[], clock.Elapsed.TotalMilliseconds")]),
    ("M16 Exact off ignored", [(LP, "ans, request.Context.Exact, budgets, stop.Token", "ans, true, budgets, stop.Token")]),
    ("M17 the text style ignored", [(LP, "options = Contexts.OptionsOf(request.Options);", "options = TextOptions.Default;")]),
    # The wire
    ("M18 required parameters not required", [(MS, "    RespectRequiredConstructorParameters = true)]", "    RespectRequiredConstructorParameters = false)]")]),
    ("M19 a null error id left out", [(MS, "[property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Id", "long? Id")]),
    ("M20 the type tag must come first", [(MS, "    AllowOutOfOrderMetadataProperties = true,\n", "")]),
    # The mapping, and the soft stop
    ("M21 up and down swapped", [(CX, "        Rounding.Up => RoundingMode.TowardPositive,\n        Rounding.Down => RoundingMode.TowardNegative,",
                                  "        Rounding.Up => RoundingMode.TowardNegative,\n        Rounding.Down => RoundingMode.TowardPositive,")]),
    ("M22 a format mapped to bits", [(CX, "target.ExponentBits is int exponentBits ? Target.Of(new IeeeFormat(exponentBits, target.Precision)) : Target.Bits(target.Precision);",
                                      "Target.Bits(target.Precision);")]),
    ("M23 budgets half mapped", [(CX, "new ExactBudgets(budgets.IntegerBits, budgets.RatioBits, budgets.MultiplyWork);", "new ExactBudgets(budgets.IntegerBits);")]),
    ("M24 the stop never checked", [(EV, "        if (stop.IsCancellationRequested) throw Stopped();\n", "")]),
    ("M25 a stop checked only at the root", [(EV, "        if (stop.IsCancellationRequested) throw Stopped();\n", "        if (stop.IsCancellationRequested && node.Depth == 0) throw Stopped();\n")]),
]


def apply(text, old, new):
    for o, n in ((old, new), (old.replace("\n", "\r\n"), new.replace("\n", "\r\n"))):
        if text.count(o) == 1:
            return text.replace(o, n)
    raise ValueError(f"not found exactly once: {old[:70]!r} (count {text.count(old)})")


def test(args, timeout):
    p = subprocess.run(["dotnet", "test", *args, "--nologo", "--logger", "console;verbosity=normal"],
                       cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    return p.returncode, p.stdout + p.stderr


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
        runs = [[r"tests\Calc.Worker.Tests"]]
        if any(path.startswith(r"src\Calc.Engine") for path, _, _ in edits):
            runs.append([r"tests\Calc.Engine.Tests", "--filter", "FullyQualifiedName~ContextsTests"])
        failed, code, out = [], 0, ""
        for args in runs:
            try:
                c, o = test(args, 600)
            except subprocess.TimeoutExpired:
                return f"TIMEOUT    {name}"
            code |= c
            out += o
            failed += re.findall(r"Failed Calc\.\w+\.Tests\.(\S+)", o)
        dt = time.time() - t0
        if "error CS" in out:
            err = re.search(r"error CS\d+: [^\[]*", out)
            return f"BUILD ERR  {name}: {err.group(0) if err else ''}"
        if "Test Run Aborted" in out:
            return f"ABORTED    {name} ({dt:.0f} s): the test host crashed"
        failed = sorted(set(failed))
        if failed or code != 0:
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
