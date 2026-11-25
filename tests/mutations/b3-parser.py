"""Calc's B3 mutation battery on the parser and printer, by ../Natural/CLAUDE.md's recipe.

usage: python tests/mutations/b3-parser.py <first> <count> | check
Each mutation's old text must match exactly once. It's applied, the Engine tests run, and the file is restored in
`finally`. All 24 were predicted red. 23 were, first time; P11 survived until WideIsNotDeep came, and is red now.
None touches the stack guard, so none can overflow the stack and crash the test
host, which on Windows also pops up a dialog. Don't build Calc while it runs.
"""
import ctypes, re, subprocess, sys, time
from pathlib import Path

ROOT = str(Path(__file__).resolve().parents[2])
PA = r"src\Calc.Syntax\Parser.cs"
PR = r"src\Calc.Syntax\Printer.cs"

M = [
    # Parser: precedence and associativity
    ("P1 ^ left associative", [(PA, "kind == TokenKind.Caret ? power - 1 : power, op)", "power, op)")]),
    ("P2 prefix binds tighter than ^", [(PA, "private const int PrefixPower = 90;", "private const int PrefixPower = 105;")]),
    ("P3 implicit products group to the right", [(PA, "Node right = ParseExpression(ProductPower, NoOwner);", "Node right = ParseExpression(ProductPower - 1, NoOwner);")]),
    ("P4 shifts bind tighter than +", [(PA, "TokenKind.ShiftLeft or TokenKind.ShiftRight => 60,", "TokenKind.ShiftLeft or TokenKind.ShiftRight => 75,")]),
    # Parser: what may start an implicit product, and the % rule
    ("P5 a number starts an implicit product", [(PA, "private static bool StartsProduct(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.LeftParen or TokenKind.Root;",
                                                 "private static bool StartsProduct(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.LeftParen or TokenKind.Root or TokenKind.Number;")]),
    ("P6 no % rule", [(PA, "if (postfix == PostfixOp.Percent && (", "if (false && (")]),
    ("P7 the % rule misses numbers", [(PA, "|| _tokens[_at].Kind is TokenKind.Number or TokenKind.Tilde))", "|| _tokens[_at].Kind is TokenKind.Tilde))")]),
    ("P8 a name before ( isn't a call", [(PA, "_tokens[_at].Kind == TokenKind.LeftParen ? ParseCall(at) :", "false ? ParseCall(at) :")]),
    ("P9 a call takes one argument", [(PA, "if (_tokens[_at].Kind != TokenKind.Comma) break;", "break;")]),
    # Parser: depth
    ("P10 the counter one too loose", [(PA, "if (++_nesting > MaxDepth)", "if (++_nesting > MaxDepth + 1)")]),
    ("P11 the counter never comes down", [(PA, "        _nesting--;\n        return left;", "        return left;")]),
    ("P12 no tree-depth check", [(PA, "node.Depth <= MaxDepth ? node :", "true ? node :")]),
    # Parser: spans and errors
    ("P13 brackets don't widen the span", [(PA, "inner.Span = new TextSpan(_tokens[at].Span.Start, _tokens[_at].Span.End);", "")]),
    ("P14 the operator's owner forgotten", [(PA, "kind == TokenKind.Caret ? power - 1 : power, op)", "kind == TokenKind.Caret ? power - 1 : power, NoOwner)")]),
    # Printer: parentheses
    ("R1 (2^3)^2 loses its parentheses", [(PR, "WriteOperand(b.Left, Rank(b.Left) <= rank);", "WriteOperand(b.Left, Rank(b.Left) < rank);")]),
    ("R2 1-(2-3) loses its parentheses", [(PR, "WriteOperand(b.Right, Rank(b.Right) <= rank);", "WriteOperand(b.Right, Rank(b.Right) < rank);")]),
    ("R3 a prefix wraps ^ needlessly", [(PR, "WriteOperand(p.Operand, Rank(p.Operand) < PrefixRank);", "WriteOperand(p.Operand, Rank(p.Operand) < PostfixRank);")]),
    # Printer: implicit products read back
    ("R4 implicit products glued", [(PR, "text.Append(understood ? \"·\" : \" \");", "text.Append(understood ? \"·\" : \"\");")]),
    ("R5 a name before ( not wrapped", [(PR, "|| (!understood && rightStartsWithParen && EndsWithName(b.Left))", "")]),
    ("R6 only the printer's own ( counts", [(PR, "bool rightStartsWithParen = rightInParens || First(b.Right) == Start.Paren;", "bool rightStartsWithParen = rightInParens;")]),
    ("R7 % before an operand not wrapped", [(PR, "|| (!understood && EndsWithPercent(b.Left))", "")]),
    ("R8 Last stops at an implicit product", [(PR, "case BinaryNode { IsImplicit: true } b when !RightInParens(b):\n                        node = b.Right;\n                        break;\n", "")]),
    ("R9 a prefix - may start the right side", [(PR, "First(b.Right) is Start.Number or Start.OtherPrefix;", "First(b.Right) is Start.Number;")]),
    # Printer: the understood form
    ("R10 (1/2)x shown without parentheses", [(PR, "|| (understood && b.Left is BinaryNode { IsImplicit: false, Op: BinaryOp.Divide or BinaryOp.Mod or BinaryOp.Rem });", ";")]),
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
