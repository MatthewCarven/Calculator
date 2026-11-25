using System.Globalization;
using System.Numerics;
using Calc.Engine.Eval;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Tests.Eval;

public class PowerTests
{
    private static readonly RoundingMode[] Modes = [RoundingMode.ToNearestEven, RoundingMode.TowardZero, RoundingMode.TowardPositive, RoundingMode.TowardNegative];

    [Fact]
    public void RandomIntegerPowersMatchTheRationalOracle()
    {
        // Both routes: |n|·bitlen(m) at most 4(p + 64) goes exact, the rest by bounds. Negative bases and exponents too.
        var random = new Random(8);
        int exact = 0, bounds = 0;
        for (int i = 0; i < 2_000; i++)
        {
            int p = random.Next(2, 121);
            RoundingMode mode = Modes[random.Next(4)];
            var floats = new FloatArithmetic(Target.Bits(p), mode);
            string literal = random.Next(5) switch
            {
                0 => random.Next(0, 20).ToString(CultureInfo.InvariantCulture),
                1 => (random.NextDouble() * 4).ToString("R", CultureInfo.InvariantCulture),
                2 => (1 + random.NextDouble() / 1000).ToString("R", CultureInfo.InvariantCulture),
                3 => "0x1." + random.Next(1, 0xFFFF).ToString("x4", CultureInfo.InvariantCulture) + "p" + random.Next(-3, 4).ToString(CultureInfo.InvariantCulture),
                _ => random.Next(1, 1_000_000).ToString(CultureInfo.InvariantCulture),
            };
            Value x = floats.Literal(literal, default);
            if (random.Next(3) == 0) x = floats.Negate(x, default);
            int n = random.Next(4) switch { 0 => random.Next(-3, 4), 1 => random.Next(-40, 41), 2 => random.Next(-300, 301), _ => random.Next(-1500, 1501) };
            // Exact, not a literal: at 7 bits the literal 786 rounds to 784, as any literal rounds to its target.
            Value y = new FloatValue((ApFloat)(long)n);

            ApFloat xf = ((FloatValue)x).Number;
            Rational xr = RationalOracle.ToRational(xf);
            string what = $"({(xf.IsNegative ? "-" : "")}{literal})^{n} at {p} bits, {mode}";
            if (!xf.IsZero && Math.Abs((long)n) * ApFloat.Abs(xf).Significand.BitLength <= 4L * (p + 64)) exact++; else bounds++;

            if (xr.IsZero && n < 0)
            {
                var error = Assert.Throws<CalcError>(() => floats.Power(x, y, default));
                Assert.True(error.Kind == ErrorKind.DivideByZero, $"{what}: {error.Kind}");
                continue;
            }
            bool inexact = false;
            Rational expected = RationalOracle.Round(Pow(xr, n), p, mode, ref inexact);
            var before = floats.Flags;
            Rational got = RationalOracle.ToRational(((FloatValue)floats.Power(x, y, default)).Number);
            if (got != expected) Assert.Fail($"{what}: expected {expected}, got {got}");
            bool raised = ((floats.Flags & ~before) & IeeeFlags.Inexact) != 0;
            if (raised != inexact && !((before & IeeeFlags.Inexact) != 0)) Assert.Fail($"{what}: Inexact should be {inexact}");
        }
        Assert.InRange(exact, 300, 1_700);
        Assert.InRange(bounds, 300, 1_700);
    }

    private static Rational Pow(Rational x, int n)
    {
        if (n == 0) return new Rational(1, 1);
        Rational r = Rational.Of(BigInteger.Pow(x.Num, Math.Abs(n)), BigInteger.Pow(x.Den, Math.Abs(n)));
        return n > 0 ? r : new Rational(1, 1) / r;
    }

    [Fact]
    public void TheSpecialsMatchMathPow()
    {
        // T10's table, every pair, against hardware. y = 0.5 is sqrt, as Math.Sqrt for a finite non-zero x.
        double[] xs = [0, -0.0, 1, -1, 2, -2, 0.5, -0.5, 3, -3, 0.25, double.PositiveInfinity, double.NegativeInfinity, double.NaN];
        double[] ys = [0, -0.0, 1, -1, 2, -2, 3, -3, 0.5, double.PositiveInfinity, double.NegativeInfinity, double.NaN, 1e20, -1e20, 1e300];
        var floats = new FloatArithmetic(Target.Of(IeeeFormat.Binary64), RoundingMode.ToNearestEven);
        int compared = 0;
        foreach (double x in xs)
            foreach (double y in ys)
            {
                double actual;
                try { actual = (double)((FloatValue)floats.Power(new FloatValue(x), new FloatValue(y), default)).Number; }
                catch (CalcError e) when (e.Kind == ErrorKind.NotAvailable) { continue; }
                double expected = y == 0.5 && double.IsFinite(x) && x != 0 ? Math.Sqrt(x) : Math.Pow(x, y);
                bool same = double.IsNaN(expected) ? double.IsNaN(actual) : BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual);
                Assert.True(same, $"{x:R}^{y:R}: Math.Pow gives {expected:R}, Calc {actual:R}");
                compared++;
            }
        Assert.InRange(compared, 180, 210);
    }

    private static Outcome Run(string source, Target target, RoundingMode mode = RoundingMode.ToNearestEven) => Evaluator.Run(source, target, mode);
    private static double D(Outcome o) => (double)((FloatValue)o.Value).Number;
    private static readonly Target Binary64 = Target.Of(IeeeFormat.Binary64);

    [Fact]
    public void TheBriefsCases()
    {
        var o = Run("3^1000", Binary64);
        Assert.Equal(double.PositiveInfinity, D(o));
        Assert.Equal(IeeeFlags.Overflow | IeeeFlags.Inexact, o.Flags);
        Assert.Equal(double.MaxValue, D(Run("3^1000", Binary64, RoundingMode.TowardZero)));
        Assert.Equal(IeeeFlags.Inexact, Run("3^200", Target.Bits(256)).Flags);
        Assert.Equal(double.PositiveInfinity, D(Run("1e300^1e16", Binary64)));
        Assert.Equal(ErrorKind.Overflow, Assert.Throws<CalcError>(() => Run("3^(2^63)", Target.Bits(256))).Kind);
        var wrapped = Assert.Throws<CalcError>(() => Run("(2^(2^62))^2", Target.Bits(256)));
        Assert.Equal(ErrorKind.Overflow, wrapped.Kind);
        Assert.Equal(new TextSpan(10, 11), wrapped.Span);
        // The brief said this overflowed; its exponent, 7.3·10^18, fits a long. Its decimal exponent is log10(3)·2^62.
        Assert.EndsWith("E+2200333419485199624", Run("3^(2^62)", Target.Bits(256)).Value.ToString());
    }

    [Theory]
    [InlineData("0^0", "1")]
    [InlineData("(-2)^3", "-8")]
    [InlineData("(-2)^4", "16")]
    [InlineData("(-2)^-3", "-0.125")]
    [InlineData("2.5²", "6.25")]
    [InlineData("1.5³", "3.375")]
    [InlineData("10^-2", "0.01")]
    [InlineData("(-1)^(2^70)", "1")]
    [InlineData("(-1)^(2^70+1)", "-1")]
    [InlineData("1^(2^70)", "1")]
    public void PowersOnTheirOwn(string source, string expected) =>
        Assert.Equal(expected, Run(source, Target.Bits(256)).Value.ToString());

    [Fact]
    public void AtTheEdgesOfAFormat()
    {
        Assert.Equal(double.Epsilon, D(Run("2^-1074", Binary64)));
        var underflow = Run("2^-1080", Binary64);
        Assert.Equal(0.0, D(underflow));
        Assert.Equal(IeeeFlags.Underflow | IeeeFlags.Inexact, underflow.Flags);
        Assert.Equal(double.Epsilon, D(Run("0.5^2000", Binary64, RoundingMode.TowardPositive)));
        Assert.Equal(double.PositiveInfinity, D(Run("3^(2^70)", Binary64)));
        Assert.Equal(0.0, D(Run("0.25^(2^70)", Binary64)));
        Assert.Equal(double.PositiveInfinity, D(Run("(-3)^(2^70)", Binary64)));
        Assert.Equal(double.PositiveInfinity, D(Run("0.5^-(2^70)", Binary64)));
        Assert.Equal(double.NegativeInfinity, D(Run("(-3)^1001", Binary64)));
        // A negative exponent on the bounds route, cut short at the format's edge, then inverted.
        var tiny = Run("3^-1000", Binary64);
        Assert.Equal(0.0, D(tiny));
        Assert.Equal(IeeeFlags.Underflow | IeeeFlags.Inexact, tiny.Flags);
        Assert.Equal(double.PositiveInfinity, D(Run("0.5^-2000", Binary64)));
        Assert.Equal(-0.0, D(Run("(-3)^-1001", Binary64)));
        Assert.True(double.IsNegative(D(Run("(-3)^-1001", Binary64))));
        // Exactly at the thresholds for an exponent past a long: |x| = 2 and |x| = 1/2 are settled.
        Assert.Equal(double.PositiveInfinity, D(Run("2^(2^70)", Binary64)));
        Assert.Equal(0.0, D(Run("0.5^(2^70)", Binary64)));
        var byZero = Run("0^-1", Binary64);
        Assert.Equal(double.PositiveInfinity, D(byZero));
        Assert.Equal(IeeeFlags.DivideByZero, byZero.Flags);
        Assert.Equal(double.NegativeInfinity, D(Run("(-0)^-3", Binary64)));
    }

    [Fact]
    public void APowerOfTwoStaysExactHoweverLarge()
    {
        var o = Run("2^1000000", Target.Bits(53));
        Assert.Equal(IeeeFlags.None, o.Flags);
        Assert.Equal(0, ((FloatValue)o.Value).Number.CompareTo(new ApFloat(1, 1_000_000, 1)));
    }

    [Fact]
    public void AHalfIsSqrt()
    {
        Assert.Equal(Math.Sqrt(2), D(Run("2^0.5", Target.Bits(53))));
        Assert.True(double.IsNaN(D(Run("(-4)^0.5", Binary64))));
        Assert.Equal(IeeeFlags.Invalid, Run("(-4)^0.5", Binary64).Flags);
    }

    [Theory]
    [InlineData("0^-1", ErrorKind.DivideByZero, 1, 2)]
    [InlineData("(-0)^-2", ErrorKind.DivideByZero, 4, 5)]
    [InlineData("(-4)^0.5", ErrorKind.Domain, 4, 5)]
    [InlineData("(-8)^(1/3)", ErrorKind.Domain, 4, 5)]
    [InlineData("2^1.5", ErrorKind.NotAvailable, 1, 2)]
    [InlineData("1.5^(2^70)", ErrorKind.NotAvailable, 3, 4)]
    [InlineData("3^(2^70)", ErrorKind.Overflow, 1, 2)]
    [InlineData("2^(2^70)", ErrorKind.Overflow, 1, 2)]
    [InlineData("0.25^(2^70)", ErrorKind.Overflow, 4, 5)]
    public void ErrorsAtBits(string source, ErrorKind kind, int start, int end)
    {
        var error = Assert.Throws<CalcError>(() => Run(source, Target.Bits(256)));
        Assert.Equal(kind, error.Kind);
        Assert.Equal(new TextSpan(start, end), error.Span);
    }
}
