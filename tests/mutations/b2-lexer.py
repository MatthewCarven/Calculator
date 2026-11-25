"""Calc's B2 mutation battery on the lexer, by ../Natural/CLAUDE.md's recipe.

usage: python tests/mutations/b2-lexer.py <first> <count> | check
Each mutation's old text must match exactly once. It's applied, the Engine tests run, and the file is restored in
`finally`. All 20 were predicted red. Don't build Calc while it runs.
"""
import re, subprocess, sys, time
from pathlib import Path

ROOT = str(Path(__file__).resolve().parents[2])
LX = r"src\Calc.Syntax\Lexer.cs"

M = [
    ("L1 an exponent with no digits taken", [(LX, "IsDigit(s[j]) ? ScanDigits(s, j, 10) : i;", "IsDigit(s[j]) ? ScanDigits(s, j, 10) : j;")]),
    ("L2 no sign on an exponent", [(LX, "if (j < s.Length && s[j] is '+' or '-') j++;", "")]),
    ("L3 a hex fraction without p", [(LX, "if (point && end == i)", "if (false)")]),
    ("L4 an underscore may start a run", [(LX, "if (i == first || i + 1 >= s.Length", "if (i + 1 >= s.Length")]),
    ("L5 an underscore before a non-digit", [(LX, "if (i == first || i + 1 >= s.Length || DigitValue(s[i + 1]) >= radix)", "if (i == first || i + 1 >= s.Length)")]),
    ("L6 a digit the radix lacks just ends it", [(LX, "else if (IsDigit(c))", "else if (false)")]),
    ("L7 no check for a second point", [(LX, "if (end < s.Length && s[end] == '.')", "if (false)")]),
    ("L8 the two point messages swapped", [(LX, "exponent ? \"An exponent can't have a point.\" : \"A number has one point at most.\"",
                                            "exponent ? \"A number has one point at most.\" : \"An exponent can't have a point.\"")]),
    ("L9 pi joins a name", [(LX, "c == '_' || (char.IsLetter(c) && !IsConstantLetter(c));", "c == '_' || char.IsLetter(c);")]),
    ("L10 ** is two stars", [(LX, "case '*' when next == '*': return (TokenKind.Caret, 2);", "")]),
    ("L11 to isn't a word", [(LX, "\"to\" => TokenKind.To,", "")]),
    ("L12 0B isn't a prefix", [(LX, "'b' or 'B' => 2,", "'b' => 2,")]),
    ("L13 octal fraction not refused by name", [(LX, "if (i < s.Length && s[i] == '.') throw Error(start, i + 1, \"Octal numbers are whole.\");", "")]),
    ("L14 only spaces are whitespace", [(LX, "while (i < source.Length && char.IsWhiteSpace(source[i])) i++;", "while (i < source.Length && source[i] == ' ') i++;")]),
    ("L15 f isn't a hex digit", [(LX, ">= 'a' and <= 'f' => c - 'a' + 10,", ">= 'a' and <= 'e' => c - 'a' + 10,")]),
    ("L16 .5 isn't a number", [(LX, " || (c == '.' && i + 1 < source.Length && IsDigit(source[i + 1]))", "")]),
    ("L17 a surrogate pair's error one wide", [(LX, "? rune.Utf16SequenceLength : 1;", "? 1 : 1;")]),
    ("L18 the text cache past ASCII", [(LX, "end - start == 1 && s[start] < 128 ?", "end - start == 1 ?")]),
    ("L19 p is a decimal exponent too", [(LX, "int end = ScanExponent(s, i, \"eE\");", "int end = ScanExponent(s, i, \"eEpP\");")]),
    ("L20 a hex number needs whole digits", [(LX, "if (!whole && !fraction)", "if (!whole)")]),
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
                               cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=240)
        except subprocess.TimeoutExpired:
            return f"TIMEOUT    {name}"
        out = p.stdout + p.stderr
        dt = time.time() - t0
        if "error CS" in out:
            err = re.search(r"error CS\d+: [^\[]*", out)
            return f"BUILD ERR  {name}: {err.group(0) if err else ''}"
        failed = sorted(set(re.findall(r"Failed Calc\.Engine\.Tests\.(\S+)", out)))
        if failed or p.returncode != 0:
            return f"RED        {name} ({dt:.0f} s, {len(failed)} failed): {', '.join(f.split('.')[-1][:60] for f in failed[:3])}"
        return f"green      {name} ({dt:.0f} s)"
    finally:
        for full, data in originals.items():
            open(full, "wb").write(data)


if sys.argv[1] == "check":
    text = open(ROOT + "\\" + LX, "rb").read().decode("utf-8")
    for name, edits in M:
        for path, old, new in edits:
            try:
                apply(text, old, new)
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
