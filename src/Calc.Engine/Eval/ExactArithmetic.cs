using System.Globalization;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Eval;

/// <summary>
/// How far exact values go before carrying on as floats. Both are settings, and both are checked from bit lengths before
/// anything is computed.
/// </summary>
/// <param name="IntegerBits">No integer past this: 2^24 bits, about 5 million digits.</param>
/// <param name="RatioBits">No numerator or denominator past this: 2^14 bits, since GCD there takes about 100 ms.</param>
/// <param name="MultiplyWork">bits(a)·bits(b) for one multiply, at most: 2^32, about 1 s today (Matthew, 2026-11-27:
/// a float at once past it). A power is checked on its last squaring. D3 raises it once multiply is faster.</param>
public sealed record ExactBudgets(long IntegerBits = 1L << 24, long RatioBits = 1L << 14, double MultiplyWork = 4294967296.0)
{
    public static readonly ExactBudgets Default = new();
}

/// <summary>
/// Integers and fractions stay exact until something irrational or too large forces a float. + − × ÷, negate, % and
/// integer powers are exact here; everything else, and anything past a budget, goes to <see cref="FloatArithmetic"/>,
/// which converts exact operands at the target. Past a budget the float is marked <see cref="FloatValue.ExactTooLarge"/>:
/// nothing is refused. Flags come only from those conversions and float operations, so exact work raises none.
/// </summary>
/// <remarks>Format targets never come here: they're IEEE, so they always use floats.</remarks>
public sealed class ExactArithmetic : IArithmetic
{
    private readonly FloatArithmetic _floats;
    private readonly ExactBudgets _budgets;

    public ExactArithmetic(Target target, RoundingMode mode, ExactBudgets? budgets = null)
    {
        if (target.IsFormat) throw new ArgumentException("A format target always uses floats.", nameof(target));
        _floats = new FloatArithmetic(target, mode);
        _budgets = budgets ?? ExactBudgets.Default;
    }

    public IeeeFlags Flags => _floats.Flags;

    /// <summary>
    /// Whole numbers in any radix as integers; decimals as digits/10^k; hex and binary fractions as dyadic fractions. Past
    /// a budget, a float, by the float path's single rounding: 1e1000000 is a float at once, where building it exactly
    /// would take minutes.
    /// </summary>
    public Value Literal(string text, TextSpan at) => ExactLiteral(text.Replace("_", "")) ?? TooLarge(_floats.Literal(text, at));

    private Value? ExactLiteral(string s)
    {
        if (s.Length > 2 && s[0] == '0' && char.ToLowerInvariant(s[1]) is 'x' or 'b' or 'o')
        {
            int bitsPerDigit = char.ToLowerInvariant(s[1]) switch { 'x' => 4, 'b' => 1, _ => 3 };
            string body = s[2..];
            long exponent = 0;
            int p = body.IndexOfAny(['p', 'P']);
            if (p >= 0)
            {
                // Lexed as decimal digits after a sign: a long can still overflow, which is past any budget anyway.
                if (!long.TryParse(body[(p + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return null;
                body = body[..p];
            }
            int point = body.IndexOf('.');
            string digits = point < 0 ? body : body.Remove(point, 1);
            long fractionDigits = point < 0 ? 0 : body.Length - point - 1;
            if ((long)digits.Length * bitsPerDigit > _budgets.IntegerBits) return null;
            ApInt m = WholeDigits(digits, bitsPerDigit);
            // m · 2^shift, a power of two, so exempt from the work budget, but not from the size budget.
            long shift;
            try { shift = checked(exponent - fractionDigits * bitsPerDigit); }
            catch (OverflowException) { return null; }
            return Scaled(m, shift);
        }
        else
        {
            long exponent = 0;
            int e = s.IndexOfAny(['e', 'E']);
            if (e >= 0)
            {
                if (!long.TryParse(s[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return null;
                s = s[..e];
            }
            int point = s.IndexOf('.');
            string digits = point < 0 ? s : s.Remove(point, 1);
            long fractionDigits = point < 0 ? 0 : s.Length - point - 1;
            // A decimal digit is log2(10) = 3.32 bits; the estimates round up.
            if (DecimalBits(digits.Length) > _budgets.IntegerBits) return null;
            long scale;
            try { scale = checked(exponent - fractionDigits); }
            catch (OverflowException) { return null; }
            ApInt d = digits.Length == 0 ? ApInt.Zero : ApInt.Parse(digits, CultureInfo.InvariantCulture);
            if (d.IsZero) return new IntValue(ApInt.Zero);
            if (scale >= 0)
            {
                // d·10^scale: the power's last squaring, and the final multiply, within the work budget; the result within size.
                long powerBits = DecimalBits(scale);
                if (powerBits > _budgets.IntegerBits || d.BitLength + powerBits > _budgets.IntegerBits) return null;
                if (!WithinWork(powerBits / 2 + 1, powerBits / 2 + 1) || !WithinWork(d.BitLength, powerBits)) return null;
                return new IntValue(d * ApInt.Pow(10, (int)scale));
            }
            long denominatorBits = DecimalBits(-scale);
            if (d.BitLength > _budgets.RatioBits || denominatorBits > _budgets.RatioBits) return null;
            return RatioValue.Of(d, ApInt.Pow(10, (int)-scale));
        }
    }

    private static ApInt WholeDigits(string digits, int bitsPerDigit)
    {
        if (bitsPerDigit != 3) return digits.Length == 0 ? ApInt.Zero : ApInt.Parse((bitsPerDigit == 4 ? "0x" : "0b") + digits, CultureInfo.InvariantCulture);
        ApInt n = ApInt.Zero;
        foreach (char c in digits) n = n * 8 + (c - '0');
        return n;
    }

    /// <summary>m·2^shift exactly, as an integer or a dyadic fraction, if within the size budget.</summary>
    private Value? Scaled(ApInt m, long shift)
    {
        if (m.IsZero) return new IntValue(ApInt.Zero);
        if (shift >= 0)
            return m.BitLength + shift <= _budgets.IntegerBits ? new IntValue(m << (int)shift) : null;
        // Cancel twos first, so the size check sees the fraction as it will be.
        long twos = Math.Min(-shift, long.CreateChecked(ApInt.TrailingZeroCount(ApInt.Abs(m))));
        ApInt numerator = m >> (int)twos;
        long denominatorExponent = -shift - twos;
        if (denominatorExponent == 0) return new IntValue(numerator);
        if (numerator.BitLength > _budgets.RatioBits || denominatorExponent + 1 > _budgets.RatioBits) return null;
        return RatioValue.Of(numerator, ApInt.One << (int)denominatorExponent);
    }

    /// <summary>An upper bound on the bits of a number with that many decimal digits: n·log2(10), rounded up.</summary>
    private static long DecimalBits(long digits) => digits <= 0 ? 0 : (long)Math.Ceiling(digits * 3.3219280948873623) + 1;

    public Value Import(Value value, TextSpan at) => value is FloatValue ? _floats.Import(value, at) : value;

    public Value Add(Value a, Value b, TextSpan at) => Sum(a, b, negateB: false, at);

    public Value Subtract(Value a, Value b, TextSpan at) => Sum(a, b, negateB: true, at);

    private Value Sum(Value a, Value b, bool negateB, TextSpan at)
    {
        if (a is IntValue x && b is IntValue y)
        {
            if (Math.Max(x.Number.BitLength, y.Number.BitLength) + 1 <= _budgets.IntegerBits)
                return new IntValue(negateB ? x.Number - y.Number : x.Number + y.Number);
        }
        else if (Fraction(a) is (ApInt n1, ApInt d1) && Fraction(b) is (ApInt n2, ApInt d2))
        {
            long numeratorBits = Math.Max(n1.BitLength + d2.BitLength, n2.BitLength + d1.BitLength) + 1;
            if (FitsRatio(numeratorBits, d1.BitLength + d2.BitLength))
            {
                ApInt cross = n2 * d1;
                return RatioValue.Of(n1 * d2 + (negateB ? -cross : cross), d1 * d2);
            }
        }
        else
        {
            return negateB ? _floats.Subtract(a, b, at) : _floats.Add(a, b, at);
        }
        return TooLarge(negateB ? _floats.Subtract(a, b, at) : _floats.Add(a, b, at));
    }

    public Value Multiply(Value a, Value b, TextSpan at)
    {
        if (a is IntValue x && b is IntValue y)
        {
            long bitsX = x.Number.BitLength, bitsY = y.Number.BitLength;
            if (bitsX + bitsY <= _budgets.IntegerBits && WithinWork(bitsX, bitsY)) return new IntValue(x.Number * y.Number);
        }
        else if (Fraction(a) is (ApInt n1, ApInt d1) && Fraction(b) is (ApInt n2, ApInt d2))
        {
            if (FitsRatio(n1.BitLength + n2.BitLength, d1.BitLength + d2.BitLength)) return RatioValue.Of(n1 * n2, d1 * d2);
        }
        else
        {
            return _floats.Multiply(a, b, at);
        }
        return TooLarge(_floats.Multiply(a, b, at));
    }

    /// <summary>
    /// Exact division by zero is always DivideByZero, 0/0 too. Integer by integer tries DivRem first, so the ratio limit
    /// applies only to a fraction that remains: 10^6000/10 stays an integer.
    /// </summary>
    public Value Divide(Value a, Value b, TextSpan at)
    {
        if (IsExactZero(b) && a is IntValue or RatioValue) throw new CalcError(ErrorKind.DivideByZero, "Division by zero.", at);
        if (a is IntValue x && b is IntValue y)
        {
            if (WithinWork(x.Number.BitLength, y.Number.BitLength))
            {
                var (quotient, remainder) = ApInt.DivRem(x.Number, y.Number);
                if (remainder.IsZero) return new IntValue(quotient);
                if (FitsRatio(x.Number.BitLength, y.Number.BitLength)) return RatioValue.Of(x.Number, y.Number);
            }
        }
        else if (Fraction(a) is (ApInt n1, ApInt d1) && Fraction(b) is (ApInt n2, ApInt d2))
        {
            if (FitsRatio(n1.BitLength + d2.BitLength, d1.BitLength + n2.BitLength)) return RatioValue.Of(n1 * d2, d1 * n2);
        }
        else
        {
            return _floats.Divide(a, b, at);
        }
        return TooLarge(_floats.Divide(a, b, at));
    }

    public Value Negate(Value a, TextSpan at) => a switch
    {
        IntValue x => new IntValue(-x.Number),
        RatioValue r => RatioValue.Of(-r.Numerator, r.Denominator),
        _ => _floats.Negate(a, at),
    };

    /// <summary>÷100, exactly: 200 + 10% is 200.1, and 1/3% is 1/300.</summary>
    public Value Percent(Value a, TextSpan at) =>
        a is IntValue or RatioValue ? Divide(a, new IntValue(100), at) : _floats.Percent(a, at);

    /// <summary>
    /// An exact base to an integer power stays exact within budget; a negative power gives a fraction. Base 0, 1 and -1
    /// are settled for any integer first, so 1^(10^30) is exact. A power of two is exempt from the work budget, since
    /// it's a shift: 2^1000000 takes under a millisecond. Anything else goes to the float path, with y passed exactly.
    /// </summary>
    public Value Power(Value a, Value b, TextSpan at)
    {
        if (b is not IntValue { Number: var y } || a is not (IntValue or RatioValue)) return _floats.Power(a, b, at);

        var (n, d) = Fraction(a)!.Value;
        bool odd = !(y & ApInt.One).IsZero;
        if (n.IsZero)
        {
            if (y.IsZero) return new IntValue(ApInt.One);
            if (y.IsNegative) throw new CalcError(ErrorKind.DivideByZero, "Division by zero.", at);
            return new IntValue(ApInt.Zero);
        }
        if (d == ApInt.One && ApInt.Abs(n) == ApInt.One) return new IntValue(n.IsNegative && odd ? ApInt.MinusOne : ApInt.One);
        if (y.IsZero) return new IntValue(ApInt.One);

        if (ApInt.Abs(y).BitLength <= 31)
        {
            int k = int.CreateChecked(ApInt.Abs(y));
            if (Raised(n, d, k) is var (pn, pd))
                return y.IsNegative ? RatioValue.Of(pn.IsNegative ? -pd : pd, ApInt.Abs(pn)) : RatioValue.Of(pn, pd);
        }
        return TooLarge(_floats.Power(a, b, at));
    }

    /// <summary>(n/d)^k exactly if the budgets allow, both parts positive powers; checked before computing.</summary>
    private (ApInt Numerator, ApInt Denominator)? Raised(ApInt n, ApInt d, int k)
    {
        ApInt magnitude = ApInt.Abs(n);
        bool negative = n.IsNegative && (k & 1) == 1;
        long numeratorBits = PowerBits(magnitude, k), denominatorBits = PowerBits(d, k);
        if (d == ApInt.One)
        {
            if (IsPowerOfTwo(magnitude))
            {
                // A shift: only the size budget applies.
                long exponent = checked((magnitude.BitLength - 1) * (long)k);
                if (exponent + 1 > _budgets.IntegerBits) return null;
                ApInt power = ApInt.One << (int)exponent;
                return (negative ? -power : power, ApInt.One);
            }
            if (numeratorBits > _budgets.IntegerBits || !WithinWork(numeratorBits / 2 + 1, numeratorBits / 2 + 1)) return null;
        }
        else if (!FitsRatio(numeratorBits, denominatorBits))
        {
            return null;
        }
        ApInt top = ApInt.Pow(magnitude, k);
        return (negative ? -top : top, d == ApInt.One ? ApInt.One : ApInt.Pow(d, k));
    }

    private static bool IsPowerOfTwo(ApInt magnitude) => (magnitude & (magnitude - ApInt.One)).IsZero;

    /// <summary>
    /// An upper bound on the bits of m^k: k·log2(m) + 1, with log2 from m's top 64 bits and a hair of margin. bitlen(m)·k
    /// alone counts 3 as 2 bits, not 1.585, and would send 3^80000, 1.5 s and well within budget, to a float. A budget
    /// estimate, not a value, so a double is fine here (rule 3 is about values).
    /// </summary>
    private static long PowerBits(ApInt magnitude, long k)
    {
        if (magnitude <= ApInt.One) return 1;
        int drop = (int)Math.Max(0, magnitude.BitLength - 64);
        double log2 = Math.Log2(ulong.CreateChecked(magnitude >> drop)) + drop;
        return (long)Math.Ceiling(k * log2 * (1 + 1e-12)) + 1;
    }

    // Not exact here: B8 makes these honest. They go to floats, which convert exact operands at the target.
    public Value Mod(Value a, Value b, TextSpan at) => _floats.Mod(a, b, at);
    public Value Rem(Value a, Value b, TextSpan at) => _floats.Rem(a, b, at);
    public Value Factorial(Value a, TextSpan at) => _floats.Factorial(a, at);
    public Value And(Value a, Value b, TextSpan at) => _floats.And(a, b, at);
    public Value Or(Value a, Value b, TextSpan at) => _floats.Or(a, b, at);
    public Value Xor(Value a, Value b, TextSpan at) => _floats.Xor(a, b, at);
    public Value ShiftLeft(Value a, Value b, TextSpan at) => _floats.ShiftLeft(a, b, at);
    public Value ShiftRight(Value a, Value b, TextSpan at) => _floats.ShiftRight(a, b, at);
    public Value Not(Value a, TextSpan at) => _floats.Not(a, at);
    public int? Compare(Value a, Value b) => _floats.Compare(a, b);
    public Value Call(string name, IReadOnlyList<Value> arguments, TextSpan at) => _floats.Call(name, arguments, at);

    private static (ApInt Numerator, ApInt Denominator)? Fraction(Value v) => v switch
    {
        IntValue i => (i.Number, ApInt.One),
        RatioValue r => (r.Numerator, r.Denominator),
        _ => null,
    };

    private static bool IsExactZero(Value v) => v is IntValue { Number.IsZero: true };

    private bool WithinWork(long bitsA, long bitsB) => (double)bitsA * bitsB <= _budgets.MultiplyWork;

    private bool FitsRatio(long numeratorBits, long denominatorBits) =>
        numeratorBits <= _budgets.RatioBits && denominatorBits <= _budgets.RatioBits;

    /// <summary>The float that stands in for an exact answer past a budget, chipped so.</summary>
    private static Value TooLarge(Value floatResult) =>
        floatResult is FloatValue f ? f with { ExactTooLarge = true } : floatResult;
}
