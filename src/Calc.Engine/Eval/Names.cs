using Calc.Engine.Values;
using Calc.Syntax;

namespace Calc.Engine.Eval;

/// <summary>
/// What a bare name means: a constant, <c>Ans</c>, and later the person's definitions (H2). An unknown name is a
/// <see cref="CalcError"/>.
/// </summary>
public sealed class Names(Value? ans = null)
{
    // Calculated to the target, which needs Natural's transcendentals (W0).
    private static readonly HashSet<string> Constants = ["π", "pi", "τ", "tau", "φ", "phi", "e"];

    public Value Resolve(string name, TextSpan at, IArithmetic arithmetic)
    {
        if (name == "Ans")
            return ans is null
                ? throw new CalcError(ErrorKind.NotAvailable, "There's no answer yet for Ans to mean.", at)
                : arithmetic.Import(ans, at);
        if (Constants.Contains(name))
            throw new CalcError(ErrorKind.NotAvailable, $"{name} isn't available yet.", at);
        throw new CalcError(ErrorKind.Syntax, $"Unknown name '{name}'.", at);
    }
}
