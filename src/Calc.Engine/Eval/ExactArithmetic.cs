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
/// Integers and fractions stay exact until something irrational or too large forces a float: + − × ÷, negate, %, mod,
/// rem, integer powers, the bit operators, comparisons, and abs, sign, floor, ceil, trunc, round, max, min, fma and the
/// sqrt of a perfect square. Past a budget the float is marked <see cref="FloatValue.ExactTooLarge"/>: nothing is
/// refused. Flags come only from float operations, so exact work raises none.
/// </summary>
/// <remarks>
/// <para>Where a float comes in, or a budget runs out, the answer is still right for the exact values, rounded once, never
/// for the exact values rounded first (B8). Rounding first differs in about 30% of cases for 1/3 × sqrt(2) and its kin.</para>
/// <list type="bullet">
/// <item>× ÷ with a float: the exact side's parts times the float, exactly, then one division.</item>
/// <item>+ − with a float, sqrt, ^ on an exact base, and functions: <see cref="RoundedCall"/>, which brackets the exact
/// value.</item>
/// <item>mod, rem and fma with a float: the float taken as the fraction it is, within the budgets, then exact work.</item>
/// <item>Past a budget: the fraction unreduced, built and divided once, within the work budget; past that, RoundedCall.</item>
/// <item>max, min and comparisons: exact, a float against an exact value too.</item>
/// </list>
/// <para>Format targets never come here: they're IEEE, so they always use floats.</para>
/// </remarks>
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

    // + and -

    public Value Add(Value a, Value b, TextSpan at) => Sum(a, b, negate: false, at);

    public Value Subtract(Value a, Value b, TextSpan at) => Sum(a, b, negate: true, at);

    private Value Sum(Value a, Value b, bool negate, TextSpan at)
    {
        Value Floats(IReadOnlyList<Value> v) => negate ? _floats.Subtract(v[0], v[1], at) : _floats.Add(v[0], v[1], at);
        // With a float, RoundedCall always settles: an integer or a fraction over a power of two is exact at its width,
        // and any other fraction keeps the sum off every point where the rounding changes.
        if (Fraction(a) is not (ApInt n1, ApInt d1) || Fraction(b) is not (ApInt n2, ApInt d2))
            return RoundedCall.Of(_floats, [a, b], Floats);
        if (ExactSum(n1, d1, n2, d2, negate) is Value exact) return exact;
        // Past a budget: the fraction unreduced, built and divided once, if the work allows.
        Value Unreduced() => _floats.Quotient(SumNumerator(n1, d1, n2, d2, negate), d1 * d2, at);
        if (WithinWork(SumBits(n1, d1, n2, d2), d1.BitLength + d2.BitLength)) return TooLarge(Unreduced());
        // Past the work budget too: RoundedCall, and the fraction built anyway only if it can't settle, which means the
        // answer is exactly where the rounding changes. The same goes for × ÷ and fma below.
        return TooLarge(RoundedCall.Of(_floats, [a, b], Floats, unsettled: Unreduced));
    }

    /// <summary>n1/d1 ± n2/d2 exactly, or null past a budget.</summary>
    private Value? ExactSum(ApInt n1, ApInt d1, ApInt n2, ApInt d2, bool negate)
    {
        if (d1 == ApInt.One && d2 == ApInt.One)
            return Math.Max(n1.BitLength, n2.BitLength) + 1 <= _budgets.IntegerBits ? new IntValue(negate ? n1 - n2 : n1 + n2) : null;
        if (!FitsRatio(SumBits(n1, d1, n2, d2), d1.BitLength + d2.BitLength)) return null;
        return RatioValue.Of(SumNumerator(n1, d1, n2, d2, negate), d1 * d2);
    }

    private static long SumBits(ApInt n1, ApInt d1, ApInt n2, ApInt d2) =>
        Math.Max(n1.BitLength + d2.BitLength, n2.BitLength + d1.BitLength) + 1;

    private static ApInt SumNumerator(ApInt n1, ApInt d1, ApInt n2, ApInt d2, bool negate)
    {
        ApInt left = d2 == ApInt.One ? n1 : n1 * d2, right = d1 == ApInt.One ? n2 : n2 * d1;
        return negate ? left - right : left + right;
    }

    // × and ÷

    public Value Multiply(Value a, Value b, TextSpan at)
    {
        if (Fraction(a) is (ApInt n1, ApInt d1) && Fraction(b) is (ApInt n2, ApInt d2))
        {
            if (ExactProduct(n1, d1, n2, d2) is Value exact) return exact;
            Value Unreduced() => _floats.Quotient(n1 * n2, d1 * d2, at);
            if (WithinWork(n1.BitLength, n2.BitLength) && WithinWork(n1.BitLength + n2.BitLength, d1.BitLength + d2.BitLength))
                return TooLarge(Unreduced());
            return TooLarge(RoundedCall.Of(_floats, [a, b], v => _floats.Multiply(v[0], v[1], at), absolute: false, unsettled: Unreduced));
        }
        if (a is FloatValue && b is FloatValue) return _floats.Multiply(a, b, at);
        // One float: the exact side's numerator times it, exactly, then one division by its denominator. Right even
        // where the answer is exact, as 1/3 × 3.0 is, which RoundedCall can't settle on in a directed mode.
        var (n, d) = Fraction(a is FloatValue ? b : a)!.Value;
        ApFloat y = ((FloatValue)(a is FloatValue ? a : b)).Number;
        return _floats.Quotient(Times(n, y), d, at);
    }

    /// <summary>(n1/d1)·(n2/d2) exactly, or null past a budget.</summary>
    private Value? ExactProduct(ApInt n1, ApInt d1, ApInt n2, ApInt d2)
    {
        if (d1 == ApInt.One && d2 == ApInt.One)
        {
            long bitsX = n1.BitLength, bitsY = n2.BitLength;
            return bitsX + bitsY <= _budgets.IntegerBits && WithinWork(bitsX, bitsY) ? new IntValue(n1 * n2) : null;
        }
        return FitsRatio(n1.BitLength + n2.BitLength, d1.BitLength + d2.BitLength) ? RatioValue.Of(n1 * n2, d1 * d2) : null;
    }

    /// <summary>
    /// Exact division by zero is always DivideByZero, 0/0 too. Integer by integer tries DivRem first, so the ratio limit
    /// applies only to a fraction that remains: 10^6000/10 stays an integer.
    /// </summary>
    public Value Divide(Value a, Value b, TextSpan at)
    {
        if (IsExactZero(b) && a is IntValue or RatioValue) throw new CalcError(ErrorKind.DivideByZero, "Division by zero.", at);
        if (Fraction(a) is (ApInt n1, ApInt d1) && Fraction(b) is (ApInt n2, ApInt d2))
        {
            if (ExactQuotient(n1, d1, n2, d2) is Value exact) return exact;
            Value Unreduced() => _floats.Quotient(n1 * d2, d1 * n2, at);
            if (WithinWork(n1.BitLength, d2.BitLength) && WithinWork(d1.BitLength, n2.BitLength)
                && WithinWork(n1.BitLength + d2.BitLength, d1.BitLength + n2.BitLength))
                return TooLarge(Unreduced());
            return TooLarge(RoundedCall.Of(_floats, [a, b], v => _floats.Divide(v[0], v[1], at), absolute: false, unsettled: Unreduced));
        }
        if (a is FloatValue && b is FloatValue) return _floats.Divide(a, b, at);
        if (b is FloatValue { Number: var y })
        {
            // (n/d) ÷ y = n / (d·y). A zero y is DivideByZero, from Quotient.
            var (n, d) = Fraction(a)!.Value;
            return _floats.Quotient(n, Times(d, y), at);
        }
        // y ÷ (n/d) = (y·d) / n.
        var (bn, bd) = Fraction(b)!.Value;
        return _floats.Quotient(Times(bd, ((FloatValue)a).Number), bn, at);
    }

    /// <summary>(n1/d1)/(n2/d2) exactly, or null past a budget. n2 isn't zero.</summary>
    private Value? ExactQuotient(ApInt n1, ApInt d1, ApInt n2, ApInt d2)
    {
        if (d1 == ApInt.One && d2 == ApInt.One)
        {
            if (!WithinWork(n1.BitLength, n2.BitLength)) return null;
            var (quotient, remainder) = ApInt.DivRem(n1, n2);
            if (remainder.IsZero) return new IntValue(quotient);
            return FitsRatio(n1.BitLength, n2.BitLength) ? RatioValue.Of(n1, n2) : null;
        }
        return FitsRatio(n1.BitLength + d2.BitLength, d1.BitLength + n2.BitLength) ? RatioValue.Of(n1 * d2, d1 * n2) : null;
    }

    /// <summary>n·y exactly: ApFloat's product at the width both significands need, so nothing rounds.</summary>
    private static ApFloat Times(ApInt n, ApFloat y)
    {
        long width = n.IsZero || !y.IsFinite || y.IsZero ? 1 : n.BitLength + y.Significand.BitLength;
        return ApFloat.Multiply(n, y, (int)Math.Clamp(width, 1, ApFloat.MaxPrecision));
    }

    public Value Negate(Value a, TextSpan at) => a switch
    {
        IntValue x => new IntValue(-x.Number),
        RatioValue r => r.Negated(),
        _ => _floats.Negate(a, at),
    };

    /// <summary>÷100, exactly: 200 + 10% is 200.1, and 1/3% is 1/300.</summary>
    public Value Percent(Value a, TextSpan at) =>
        a is IntValue or RatioValue ? Divide(a, new IntValue(100), at) : _floats.Percent(a, at);

    // ^

    /// <summary>
    /// An exact base to an integer power stays exact within budget; a negative power gives a fraction. Base 0, 1 and -1
    /// are settled for any integer first, so 1^(10^30) is exact. A power of two is exempt from the work budget, since
    /// it's a shift: 2^1000000 takes under a millisecond. ^(1/2) is sqrt. Anything else with an exact base is the float
    /// power of that exact base, with y held as it is: 1.1^5000 at 53 bits is 9.19233389907065E+206, where rounding the
    /// base first gets 3 of 15 digits wrong.
    /// </summary>
    public Value Power(Value a, Value b, TextSpan at)
    {
        if (a is FloatValue) return _floats.Power(a, b, at);
        if (b is RatioValue half && half.Numerator == ApInt.One && half.Denominator == 2) return Sqrt(a, at);
        if (b is not IntValue { Number: var y }) return FloatPower(a, b, at);

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
        return TooLarge(FloatPower(a, b, at));
    }

    /// <summary>x^y in floats for an exact x, rounded once for the exact x, with y passed as it is.</summary>
    private Value FloatPower(Value a, Value b, TextSpan at)
    {
        long exponentBits = b switch
        {
            IntValue i => Math.Min(i.Number.BitLength, 64),
            FloatValue { Number: { IsFinite: true, IsZero: false } y } => Math.Clamp(y.Exponent + y.Significand.BitLength, 0, 64),
            _ => 0,
        };
        return RoundedCall.Of(_floats, [a], v => _floats.Power(v[0], b, at), absolute: false, extraBits: exponentBits);
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

    // sqrt

    /// <summary>
    /// A perfect square, or a fraction of two, stays exact; anything else is the float root of the exact value. A
    /// negative is Domain, as in floats.
    /// </summary>
    private Value Sqrt(Value a, TextSpan at)
    {
        if (Fraction(a) is not (ApInt n, ApInt d)) return _floats.Call("sqrt", [a], at);
        if (n.IsNegative) throw new CalcError(ErrorKind.Domain, "That has no answer.", at);
        var (top, topKnown) = SquareRoot(n);
        var (bottom, bottomKnown) = SquareRoot(d);
        if (top is ApInt rn && bottom is ApInt rd) return RatioValue.Of(rn, rd);
        Value root = RoundedCall.Of(_floats, [a], v => _floats.Call("sqrt", v, at), absolute: false);
        // A part known not to be a square makes the root irrational: a plain float. Not knowing, because telling would
        // cost too much, is the budget's doing.
        bool irrational = (topKnown && top is null) || (bottomKnown && bottom is null);
        return irrational ? root : TooLarge(root);
    }

    /// <summary>
    /// x's integer square root if x is a perfect square, with Known true; null with Known true if it isn't; null with
    /// Known false if telling would cost too much. The twos are settled from their count, so a power of two costs
    /// nothing; SqrtRem, which is quadratic, runs on the odd part only within the work budget: sqrt(2^1000000) took
    /// 11.8 s by SqrtRem alone.
    /// </summary>
    private (ApInt? Root, bool Known) SquareRoot(ApInt x)
    {
        if (x.IsZero) return (x, true);
        long twos = long.CreateChecked(ApInt.TrailingZeroCount(x));
        if ((twos & 1) != 0) return (null, true);
        ApInt odd = x >> (int)twos;
        if (!WithinWork(odd.BitLength, odd.BitLength)) return (null, false);
        var (root, remainder) = ApInt.SqrtRem(odd);
        return (remainder.IsZero ? root << (int)(twos / 2) : null, true);
    }

    // mod and rem

    /// <summary>Floored: the result takes the divisor's sign, so -7 mod 3 is 2, and 7/2 mod 1/3 is 1/6.</summary>
    public Value Mod(Value a, Value b, TextSpan at) => Remainder(a, b, floored: true, at);

    /// <summary>Truncated: the result takes the dividend's sign, so -7 rem 3 is -1.</summary>
    public Value Rem(Value a, Value b, TextSpan at) => Remainder(a, b, floored: false, at);

    private Value Remainder(Value a, Value b, bool floored, TextSpan at)
    {
        Value Floats(IReadOnlyList<Value> v) => floored ? _floats.Mod(v[0], v[1], at) : _floats.Rem(v[0], v[1], at);
        if (a is FloatValue && b is FloatValue) return Floats([a, b]);
        bool mixed = a is FloatValue || b is FloatValue;
        // A float meeting an exact value is taken as the fraction it is, if the budgets allow, so the answer is exact and
        // then rounded once. RoundedCall would do for most, but a remainder jumps where the quotient is whole, and lands
        // exactly on a value as 2.5 mod 2/3 does (1/2), which no bracket settles on in a directed mode.
        if (Fraction(AsExact(a)) is not (ApInt n1, ApInt d1) || Fraction(AsExact(b)) is not (ApInt n2, ApInt d2))
            return RoundedCall.Of(_floats, [a, b], Floats);
        if (n2.IsZero) throw new CalcError(ErrorKind.DivideByZero, "Division by zero.", at);

        // a/b = (n1·d2)/(n2·d1), so the remainder is ((n1·d2) rem (n2·d1)) / (d1·d2).
        long dividendBits = n1.BitLength + d2.BitLength, divisorBits = n2.BitLength + d1.BitLength;
        if (WithinWork(n1.BitLength, d2.BitLength) && WithinWork(n2.BitLength, d1.BitLength) && WithinWork(dividendBits, divisorBits))
            return Exactly();
        // Past the work budget, a float. Where the quotient is whole, the remainder jumps, and its answer, 0, is exactly
        // where no bracket settles: 2·10^20 mod 2/3 is one. Only then is the exact remainder worked out, whatever it costs.
        Value rounded = RoundedCall.Of(_floats, [a, b], Floats, unsettled: Exactly);
        return mixed ? rounded : TooLarge(rounded);

        Value Exactly()
        {
            ApInt dividend = d2 == ApInt.One ? n1 : n1 * d2, divisor = d1 == ApInt.One ? n2 : n2 * d1;
            ApInt r = ApInt.DivRem(dividend, divisor).Remainder;
            if (floored && !r.IsZero && r.IsNegative != divisor.IsNegative) r += divisor;

            ApInt denominator = d1 * d2;
            Value result;
            if (denominator == ApInt.One) result = new IntValue(r); // |r| < |divisor|: within size
            else if (FitsRatio(divisorBits, denominator.BitLength)) result = RatioValue.Of(r, denominator);
            else return mixed ? _floats.Quotient(r, denominator, at) : TooLarge(_floats.Quotient(r, denominator, at));
            return mixed ? _floats.Rounded(result, at) : result;
        }
    }

    // Bit operators: on integers, as two's complement of unlimited width, exactly. A fraction is Domain. With a float,
    // the integer goes to the float path as itself, at its own width, so no bits are lost before the operator.

    public Value And(Value a, Value b, TextSpan at) =>
        a is IntValue x && b is IntValue y ? new IntValue(x.Number & y.Number) : _floats.And(Whole(a, at), Whole(b, at), at);

    public Value Or(Value a, Value b, TextSpan at) =>
        a is IntValue x && b is IntValue y ? new IntValue(x.Number | y.Number) : _floats.Or(Whole(a, at), Whole(b, at), at);

    public Value Xor(Value a, Value b, TextSpan at) =>
        a is IntValue x && b is IntValue y ? new IntValue(x.Number ^ y.Number) : _floats.Xor(Whole(a, at), Whole(b, at), at);

    public Value Not(Value a, TextSpan at) => a is IntValue x ? new IntValue(~x.Number) : _floats.Not(Whole(a, at), at);

    /// <summary>floor(x·2^n), so a negative n shifts right.</summary>
    public Value ShiftLeft(Value a, Value b, TextSpan at) => Shift(a, b, left: true, at);

    /// <summary>floor(x·2^-n): an arithmetic shift, so -7 &gt;&gt; 1 is -4.</summary>
    public Value ShiftRight(Value a, Value b, TextSpan at) => Shift(a, b, left: false, at);

    private Value Shift(Value a, Value b, bool left, TextSpan at)
    {
        if (b is RatioValue) throw new CalcError(ErrorKind.Domain, "A shift needs a whole number of places.", at);
        if (a is not IntValue { Number: var x } || b is not IntValue { Number: var count })
            return Floats(Whole(a, at), b is IntValue c ? new FloatValue(c.Number) : b);
        ApInt places = left ? count : -count;
        if (x.IsZero) return a;
        if (places.IsNegative)
        {
            // Right: every bit can go, leaving 0, or -1 for a negative.
            if (-places >= x.BitLength) return new IntValue(x.IsNegative ? ApInt.MinusOne : ApInt.Zero);
            return new IntValue(x >> int.CreateChecked(-places));
        }
        // Left: within the size budget, else a float, which a shift makes exactly before its one rounding.
        if (places.BitLength < 63 && x.BitLength + long.CreateChecked(places) <= _budgets.IntegerBits)
            return new IntValue(x << int.CreateChecked(places));
        return TooLarge(Floats(Whole(a, at), new FloatValue(count)));

        Value Floats(Value value, Value by) => left ? _floats.ShiftLeft(value, by, at) : _floats.ShiftRight(value, by, at);
    }

    /// <summary>For the float path: an integer as itself, exactly; a float as it is. A fraction is no whole number.</summary>
    private static Value Whole(Value v, TextSpan at) => v switch
    {
        IntValue i => new FloatValue(i.Number),
        RatioValue => throw new CalcError(ErrorKind.Domain, "Bit operators need whole numbers.", at),
        _ => v,
    };

    public Value Factorial(Value a, TextSpan at) => _floats.Factorial(a, at);

    // Comparisons and functions

    /// <summary>Exactly, an exact value against a float too: n/d against y as n against y·d, since d &gt; 0.</summary>
    public int? Compare(Value a, Value b)
    {
        if (Fraction(a) is (ApInt n1, ApInt d1) && Fraction(b) is (ApInt n2, ApInt d2)) return ExactFunctions.Compare(n1, d1, n2, d2);
        if (a is FloatValue && b is FloatValue) return _floats.Compare(a, b);
        bool flipped = a is FloatValue;
        var (n, d) = Fraction(flipped ? b : a)!.Value;
        ApFloat y = ((FloatValue)(flipped ? a : b)).Number;
        if (y.IsNaN) return null;
        int c = Math.Sign(((ApFloat)n).CompareTo(Times(d, y)));
        return flipped ? -c : c;
    }

    /// <summary>
    /// abs, sign, floor, ceil, trunc and round of an exact value are exact, and max and min pick one of their arguments
    /// by exact comparison, as it is. sqrt and fma stay exact where they can. Anything else, as the W sessions'
    /// functions will be, is the float function, right for the exact argument (<see cref="RoundedCall"/>).
    /// </summary>
    public Value Call(string name, IReadOnlyList<Value> arguments, TextSpan at)
    {
        FunctionTable.Check(name, arguments.Count, at);
        if (arguments.All(v => v is FloatValue)) return _floats.Call(name, arguments, at);
        switch (name)
        {
            case "sqrt":
                return Sqrt(arguments[0], at);
            case "max" or "min":
                return Pick(name == "max" ? 1 : -1, name, arguments, at);
            case "fma":
                return Fma(arguments, at);
        }
        if (Fraction(arguments[0]) is (ApInt n, ApInt d))
        {
            switch (name)
            {
                case "abs": return arguments[0] is RatioValue r ? r.Abs() : new IntValue(ApInt.Abs(n));
                case "sign": return new IntValue(n.Sign);
                case "floor": return new IntValue(ExactFunctions.Floor(n, d));
                case "ceil": return new IntValue(ExactFunctions.Ceiling(n, d));
                case "trunc": return new IntValue(ExactFunctions.Truncate(n, d));
                case "round": return new IntValue(ExactFunctions.Round(n, d));
            }
        }
        return RoundedCall.Of(_floats, arguments, v => _floats.Call(name, v, at));
    }

    /// <summary>
    /// The largest (want 1) or smallest (want -1) argument, as it is: max(1/3, 0.2) is exactly 1/3. A tie between an
    /// exact value and a float goes to the exact one.
    /// </summary>
    private Value Pick(int want, string name, IReadOnlyList<Value> arguments, TextSpan at)
    {
        Value best = arguments[0];
        for (int i = 1; i < arguments.Count; i++)
        {
            // Unordered only with NaN, which a precision target never makes: IEEE's rule then, from the float path.
            if (Compare(arguments[i], best) is not int c) return RoundedCall.Of(_floats, arguments, v => _floats.Call(name, v, at));
            if (c == want || (c == 0 && best is FloatValue && arguments[i] is not FloatValue)) best = arguments[i];
        }
        return best;
    }

    /// <summary>
    /// a·b + c exactly within the budgets. With a float among them, each float is taken as the fraction it is, so the
    /// answer is rounded once, from the exact value.
    /// </summary>
    private Value Fma(IReadOnlyList<Value> arguments, TextSpan at)
    {
        Value Floats(IReadOnlyList<Value> v) => _floats.Call("fma", v, at);
        bool mixed = arguments.Any(v => v is FloatValue);
        if (Fraction(AsExact(arguments[0])) is not (ApInt n1, ApInt d1) || Fraction(AsExact(arguments[1])) is not (ApInt n2, ApInt d2)
            || Fraction(AsExact(arguments[2])) is not (ApInt n3, ApInt d3))
            return RoundedCall.Of(_floats, arguments, Floats);
        if (ExactProduct(n1, d1, n2, d2) is Value product && Fraction(product) is (ApInt pn, ApInt pd)
            && ExactSum(pn, pd, n3, d3, negate: false) is Value sum)
            return mixed ? _floats.Rounded(sum, at) : sum;

        // Past a budget: as one fraction, (n1·n2·d3 + n3·d1·d2) / (d1·d2·d3), built and divided once if the work allows.
        long numeratorBits = Math.Max(n1.BitLength + n2.BitLength + d3.BitLength, n3.BitLength + d1.BitLength + d2.BitLength) + 1;
        long denominatorBits = d1.BitLength + d2.BitLength + d3.BitLength;
        Value Unreduced() => _floats.Quotient(n1 * n2 * d3 + n3 * d1 * d2, d1 * d2 * d3, at);
        Value result = WithinWork(n1.BitLength, n2.BitLength) && WithinWork(numeratorBits, denominatorBits)
            ? Unreduced()
            : RoundedCall.Of(_floats, arguments, Floats, unsettled: Unreduced);
        return mixed ? result : TooLarge(result);
    }

    /// <summary>A value as an exact fraction: itself if exact; a float as the fraction it is, if the budgets allow.</summary>
    private Value? AsExact(Value v)
    {
        if (v is not FloatValue { Number: var x }) return v;
        if (!x.IsFinite) return null;
        if (x.IsZero) return new IntValue(ApInt.Zero);
        ApInt m = x.Significand;
        return Scaled(x.IsNegative ? -m : m, x.Exponent);
    }

    private static (ApInt Numerator, ApInt Denominator)? Fraction(Value? v) => v switch
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
