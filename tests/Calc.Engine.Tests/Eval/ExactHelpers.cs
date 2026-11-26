using System.Globalization;
using System.Numerics;
using Calc.Engine.Values;
using Natural;

namespace Calc.Engine.Tests.Eval;

/// <summary>Exact fractions for B8's tests: values to <see cref="Rational"/> and back, and the rounding functions, by BigInteger.</summary>
internal static class ExactHelpers
{
    public static readonly RoundingMode[] Modes =
        [RoundingMode.ToNearestEven, RoundingMode.TowardZero, RoundingMode.TowardPositive, RoundingMode.TowardNegative];

    public static BigInteger Big(ApInt n) => BigInteger.Parse(n.ToString(), CultureInfo.InvariantCulture);

    public static ApInt Ap(BigInteger n) => ApInt.Parse(n.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Any value's exact worth: a float's too.</summary>
    public static Rational Q(Value v) => v switch
    {
        IntValue i => new Rational(Big(i.Number), 1),
        RatioValue r => new Rational(Big(r.Numerator), Big(r.Denominator)),
        FloatValue f => RationalOracle.ToRational(f.Number),
        _ => throw new ArgumentException(v.GetType().Name),
    };

    /// <summary>The exact value for a fraction, as the engine holds it: an integer when whole.</summary>
    public static Value Exact(Rational q) => RatioValue.Of(Ap(q.Num), Ap(q.Den));

    public static int Compare(Rational a, Rational b) => (a.Num * b.Den).CompareTo(b.Num * a.Den);

    public static Rational Floor(Rational q)
    {
        var whole = BigInteger.DivRem(q.Num, q.Den, out var remainder);
        return Rational.Of(remainder.Sign < 0 ? whole - 1 : whole, 1);
    }

    public static Rational Truncate(Rational q) => Rational.Of(BigInteger.Divide(q.Num, q.Den), 1);

    public static Rational RoundHalfEven(Rational q)
    {
        Rational down = Floor(q), half = new(1, 2);
        int c = Compare(q - down, half);
        return c < 0 ? down : c > 0 ? down + new Rational(1, 1) : (down.Num.IsEven ? down : down + new Rational(1, 1));
    }

    public static Rational Round(Rational q, int p, RoundingMode mode)
    {
        bool ignored = false;
        return RationalOracle.Round(q, p, mode, ref ignored);
    }

    /// <summary>A random fraction, sometimes whole, sometimes negative, with parts up to the bits given.</summary>
    public static Rational RandomFraction(Random random, int bits = 60)
    {
        BigInteger n = RandomBig(random, random.Next(1, bits + 1)), d = random.Next(4) == 0 ? 1 : RandomBig(random, random.Next(1, bits + 1));
        if (n.IsZero) n = 1;
        if (d.IsZero) d = 3;
        return Rational.Of(random.Next(2) == 0 ? n : -n, d);
    }

    /// <summary>A random p-bit float, never zero, its exponent between -80 and 40.</summary>
    public static FloatValue RandomFloat(Random random, int p)
    {
        BigInteger m = RandomBig(random, random.Next(1, p + 1));
        if (m.IsZero) m = 1;
        if (random.Next(2) == 0) m = -m;
        return new FloatValue(new ApFloat(Ap(m), random.Next(-80, 41), p));
    }

    public static BigInteger RandomBig(Random random, int bits)
    {
        var bytes = new byte[bits / 8 + 2];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        return new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
    }
}
