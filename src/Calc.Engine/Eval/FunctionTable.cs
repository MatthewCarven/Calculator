using Calc.Syntax;

namespace Calc.Engine.Eval;

/// <summary>A function's name, how many arguments it takes, and the session it's waiting on, if any.</summary>
public sealed record Function(string Name, int MinArguments, int MaxArguments, string? WaitingFor = null)
{
    public bool IsAvailable => WaitingFor is null;
}

/// <summary>
/// Every function Calc knows: the one list of names and arities, for the evaluator, B9's <c>functions</c> reply and the
/// window's keys. Those waiting on Natural are listed, so their names are known and they say not available.
/// </summary>
public static class FunctionTable
{
    private const int Many = int.MaxValue;

    private static readonly Dictionary<string, Function> ByName = new Function[]
    {
        new("sqrt", 1, 1), new("abs", 1, 1), new("sign", 1, 1),
        new("floor", 1, 1), new("ceil", 1, 1), new("trunc", 1, 1), new("round", 1, 1),
        new("max", 1, Many), new("min", 1, Many), new("fma", 3, 3),
        new("exp", 1, 1, "W1"), new("ln", 1, 1, "W1"), new("log1p", 1, 1, "W1"),
        new("log2", 1, 1, "W2"), new("log10", 1, 1, "W2"), new("log", 1, 2, "W2"), new("expm1", 1, 1, "W2"),
        new("exp2", 1, 1, "W2"), new("exp10", 1, 1, "W2"), new("root", 2, 2, "W2"), new("cbrt", 1, 1, "W2"), new("hypot", 2, 2, "W2"),
        new("sin", 1, 1, "W3"), new("cos", 1, 1, "W3"), new("tan", 1, 1, "W3"),
        new("asin", 1, 1, "W5"), new("acos", 1, 1, "W5"), new("atan", 1, 1, "W5"), new("atan2", 2, 2, "W5"),
        new("sinh", 1, 1, "W5"), new("cosh", 1, 1, "W5"), new("tanh", 1, 1, "W5"),
        new("asinh", 1, 1, "W5"), new("acosh", 1, 1, "W5"), new("atanh", 1, 1, "W5"),
    }.ToDictionary(f => f.Name);

    public static IReadOnlyCollection<Function> All => ByName.Values;

    public static Function? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>The function, if it exists, takes that many arguments, and works yet; otherwise the error to show.</summary>
    public static Function Check(string name, int arguments, TextSpan at)
    {
        if (Find(name) is not Function f) throw new CalcError(ErrorKind.Syntax, $"Unknown function '{name}'.", at);
        if (arguments < f.MinArguments || arguments > f.MaxArguments)
        {
            string wants = f.MinArguments == f.MaxArguments ? $"{f.MinArguments}" : f.MaxArguments == Many ? $"at least {f.MinArguments}" : $"{f.MinArguments} or {f.MaxArguments}";
            bool one = f.MinArguments == 1 && f.MaxArguments is 1 or Many;
            throw new CalcError(ErrorKind.Syntax, $"{name} takes {wants} argument{(one ? "" : "s")}, not {arguments}.", at);
        }
        if (!f.IsAvailable) throw new CalcError(ErrorKind.NotAvailable, $"{name} isn't available yet.", at);
        return f;
    }
}
