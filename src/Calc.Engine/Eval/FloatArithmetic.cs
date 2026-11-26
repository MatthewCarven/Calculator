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
        // An exact Ans, with Exact off, converts at use (B11); the conversion is its one rounding.
        if (value is not FloatValue) return new FloatValue(F(value));
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
        if (_mathematical && y.IsZero) throw DivisionByZero(at);
        var before = _ieee.Flags;
        return Checked(_ieee.Divide(F(a), y), before, at);
    }

    /// <summary>Exact, as IEEE's negate is: the sign flips, and Convert leaves the value at the target untouched.</summary>
    public Value Negate(Value a, TextSpan at)
    {
        var before = _ieee.Flags;
        return Checked(_ieee.Convert(-F(a)), before, at);
    }

    /// <summary>x^y, correctly rounded for any integer y: see <see cref="PowerFloat"/>.</summary>
    public Value Power(Value a, Value b, TextSpan at)
    {
        var before = _ieee.Flags;
        return Checked(PowerFloat.Power(_ieee, F(a), Exponent(b), at), before, at);
    }

    /// <summary>
    /// The exponent as itself where it's exact and an integer or a half, since PowerFloat reads its value as it stands:
    /// rounded to the target, an exact 10^30 or 2^70 + 1 would be a different exponent. Anything else converts as usual.
    /// </summary>
    private ApFloat Exponent(Value b) => b switch
    {
        IntValue i => i.Number,
        RatioValue { Denominator: var d } r when d == 2 => new ApFloat(r.Numerator, -1, ApFloat.MaxPrecision),
        _ => F(b),
    };

    /// <summary>Floored: the result takes the divisor's sign, so -7 mod 3 is 2. Exact, then rounded once.</summary>
    public Value Mod(Value a, Value b, TextSpan at)
    {
        ApFloat x = F(a), y = F(b);
        if (_mathematical && y.IsZero) throw DivisionByZero(at);
        var before = _ieee.Flags;
        // The truncated remainder is exact. Where its sign differs from y's, the floored one is r + y, rounded once.
        ApFloat r = _ieee.Remainder(x, y);
        if (r.IsZero) r = ApFloat.CopySign(r, y);
        else if (!r.IsNaN && r.IsNegative != y.IsNegative) r = _ieee.Add(r, y);
        return Checked(r, before, at);
    }

    /// <summary>Truncated, as C#'s %: the result takes the dividend's sign, so -7 rem 3 is -1. Exact.</summary>
    public Value Rem(Value a, Value b, TextSpan at)
    {
        ApFloat y = F(b);
        if (_mathematical && y.IsZero) throw DivisionByZero(at);
        var before = _ieee.Flags;
        return Checked(_ieee.Remainder(F(a), y), before, at);
    }

    /// <summary>÷100, rounded once (Matthew, 2026-11-25): 200 + 10% is 200.1.</summary>
    public Value Percent(Value a, TextSpan at)
    {
        var before = _ieee.Flags;
        return Checked(_ieee.Divide(F(a), 100), before, at);
    }

    public Value Factorial(Value a, TextSpan at) => throw NotYet("Factorials", at);

    // The bit operators work on whole numbers, as two's complement integers of unlimited width: ~5 is -6. A fraction,
    // infinity or NaN is Domain, at a format too, since IEEE has no bit operators.

    public Value And(Value a, Value b, TextSpan at) => FromWhole(Whole(a, at) & Whole(b, at), at);
    public Value Or(Value a, Value b, TextSpan at) => FromWhole(Whole(a, at) | Whole(b, at), at);
    public Value Xor(Value a, Value b, TextSpan at) => FromWhole(Whole(a, at) ^ Whole(b, at), at);
    public Value Not(Value a, TextSpan at) => FromWhole(~Whole(a, at), at);

    /// <summary>floor(x·2^n), so a negative n shifts right: x &lt;&lt; n is x &gt;&gt; -n.</summary>
    public Value ShiftLeft(Value a, Value b, TextSpan at) => Shift(a, b, left: true, at);

    /// <summary>floor(x·2^-n): an arithmetic shift, so -7 &gt;&gt; 1 is -4.</summary>
    public Value ShiftRight(Value a, Value b, TextSpan at) => Shift(a, b, left: false, at);

    public Value Call(string name, IReadOnlyList<Value> arguments, TextSpan at)
    {
        FunctionTable.Check(name, arguments.Count, at);
        var before = _ieee.Flags;
        ApFloat result = name switch
        {
            "sqrt" => _ieee.Sqrt(F(arguments[0])),
            "abs" => _ieee.Convert(ApFloat.Abs(F(arguments[0]))),
            "sign" => Sign(F(arguments[0])),
            // Natural's own floor and its kin are exact and raise nothing: IEEE's roundToIntegralExact would raise
            // Inexact for floor(1.5), whose answer, 1, is exact.
            "floor" => _ieee.Convert(ApFloat.Floor(F(arguments[0]))),
            "ceil" => _ieee.Convert(ApFloat.Ceiling(F(arguments[0]))),
            "trunc" => _ieee.Convert(ApFloat.Truncate(F(arguments[0]))),
            "round" => _ieee.Convert(ApFloat.Round(F(arguments[0]))),
            "max" => _ieee.Convert(Fold(arguments, ApFloat.Max)),
            "min" => _ieee.Convert(Fold(arguments, ApFloat.Min)),
            "fma" => _ieee.FusedMultiplyAdd(F(arguments[0]), F(arguments[1]), F(arguments[2])),
            _ => throw new CalcError(ErrorKind.Internal, $"{name} is listed as available, but FloatArithmetic has no {name}.", at),
        };
        return Checked(result, before, at);
    }

    /// <summary>-1, or 1; a zero keeps its own sign, and NaN stays NaN.</summary>
    private ApFloat Sign(ApFloat x) => x.IsZero || x.IsNaN ? x : _ieee.Convert(x.IsNegative ? -1 : 1);

    private ApFloat Fold(IReadOnlyList<Value> arguments, Func<ApFloat, ApFloat, ApFloat> pick)
    {
        ApFloat result = F(arguments[0]);
        for (int i = 1; i < arguments.Count; i++) result = pick(result, F(arguments[i]));
        return result;
    }

    /// <summary>Bit operators stop at this many bits, as B7's exact integers will: 2^24, about 5 million digits.</summary>
    internal const long BitLimit = 1L << 24;

    private static bool IsWhole(ApFloat x) => !x.IsNaN && !x.IsInfinity && ApFloat.IsInteger(x);

    private ApInt Whole(Value value, TextSpan at)
    {
        ApFloat x = F(value);
        if (!IsWhole(x)) throw new CalcError(ErrorKind.Domain, "Bit operators need whole numbers.", at);
        if (!x.IsZero && x.Exponent + x.Significand.BitLength > BitLimit)
            throw new CalcError(ErrorKind.TooBig, $"That's too large for a bit operator: more than {BitLimit:N0} bits.", at);
        return (ApInt)x;
    }

    private FloatValue FromWhole(ApInt n, TextSpan at)
    {
        var before = _ieee.Flags;
        return Checked(_ieee.Convert(n), before, at);
    }

    private FloatValue Shift(Value a, Value b, bool left, TextSpan at)
    {
        ApFloat x = F(a), count = F(b);
        // Whole numbers, but no size limit: scaling allocates nothing.
        if (!IsWhole(x)) throw new CalcError(ErrorKind.Domain, "Bit operators need whole numbers.", at);
        if (!IsWhole(count)) throw new CalcError(ErrorKind.Domain, "A shift needs a whole number of places.", at);
        long n = long.CreateChecked((ApInt)count);
        // x·2^n is exact at x's precision with an unbounded exponent, and so is its floor; only the last step rounds,
        // into the target. Scaling inside a format first could round a tiny negative to -0, whose floor is wrong.
        ApFloat scaled = ApFloat.Multiply(x, new ApFloat(1, left ? n : checked(-n), 1), x.Precision);
        var before = _ieee.Flags;
        return Checked(_ieee.Convert(ApFloat.Floor(scaled)), before, at);
    }

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

    /// <summary>
    /// A value as a float at the target. An exact one converts here, rounding once: an integer by Convert, a fraction by
    /// one Divide, since the implicit conversions are exact up to 2^30 bits. That's how Exact off, and B7's mixed
    /// operations, treat exact operands; B8 makes the mixed ones honest.
    /// </summary>
    private ApFloat F(Value value) => value switch
    {
        FloatValue f => f.Number,
        IntValue i => _ieee.Convert(i.Number),
        RatioValue r => _ieee.Divide(r.Numerator, r.Denominator),
        _ => throw new CalcError(ErrorKind.Internal, $"FloatArithmetic was given a {value.GetType().Name}."),
    };

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

    private static CalcError DivisionByZero(TextSpan at) => new(ErrorKind.DivideByZero, "Division by zero.", at);

    private static CalcError NotYet(string what, TextSpan at) =>
        new(ErrorKind.NotAvailable, $"{what} {(what.EndsWith('s') ? "aren't" : "isn't")} available yet.", at);
}
