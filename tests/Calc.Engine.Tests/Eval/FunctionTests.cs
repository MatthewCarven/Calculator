using System.Globalization;
using System.Numerics;
using Calc.Engine.Eval;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Tests.Eval;

public class FunctionTests
{
    private static readonly RoundingMode[] Modes = [RoundingMode.ToNearestEven, RoundingMode.TowardZero, RoundingMode.TowardPositive, RoundingMode.TowardNegative];

    private static double D(Value v) => (double)((FloatValue)v).Number;
    private static float S(Value v) => (float)((FloatValue)v).Number;

    private static bool SameBits(double a, double b) => double.IsNaN(a) ? double.IsNaN(b) : BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    private static bool SameBits(float a, float b) => float.IsNaN(a) ? float.IsNaN(b) : BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static IEnumerable<double> Doubles(int seed, int count)
    {
        double[] specials = [0, -0.0, 1, -1, 0.5, -0.5, 1.5, -1.5, 2.5, -2.5, 3.5, 1e300, -1e300, 5e-324, -5e-324, 2.2250738585072014e-308,
            double.MaxValue, double.Epsilon, double.PositiveInfinity, double.NegativeInfinity, double.NaN, 4503599627370495.5, 9007199254740993];
        foreach (var s in specials) yield return s;
        var random = new Random(seed);
        for (int i = 0; i < count; i++)
        {
            double d = BitConverter.Int64BitsToDouble(random.NextInt64());
            yield return double.IsNaN(d) ? random.NextDouble() * 1000 - 500 : random.Next(3) == 0 ? Math.Round(d % 1e6, random.Next(4)) : d;
        }
    }

    [Fact]
    public void OneArgumentFunctionsMatchMathAtBinary64()
    {
        var floats = new FloatArithmetic(Target.Of(IeeeFormat.Binary64), RoundingMode.ToNearestEven);
        (string Name, Func<double, double> Expected)[] functions =
        [
            ("sqrt", Math.Sqrt), ("floor", Math.Floor), ("ceil", Math.Ceiling), ("trunc", Math.Truncate), ("round", Math.Round),
            ("abs", Math.Abs), ("sign", d => d == 0 || double.IsNaN(d) ? d : Math.Sign(d)),
        ];
        foreach (double d in Doubles(1, 5_000))
            foreach (var (name, expected) in functions)
            {
                double actual = D(floats.Call(name, [new FloatValue(d)], default));
                if (!SameBits(expected(d), actual)) Assert.Fail($"{name}({d:R}): Math gives {expected(d):R}, Calc {actual:R}");
            }
    }

    [Fact]
    public void MaxMinAndFmaMatchMathAtBinary64()
    {
        var floats = new FloatArithmetic(Target.Of(IeeeFormat.Binary64), RoundingMode.ToNearestEven);
        double[] all = Doubles(2, 400).ToArray();
        var random = new Random(3);
        for (int i = 0; i < 20_000; i++)
        {
            double a = all[random.Next(all.Length)], b = all[random.Next(all.Length)], c = all[random.Next(all.Length)];
            Check("max", Math.Max(a, b), D(floats.Call("max", [new FloatValue(a), new FloatValue(b)], default)), a, b);
            Check("min", Math.Min(a, b), D(floats.Call("min", [new FloatValue(a), new FloatValue(b)], default)), a, b);
            Check("fma", Math.FusedMultiplyAdd(a, b, c), D(floats.Call("fma", [new FloatValue(a), new FloatValue(b), new FloatValue(c)], default)), a, b, c);
        }
        Assert.Equal(7, D(floats.Call("max", [new FloatValue(3), new FloatValue(7), new FloatValue(-1)], default)));
        Assert.Equal(-1, D(floats.Call("min", [new FloatValue(3), new FloatValue(7), new FloatValue(-1)], default)));

        static void Check(string name, double expected, double actual, params double[] args)
        {
            if (!SameBits(expected, actual)) Assert.Fail($"{name}({string.Join(", ", args.Select(a => a.ToString("R")))}): Math gives {expected:R}, Calc {actual:R}");
        }
    }

    [Fact]
    public void SqrtAndFmaMatchMathFAtBinary32()
    {
        var floats = new FloatArithmetic(Target.Of(IeeeFormat.Binary32), RoundingMode.ToNearestEven);
        var random = new Random(4);
        for (int i = 0; i < 20_000; i++)
        {
            float a = BitConverter.Int32BitsToSingle(random.Next() ^ (random.Next(2) << 31)), b = (float)(random.NextDouble() * 4 - 2), c = (float)(random.NextDouble() * 1e6);
            float sqrt = S(floats.Call("sqrt", [new FloatValue(a)], default));
            if (!SameBits(MathF.Sqrt(a), sqrt)) Assert.Fail($"sqrt({a:R}): MathF gives {MathF.Sqrt(a):R}, Calc {sqrt:R}");
            float fma = S(floats.Call("fma", [new FloatValue(a), new FloatValue(b), new FloatValue(c)], default));
            if (!SameBits(MathF.FusedMultiplyAdd(a, b, c), fma)) Assert.Fail($"fma({a:R}, {b:R}, {c:R}): MathF gives {MathF.FusedMultiplyAdd(a, b, c):R}, Calc {fma:R}");
        }
    }

    [Fact]
    public void SqrtIsCorrectlyRoundedAtAnyPrecisionInEveryMode()
    {
        // The roots rounded down and up must bracket √x exactly, sit one unit apart unless exact, and the nearest must be
        // the closer: all checked by squaring, in exact fractions.
        var random = new Random(5);
        for (int i = 0; i < 2_000; i++)
        {
            int p = random.Next(2, 300);
            string literal = random.Next(2) == 0 ? random.Next(1, 1_000_000).ToString(CultureInfo.InvariantCulture) : (random.NextDouble() * Math.Pow(10, random.Next(-30, 30))).ToString("R", CultureInfo.InvariantCulture);
            // One operand for all four modes, so they take the root of the same number.
            Value operand = new FloatArithmetic(Target.Bits(p), RoundingMode.ToNearestEven).Literal(literal, default);
            Rational x = RationalOracle.ToRational(((FloatValue)operand).Number);
            Rational Root(RoundingMode mode) =>
                RationalOracle.ToRational(((FloatValue)new FloatArithmetic(Target.Bits(p), mode).Call("sqrt", [operand], default)).Number);
            Rational down = Root(RoundingMode.TowardNegative), up = Root(RoundingMode.TowardPositive), zero = Root(RoundingMode.TowardZero), near = Root(RoundingMode.ToNearestEven);
            string what = $"sqrt({literal}) at {p} bits";
            Assert.True(Compare(down * down, x) <= 0 && Compare(up * up, x) >= 0, $"{what}: {down} and {up} don't bracket it");
            Assert.True(zero == down, $"{what}: toward zero gave {zero}, down {down}");
            if (down == up) Assert.True(down * down == x, $"{what}: equal ends, but not exact");
            else Assert.True(up - down == Ulp(down, p), $"{what}: the ends are more than a unit apart");
            Rational mid = (down + up) * new Rational(1, 2);
            Assert.True(near == (Compare(mid * mid, x) > 0 ? down : up) || down == up, $"{what}: nearest gave {near}");
        }
    }

    private static int Compare(Rational a, Rational b) => (a.Num * b.Den).CompareTo(b.Num * a.Den);

    /// <summary>The gap above a positive p-bit value r.</summary>
    private static Rational Ulp(Rational r, int p)
    {
        long top = (long)r.Num.GetBitLength() - (long)r.Den.GetBitLength();
        if (Compare(Rational.Pow2(top), r) > 0) top--;
        return Rational.Pow2(top - p + 1);
    }

    [Fact]
    public void ModRemPercentAndTheRoundingFunctionsMatchTheRationalOracle()
    {
        var random = new Random(6);
        for (int i = 0; i < 5_000; i++)
        {
            int p = random.Next(2, 200);
            RoundingMode mode = Modes[random.Next(4)];
            var floats = new FloatArithmetic(Target.Bits(p), mode);
            string a = Literal(random), b = Literal(random), c = Literal(random);
            Value va = floats.Literal(a, default), vb = floats.Literal(b, default), vc = floats.Literal(c, default);
            Rational x = Exact(va), y = Exact(vb), z = Exact(vc);
            bool ignored = false;
            Rational R(Rational q) => RationalOracle.Round(q, p, mode, ref ignored);
            string what = $"({a}, {b}, {c}) at {p} bits, {mode}";

            if (!y.IsZero)
            {
                Rational q = x / y;
                Expect("mod", R(x - y * Floor(q)), floats.Mod(va, vb, default));
                Expect("rem", R(x - y * Truncate(q)), floats.Rem(va, vb, default));
            }
            Expect("%", R(x / new Rational(100, 1)), floats.Percent(va, default));
            Expect("floor", Floor(x), floats.Call("floor", [va], default));
            Expect("ceil", -Floor(-x), floats.Call("ceil", [va], default));
            Expect("trunc", Truncate(x), floats.Call("trunc", [va], default));
            Expect("round", RoundHalfEven(x), floats.Call("round", [va], default));
            Expect("abs", x.Num.Sign < 0 ? -x : x, floats.Call("abs", [va], default));
            Expect("fma", R(x * y + z), floats.Call("fma", [va, vb, vc], default));

            void Expect(string name, Rational expected, Value actual)
            {
                Rational got = Exact(actual);
                if (got != expected) Assert.Fail($"{name}{what}: expected {expected}, got {got}");
            }
        }
    }

    private static string Literal(Random random) => random.Next(4) switch
    {
        0 => (random.Next(-100, 100)).ToString(CultureInfo.InvariantCulture),
        1 => (random.NextDouble() * 200 - 100).ToString("R", CultureInfo.InvariantCulture),
        2 => (random.NextDouble() * 1e-20).ToString("R", CultureInfo.InvariantCulture),
        _ => (random.NextDouble() * 1e25 - 5e24).ToString("R", CultureInfo.InvariantCulture),
    };

    private static Rational Exact(Value v) => RationalOracle.ToRational(((FloatValue)v).Number);

    private static Rational Floor(Rational q)
    {
        var whole = BigInteger.DivRem(q.Num, q.Den, out var remainder);
        return Rational.Of(remainder.Sign < 0 ? whole - 1 : whole, 1);
    }

    private static Rational Truncate(Rational q) => Rational.Of(BigInteger.Divide(q.Num, q.Den), 1);

    private static Rational RoundHalfEven(Rational q)
    {
        Rational down = Floor(q), half = new(1, 2);
        int c = Compare(q - down, half);
        return c < 0 ? down : c > 0 ? down + new Rational(1, 1) : (down.Num.IsEven ? down : down + new Rational(1, 1));
    }

    [Theory]
    [InlineData("-7 mod 3", "2")]
    [InlineData("7 mod -3", "-2")]
    [InlineData("-7 rem 3", "-1")]
    [InlineData("7 rem -3", "1")]
    [InlineData("5.5 mod 2", "1.5")]
    [InlineData("-6 mod 3", "0")]
    [InlineData("50%", "0.5")]
    [InlineData("200 + 10%", "200.1")]
    [InlineData("-1e-30 mod 1", "1")]
    [InlineData("sign(-0.5)", "-1")]
    [InlineData("round(2.5) + round(3.5)", "6")]
    public void OperatorsAndFunctionsOnTheirOwn(string source, string expected) =>
        Assert.Equal(expected, Evaluator.Run(source, Target.Bits(53), RoundingMode.ToNearestEven).Value.ToString());

    [Fact]
    public void AZeroFromModTakesTheDivisorsSign()
    {
        var positive = ((FloatValue)Evaluator.Run("-6 mod 3", Target.Bits(53), RoundingMode.ToNearestEven).Value).Number;
        var negative = ((FloatValue)Evaluator.Run("6 mod -3", Target.Bits(53), RoundingMode.ToNearestEven).Value).Number;
        Assert.True(positive.IsZero && !positive.IsNegative);
        Assert.True(negative.IsZero && negative.IsNegative);
    }

    [Theory]
    [InlineData("0/0 mod 2", double.NaN)]
    [InlineData("5 mod (1/0)", 5)]
    [InlineData("5 mod 0", double.NaN)]
    [InlineData("5 rem 0", double.NaN)]
    [InlineData("5 mod 1e400", 5)]
    [InlineData("-5 mod (1/0)", double.PositiveInfinity)]
    [InlineData("sqrt(-1)", double.NaN)]
    public void AtAFormatTheyFollowIeee(string source, double expected)
    {
        double actual = (double)((FloatValue)Evaluator.Run(source, Target.Of(IeeeFormat.Binary64), RoundingMode.ToNearestEven).Value).Number;
        Assert.True(SameBits(expected, actual), $"{source}: expected {expected:R}, got {actual:R}");
    }

    [Theory]
    [InlineData("7 mod 0", ErrorKind.DivideByZero, 2, 5)]
    [InlineData("7 rem 0", ErrorKind.DivideByZero, 2, 5)]
    [InlineData("sqrt(-1)", ErrorKind.Domain, 0, 8)]
    [InlineData("√-4", ErrorKind.Domain, 0, 1)]
    [InlineData("foo(1)", ErrorKind.Syntax, 0, 6)]
    [InlineData("max()", ErrorKind.Syntax, 0, 5)]
    [InlineData("fma(1, 2)", ErrorKind.Syntax, 0, 9)]
    [InlineData("sqrt(1, 2)", ErrorKind.Syntax, 0, 10)]
    [InlineData("exp(1)", ErrorKind.NotAvailable, 0, 6)]
    [InlineData("atan2(1, 2)", ErrorKind.NotAvailable, 0, 11)]
    [InlineData("3!", ErrorKind.NotAvailable, 1, 2)]
    public void Errors(string source, ErrorKind kind, int start, int end)
    {
        var error = Assert.Throws<CalcError>(() => Evaluator.Run(source, Target.Bits(53), RoundingMode.ToNearestEven));
        Assert.Equal(kind, error.Kind);
        Assert.Equal(new TextSpan(start, end), error.Span);
    }

    [Fact]
    public void TheArityMessagesReadRight()
    {
        Assert.Equal("max takes at least 1 argument, not 0.", Assert.Throws<CalcError>(() => FunctionTable.Check("max", 0, default)).Message);
        Assert.Equal("fma takes 3 arguments, not 2.", Assert.Throws<CalcError>(() => FunctionTable.Check("fma", 2, default)).Message);
        Assert.Equal("log takes 1 or 2 arguments, not 3.", Assert.Throws<CalcError>(() => FunctionTable.Check("log", 3, default)).Message);
        Assert.Equal("sqrt takes 1 argument, not 2.", Assert.Throws<CalcError>(() => FunctionTable.Check("sqrt", 2, default)).Message);
    }

    [Fact]
    public void EveryAvailableFunctionIsImplemented()
    {
        var floats = new FloatArithmetic(Target.Bits(53), RoundingMode.ToNearestEven);
        foreach (var f in FunctionTable.All.Where(f => f.IsAvailable))
            floats.Call(f.Name, Enumerable.Repeat<Value>(floats.Literal("2", default), f.MinArguments).ToArray(), default);
    }

    // The bit operators.

    [Theory]
    [InlineData("5 & 3", "1")]
    [InlineData("5 | 3", "7")]
    [InlineData("5 xor 3", "6")]
    [InlineData("~5", "-6")]
    [InlineData("-6 & 3", "2")]
    [InlineData("-1 | 0", "-1")]
    [InlineData("5 xor -1", "-6")]
    [InlineData("1 << 10", "1024")]
    [InlineData("1024 >> 3", "128")]
    [InlineData("-7 >> 1", "-4")]
    [InlineData("5 << -1", "2")]
    [InlineData("-1 >> 2000", "-1")]
    [InlineData("3 << 10", "3072")]
    public void BitOperators(string source, string expected) =>
        Assert.Equal(expected, Evaluator.Run(source, Target.Bits(53), RoundingMode.ToNearestEven).Value.ToString());

    [Fact]
    public void BitOperatorsMatchBigInteger()
    {
        var random = new Random(7);
        var floats = new FloatArithmetic(Target.Bits(64), RoundingMode.ToNearestEven);
        for (int i = 0; i < 5_000; i++)
        {
            long a = random.NextInt64(-(1L << 40), 1L << 40), b = random.NextInt64(-(1L << 40), 1L << 40);
            int shift = random.Next(-50, 20);
            Value va = floats.Literal(Math.Abs(a).ToString(CultureInfo.InvariantCulture), default), vb = floats.Literal(Math.Abs(b).ToString(CultureInfo.InvariantCulture), default);
            if (a < 0) va = floats.Negate(va, default);
            if (b < 0) vb = floats.Negate(vb, default);
            Value vs = floats.Literal(Math.Abs(shift).ToString(CultureInfo.InvariantCulture), default);
            if (shift < 0) vs = floats.Negate(vs, default);
            BigInteger x = a, y = b;
            Assert.Equal((double)(x & y), D(floats.And(va, vb, default)));
            Assert.Equal((double)(x | y), D(floats.Or(va, vb, default)));
            Assert.Equal((double)(x ^ y), D(floats.Xor(va, vb, default)));
            Assert.Equal((double)~x, D(floats.Not(va, default)));
            Assert.Equal((double)(shift >= 0 ? x << shift : x >> -shift), D(floats.ShiftLeft(va, vs, default)));
            Assert.Equal((double)(shift >= 0 ? x >> shift : x << -shift), D(floats.ShiftRight(va, vs, default)));
        }
    }

    [Theory]
    [InlineData("1.5 & 1", ErrorKind.Domain, 4, 5)]
    [InlineData("1 << 0.5", ErrorKind.Domain, 2, 4)]
    [InlineData("0.5 << 1", ErrorKind.Domain, 4, 6)]
    [InlineData("2^16777217 & 1", ErrorKind.TooBig, 11, 12)]
    [InlineData("1 << 1e30", ErrorKind.Overflow, 2, 4)]
    public void BitOperatorErrors(string source, ErrorKind kind, int start, int end)
    {
        var error = Assert.Throws<CalcError>(() => Evaluator.Run(source, Target.Bits(256), RoundingMode.ToNearestEven));
        Assert.Equal(kind, error.Kind);
        Assert.Equal(new TextSpan(start, end), error.Span);
    }

    [Fact]
    public void ABitResultTooWideForTheTargetIsRounded()
    {
        var outcome = Evaluator.Run("2^60 | 1", Target.Bits(53), RoundingMode.ToNearestEven);
        Assert.Equal(IeeeFlags.Inexact, outcome.Flags);
        Assert.Equal(Math.Pow(2, 60), (double)((FloatValue)outcome.Value).Number);
    }
}
