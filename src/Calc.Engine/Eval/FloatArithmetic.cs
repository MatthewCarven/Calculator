using System.Globalization;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Eval;

/// <summary>
/// Floats, each operation correctly rounded through the evaluation's one <see cref="IeeeContext"/> (rule 4), never
/// ApFloat's operators, which widen to the larger operand's precision. + − × ÷ and negate now; the rest say not
/// available until B5, and ! until F4.
/// </summary>
/// <remarks>
/// The policy follows the target. At bits it's Mathematical: an operation that raises DivideByZero or Invalid is an
/// error, with the operator's span, and division by zero is always DivideByZero, even 0/0. At a format it's IEEE: ∞ and
/// NaN are values, and the flags only report.
/// </remarks>
public sealed class FloatArithmetic(Target target, RoundingMode mode) : IArithmetic
{
    private readonly IeeeContext _ieee = target.NewContext(mode);
    private readonly bool _mathematical = !target.IsFormat;

    public IeeeFlags Flags => _ieee.Flags;

    /// <summary>
    /// Rounded once: decimal and hex-float text by <see cref="IeeeContext.Parse"/>, whole numbers exactly by
    /// <see cref="ApInt.Parse(string, IFormatProvider?)"/> then <see cref="IeeeContext.Convert"/>, and 0o by Horner, since
    /// Natural has no octal. Underscores go first: Natural refuses them in an exponent.
    /// </summary>
    public Value Literal(string text, TextSpan at)
    {
        string s = text.Replace("_", "");
        ApFloat x;
        if (s.Length > 2 && s[0] == '0' && s[1] is 'o' or 'O')
            x = _ieee.Convert(Octal(s));
        else if (IsWhole(s))
            x = _ieee.Convert(ApInt.Parse(s, CultureInfo.InvariantCulture));
        else
            x = _ieee.Parse(s, CultureInfo.InvariantCulture);
        return new FloatValue(x);
    }

    public Value Import(Value value, TextSpan at)
    {
        ApFloat x = F(value);
        // Rule 14: a float can't leave its target. The worker refuses a mismatch first (B9); this is the wall behind it.
        if (x.Precision != _ieee.Precision)
            throw new CalcError(ErrorKind.Internal, $"A {x.Precision}-bit value reached a {target} evaluation.", at);
        return value;
    }

    public Value Add(Value a, Value b, TextSpan at)
    {
        var before = _ieee.Flags;
        return Checked(_ieee.Add(F(a), F(b)), before, at);
    }

    public Value Subtract(Value a, Value b, TextSpan at)
    {
        var before = _ieee.Flags;
        return Checked(_ieee.Subtract(F(a), F(b)), before, at);
    }

    public Value Multiply(Value a, Value b, TextSpan at)
    {
        var before = _ieee.Flags;
        return Checked(_ieee.Multiply(F(a), F(b)), before, at);
    }

    public Value Divide(Value a, Value b, TextSpan at)
    {
        ApFloat y = F(b);
        if (_mathematical && y.IsZero) throw new CalcError(ErrorKind.DivideByZero, "Division by zero.", at);
        var before = _ieee.Flags;
        return Checked(_ieee.Divide(F(a), y), before, at);
    }

    /// <summary>Exact, as IEEE's negate is: the sign flips, and Convert leaves the value at the target untouched.</summary>
    public Value Negate(Value a, TextSpan at)
    {
        var before = _ieee.Flags;
        return Checked(_ieee.Convert(-F(a)), before, at);
    }

    public Value Power(Value a, Value b, TextSpan at) => throw NotYet("Powers", at);
    public Value Mod(Value a, Value b, TextSpan at) => throw NotYet("mod", at);
    public Value Rem(Value a, Value b, TextSpan at) => throw NotYet("rem", at);
    public Value Percent(Value a, TextSpan at) => throw NotYet("%", at);
    public Value Factorial(Value a, TextSpan at) => throw NotYet("Factorials", at);
    public Value And(Value a, Value b, TextSpan at) => throw NotYet("Bit operators", at);
    public Value Or(Value a, Value b, TextSpan at) => throw NotYet("Bit operators", at);
    public Value Xor(Value a, Value b, TextSpan at) => throw NotYet("Bit operators", at);
    public Value ShiftLeft(Value a, Value b, TextSpan at) => throw NotYet("Bit operators", at);
    public Value ShiftRight(Value a, Value b, TextSpan at) => throw NotYet("Bit operators", at);
    public Value Not(Value a, TextSpan at) => throw NotYet("Bit operators", at);
    public Value Call(string name, IReadOnlyList<Value> arguments, TextSpan at) => throw NotYet($"{name}()", at);

    public int? Compare(Value a, Value b)
    {
        ApFloat x = F(a), y = F(b);
        return x.IsNaN || y.IsNaN ? null : Math.Sign(x.CompareTo(y));
    }

    /// <summary>At bits, a newly raised DivideByZero or Invalid is an error. At a format the flags only report.</summary>
    private FloatValue Checked(ApFloat result, IeeeFlags before, TextSpan at)
    {
        if (_mathematical)
        {
            var raised = _ieee.Flags & ~before;
            if ((raised & IeeeFlags.DivideByZero) != 0) throw new CalcError(ErrorKind.DivideByZero, "Division by zero.", at);
            if ((raised & IeeeFlags.Invalid) != 0) throw new CalcError(ErrorKind.Domain, "That has no answer.", at);
        }
        return new FloatValue(result);
    }

    private static ApFloat F(Value value) => value is FloatValue f
        ? f.Number
        : throw new CalcError(ErrorKind.Internal, $"FloatArithmetic was given a {value.GetType().Name}.");

    private static bool IsWhole(string s) =>
        s.Length > 2 && s[0] == '0' && s[1] is 'x' or 'X' or 'b' or 'B'
            ? s.AsSpan(2).IndexOfAny(".pP") < 0
            : s.AsSpan().IndexOfAny(".eE") < 0;

    private static ApInt Octal(string s)
    {
        ApInt n = 0;
        foreach (char c in s.AsSpan(2)) n = n * 8 + (c - '0');
        return n;
    }

    private static CalcError NotYet(string what, TextSpan at) =>
        new(ErrorKind.NotAvailable, $"{what} {(what.EndsWith('s') ? "aren't" : "isn't")} available yet.", at);
}
