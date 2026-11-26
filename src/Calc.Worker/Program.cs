// calc-worker. B9 puts the stdio loop here. For agents and people, --eval evaluates one expression:
//   calc-worker --eval "0.1+0.2" [--bits 53 | --format binary64] [--mode nearest|zero|up|down]
//               [--notation auto|scientific|engineering|fixed|aligned] [--group <separator>] [--raw] [--exact on|off]
//               [--caption]
// The result goes to stdout as the window will show it, the digits its precision guarantees (rule 9), or with --raw as
// every digit that reads back; --caption adds the line under it, such as "exact = 3/10" or "float, 76 digits". Exact is
// on by default, as in the window (Matthew, 2026-11-07). Any flags go to stderr.
// An error goes to stderr, with exit code 1; bad arguments, 2.

using Calc.Engine.Eval;
using Calc.Engine.Text;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

if (args.Length < 2 || args[0] != "--eval") return Usage();

string source = args[1];
Target target = Target.Bits(256);
RoundingMode mode = RoundingMode.ToNearestEven;
var options = TextOptions.Default;
bool raw = false;
bool caption = false;
bool exact = true;
for (int i = 2; i < args.Length; i++)
{
    if (args[i] == "--raw")
    {
        raw = true;
        continue;
    }
    if (args[i] == "--caption")
    {
        caption = true;
        continue;
    }
    if (i + 1 >= args.Length) return Usage();
    string value = args[++i];
    switch (args[i - 1])
    {
        case "--bits" when int.TryParse(value, out int bits) && bits >= 1 && bits <= ApFloat.MaxPrecision:
            target = Target.Bits(bits);
            break;
        case "--format" when Format(value) is IeeeFormat format:
            target = Target.Of(format);
            break;
        case "--mode" when Mode(value) is RoundingMode m:
            mode = m;
            break;
        case "--notation" when Enum.TryParse(value, ignoreCase: true, out Notation notation) && Enum.IsDefined(notation):
            options = options with { Notation = notation };
            break;
        case "--group":
            options = options with { Separator = value };
            break;
        case "--exact" when value is "on" or "off":
            exact = value == "on";
            break;
        default:
            return Usage();
    }
}

// Parse, evaluate and write the text on a big stack, as the loop will (rule 13).
Outcome result = default;
string text = "";
CalcError? error = null;
var compute = new Thread(() =>
{
    try
    {
        result = Evaluator.Run(source, target, mode, exact: exact);
        ResultText? shown = raw && !caption ? null : ResultText.Of(result.Value, target, options);
        text = (raw ? result.Value.ToString()! : shown!.Main) + (caption ? Environment.NewLine + shown!.Caption : "");
    }
    catch (Exception e) { error = ErrorMap.From(e, null); }
}, 256 << 20);
compute.Start();
compute.Join();

if (error is not null)
{
    Console.Error.WriteLine($"{error.Kind}{(error.Span is TextSpan span ? $" at {span}" : "")}: {error.Message}");
    return 1;
}
Console.WriteLine(text);
if (result.Flags != IeeeFlags.None) Console.Error.WriteLine($"flags: {result.Flags}");
return 0;

static IeeeFormat? Format(string name) => name switch
{
    "binary16" => IeeeFormat.Binary16,
    "binary32" => IeeeFormat.Binary32,
    "binary64" => IeeeFormat.Binary64,
    "binary128" => IeeeFormat.Binary128,
    "binary256" => IeeeFormat.Binary256,
    _ => null,
};

static RoundingMode? Mode(string name) => name switch
{
    "nearest" => RoundingMode.ToNearestEven,
    "zero" => RoundingMode.TowardZero,
    "up" => RoundingMode.TowardPositive,
    "down" => RoundingMode.TowardNegative,
    _ => null,
};

static int Usage()
{
    Console.Error.WriteLine("usage: calc-worker --eval <expression> [--bits <1 to 2^30> | --format binary16|32|64|128|256] [--mode nearest|zero|up|down] [--notation auto|scientific|engineering|fixed|aligned] [--group <separator>] [--raw] [--exact on|off] [--caption]");
    return 2;
}
