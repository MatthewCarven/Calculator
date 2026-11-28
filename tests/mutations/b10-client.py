"""Calc's B10 mutation battery on the worker client and the heap cap, by ../Natural/CLAUDE.md's recipe.

usage: python tests/mutations/b10-client.py <first> <count> | check
Each mutation's old text must match exactly once. It's applied, the client's tests run, and the file is restored in
`finally`. None of these crashes a worker. Don't build Calc while it runs.

2026-11-29: 17 of 20 red (M2 after fixing its own edit, which hadn't built). Three survive. M5: the reader thread clears a
killed worker within milliseconds of the kill, and the clear in End only closes that window, which no test hits reliably.
M11: equivalent, since .NET sets AutoFlush on a redirected StandardInput. M15: equivalent in practice, since the killed
process has exited before a test can look; the wait stays as the guarantee.
"""
import ctypes, re, subprocess, sys, time
from pathlib import Path

ROOT = str(Path(__file__).resolve().parents[2])
sys.stdout.reconfigure(encoding="utf-8")
WC = r"src\Calc.Client\WorkerClient.cs"
CI = r"src\Calc.Client\CommitInfo.cs"

M = [
    # Stop
    ("M1 a grace of 2 s", [(WC, "TimeSpan.FromMilliseconds(150);", "TimeSpan.FromMilliseconds(2000);")]),
    ("M2 no soft stop sent", [(WC, "if (waiting.IsEval) connection.TryWrite(Messages.Write(new StopRequest(id)));", "if (false) connection.TryWrite(Messages.Write(new StopRequest(id)));")]),
    ("M3 the kill skipped", [(WC, "        connection.Kill();\n        foreach (var (id, reply) in orphans) reply.TrySetResult(new ErrorReply(id, \"Stopped\", message));",
                              "        foreach (var (id, reply) in orphans) reply.TrySetResult(new ErrorReply(id, \"Stopped\", message));")]),
    ("M4 a killed worker's tasks left waiting", [(WC, "        connection.Kill();\n        foreach (var (id, reply) in orphans) reply.TrySetResult(new ErrorReply(id, \"Stopped\", message));",
                                                  "        connection.Kill();")]),
    ("M5 a killed worker kept as the live one", [(WC, "            connection.Killed = true;\n            if (_live == connection) _live = null;", "            connection.Killed = true;")]),
    # Death and the reader
    ("M6 a dead worker kept as the live one", [(WC, "                killed = Killed;\n                if (owner._live == this) owner._live = null;", "                killed = Killed;")]),
    ("M7 a death reported as Stopped", [(WC, "killed ? \"Stopped\" : ErrorKinds.WorkerDied", "\"Stopped\"")]),
    ("M8 replies matched to the wrong id", [(WC, "if (IdOf(reply) is long id && Waiting.Remove(id, out var entry))", "if (IdOf(reply) is long id && Waiting.Remove(id + 1, out var entry))")]),
    ("M9 progress taken as the answer", [(WC, "                    if (reply is ProgressReply) continue;\n", "")]),
    ("M10 a reply for nobody given to the oldest", [(WC, "if (IdOf(reply) is long id && Waiting.Remove(id, out var entry)) waiting = entry.Reply;",
                                                     "if (IdOf(reply) is long id && (Waiting.Remove(id, out var entry) || (Waiting.Count > 0 && Waiting.Remove(Waiting.Keys.Min(), out entry)))) waiting = entry.Reply;")]),
    ("M11 requests not flushed", [(WC, "                process.StandardInput.Flush();\n", "")]),
    # Starting
    ("M12 the cap not passed on", [(WC, "        start.Environment[\"DOTNET_GCHeapHardLimit\"] = HeapCap.EnvironmentValue(cap);\n", "")]),
    ("M13 free commit ignored", [(WC, "if (HeapCap.For(_options.HeapCapBytes, free) is not long cap)", "if (HeapCap.For(_options.HeapCapBytes, null) is not long cap)")]),
    ("M14 Dispose doesn't end the worker", [(WC, "        End(connection, \"Stopped: the calculator closed.\");\n        // Until", "        // Until")]),
    ("M15 Dispose doesn't wait for the end", [(WC, "        connection.Finished.Wait(TimeSpan.FromSeconds(5));\n", "")]),
    # The heap cap
    ("M16 no reserve kept", [(CI, "cap = Int128.Min(cap, (Int128)free - Reserve);", "cap = Int128.Min(cap, (Int128)free);")]),
    ("M17 unsigned, so a shortfall wraps", [(CI, "cap = Int128.Min(cap, (Int128)free - Reserve);", "cap = Int128.Min(cap, (Int128)unchecked(free - (ulong)Reserve));")]),
    ("M18 no floor", [(CI, "return cap < Floor ? null : (long)cap;", "return cap <= 0 ? null : (long)cap;")]),
    ("M19 the cap written in decimal", [(CI, "public static string EnvironmentValue(long cap) => \"0x\" + cap.ToString(\"X\", CultureInfo.InvariantCulture);",
                                         "public static string EnvironmentValue(long cap) => cap.ToString(CultureInfo.InvariantCulture);")]),
    ("M20 the page file's total, not what's free", [(CI, "return GlobalMemoryStatusEx(ref status) ? status.AvailPageFile : null;", "return GlobalMemoryStatusEx(ref status) ? status.TotalPageFile * 0 : null;")]),
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
            p = subprocess.run(["dotnet", "test", r"tests\Calc.Client.Tests", "--nologo", "--logger", "console;verbosity=normal"],
                               cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600)
        except subprocess.TimeoutExpired:
            return f"TIMEOUT    {name}"
        out = p.stdout + p.stderr
        dt = time.time() - t0
        if "error CS" in out:
            err = re.search(r"error CS\d+: [^\[]*", out)
            return f"BUILD ERR  {name}: {err.group(0) if err else ''}"
        if "Test Run Aborted" in out:
            return f"ABORTED    {name} ({dt:.0f} s): the test host crashed"
        failed = sorted(set(re.findall(r"Failed Calc\.Client\.Tests\.(\S+)", out)))
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
