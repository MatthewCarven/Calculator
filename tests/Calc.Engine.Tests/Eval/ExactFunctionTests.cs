using System.Diagnostics;
using System.Numerics;
using Calc.Engine.Eval;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;
using static Calc.Engine.Tests.Eval.ExactHelpers;

namespace Calc.Engine.Tests.Eval;

/// <summary>B8: B5's table on exact values, exactly, and an exact value meeting a float, rounded once.</summary>
public class ExactFunctionTests
{
    private static Value V(string source, ExactBudgets? budgets = null, int bits = 256, RoundingMode mode = RoundingMode.ToNearestEven) =>
        Evaluator.Run(source, Target.Bits(bits), mode, exact: true, budgets: budgets).Value;

    private static ExactArithmetic Exact256() => new(Target.Bits(256), RoundingMode.ToNearestEven);

    [Theory]
    [InlineData("abs(-2/3)", "2/3")]
    [InlineData("abs(-5)", "5")]
    [InlineData("sign(-2/3)", "-1")]
    [InlineData("sign(0)", "0")]
    [InlineData("sign(7/2)", "1")]
    [InlineData("floor(7/2)", "3")]
    [InlineData("floor(-7/2)", "-4")]
    [InlineData("floor(5)", "5")]
    [InlineData("ceil(7/2)", "4")]
    [InlineData("ceil(-7/2)", "-3")]
    [InlineData("trunc(7/2)", "3")]
    [InlineData("trunc(-7/2)", "-3")]
    [InlineData("round(5/2)", "2")]
    [InlineData("round(7/2)", "4")]
    [InlineData("round(-5/2)", "-2")]
    [InlineData("round(-3/2)", "-2")]
    [InlineData("round(1/3)", "0")]
    [InlineData("round(-2/3)", "-1")]
    [InlineData("round(10^30 + 1/2)", "1000000000000000000000000000000")]
    [InlineData("max(1/3, 1/4, -2)", "1/3")]
    [InlineData("min(1/3, 1/4, -2)", "-2")]
    [InlineData("max(2)", "2")]
    [InlineData("7 mod 3", "1")]
    [InlineData("-7 mod 3", "2")]
    [InlineData("7 mod -3", "-2")]
    [InlineData("-7 mod -3", "-1")]
    [InlineData("6 mod -3", "0")]
    [InlineData("7 rem 3", "1")]
    [InlineData("-7 rem 3", "-1")]
    [InlineData("7 rem -3", "1")]
    [InlineData("(7/2) mod (1/3)", "1/6")]
    [InlineData("(-7/2) mod (1/3)", "1/6")]
    [InlineData("(7/2) mod (-1/3)", "-1/6")]
    [InlineData("(-7/2) rem (1/3)", "-1/6")]
    [InlineData("(10^40 + 1) mod 7", "5")]
    [InlineData("fma(1/3, 3, -1)", "0")]
    [InlineData("fma(2, 3, 4)", "10")]
    [InlineData("fma(1/2, 1/3, 1/4)", "5/12")]
    [InlineData("sqrt(16/9)", "4/3")]
    [InlineData("sqrt(0)", "0")]
    [InlineData("sqrt(1/4)", "1/2")]
    [InlineData("sqrt(10^40)", "100000000000000000000")]
    [InlineData("sqrt(4^50)", "1125899906842624")]
    [InlineData("sqrt(12^2 * 2^10)", "384")]
    [InlineData("√(9/4)", "3/2")]
    [InlineData("(9/4)^(1/2)", "3/2")]
    [InlineData("5 & 3", "1")]
    [InlineData("5 | 3", "7")]
    [InlineData("5 xor 3", "6")]
    [InlineData("~5", "-6")]
    [InlineData("-6 & 255", "250")]
    [InlineData("(2^100 + 1) & 3", "1")]
    [InlineData("1 << 100", "1267650600228229401496703205376")]
    [InlineData("-7 >> 1", "-4")]
    [InlineData("7 >> -2", "28")]
    [InlineData("-1 >> 1000", "-1")]
    [InlineData("5 >> 10^30", "0")]
    [InlineData("-5 >> 10^30", "-1")]
    [InlineData("0 << 10^30", "0")]
    public void ExactResults(string source, string expected)
    {
        Value value = V(source);
        Assert.True(value is IntValue or RatioValue, $"{source} came back as a {value.GetType().Name}: {value}");
        Assert.Equal(expected, value.ToString());
    }

    [Theory]
    [InlineData("(7/2) mod 0", ErrorKind.DivideByZero)]
    [InlineData("5 rem 0", ErrorKind.DivideByZero)]
    [InlineData("5 mod (1 - 1)", ErrorKind.DivideByZero)]
    [InlineData("(1/2) & 1", ErrorKind.Domain)]
    [InlineData("1 | (1/2)", ErrorKind.Domain)]
    [InlineData("~(1/2)", ErrorKind.Domain)]
    [InlineData("(1/2) >> 1", ErrorKind.Domain)]
    [InlineData("1 << (1/2)", ErrorKind.Domain)]
    [InlineData("sqrt(-4)", ErrorKind.Domain)]
    [InlineData("sqrt(-1/4)", ErrorKind.Domain)]
    [InlineData("floor(1, 2)", ErrorKind.Syntax)]
    [InlineData("nosuch(1/2)", ErrorKind.Syntax)]
    [InlineData("exp(1/2)", ErrorKind.NotAvailable)]
    public void Errors(string source, ErrorKind kind) =>
        Assert.Equal(kind, Assert.Throws<CalcError>(() => V(source)).Kind);

    [Fact]
    public void TheRoundingFunctionsModRemFmaAndComparisonsMatchTheOracle()
    {
        var random = new Random(81);
        for (int i = 0; i < 5_000; i++)
        {
            var exact = Exact256();
            Rational x = RandomFraction(random), y = RandomFraction(random), z = RandomFraction(random);
            Value vx = ExactHelpers.Exact(x), vy = ExactHelpers.Exact(y), vz = ExactHelpers.Exact(z);
            string what = $"({x}, {y}, {z})";

            Expect("floor", Floor(x), exact.Call("floor", [vx], default));
            Expect("ceil", -Floor(-x), exact.Call("ceil", [vx], default));
            Expect("trunc", Truncate(x), exact.Call("trunc", [vx], default));
            Expect("round", RoundHalfEven(x), exact.Call("round", [vx], default));
            Expect("abs", x.Num.Sign < 0 ? -x : x, exact.Call("abs", [vx], default));
            Expect("sign", new Rational(x.Num.Sign, 1), exact.Call("sign", [vx], default));
            Expect("mod", x - y * Floor(x / y), exact.Mod(vx, vy, default));
            Expect("rem", x - y * Truncate(x / y), exact.Rem(vx, vy, default));
            Expect("fma", x * y + z, exact.Call("fma", [vx, vy, vz], default));
            Expect("max", Compare(x, y) >= 0 ? x : y, exact.Call("max", [vx, vy], default));
            Expect("min", Compare(x, y) <= 0 ? x : y, exact.Call("min", [vx, vy], default));
            Assert.True(exact.Compare(vx, vy) == Math.Sign(Compare(x, y)), $"compare{what}");
            Assert.True(exact.Compare(vx, vx) == 0, $"compare{what} with itself");
            Assert.Equal(IeeeFlags.None, exact.Flags);

            void Expect(string name, Rational expected, Value actual)
            {
                Assert.True(actual is IntValue or RatioValue, $"{name}{what}: came back as a {actual.GetType().Name}");
                if (actual is RatioValue r)
                    Assert.True(r.Denominator > ApInt.One && ApInt.GreatestCommonDivisor(ApInt.Abs(r.Numerator), r.Denominator) == ApInt.One, $"{name}{what}: {r} isn't in lowest terms");
                if (Q(actual) != expected) Assert.Fail($"{name}{what}: expected {expected}, got {actual}");
            }
        }
    }

    [Fact]
    public void BitOperatorsMatchBigInteger()
    {
        var random = new Random(82);
        var exact = Exact256();
        for (int i = 0; i < 5_000; i++)
        {
            BigInteger a = RandomBig(random, random.Next(1, 200)) * (random.Next(2) == 0 ? 1 : -1);
            BigInteger b = RandomBig(random, random.Next(1, 200)) * (random.Next(2) == 0 ? 1 : -1);
            int count = random.Next(-300, 300);
            Value va = new IntValue(Ap(a)), vb = new IntValue(Ap(b)), vc = new IntValue(count);
            Expect("&", a & b, exact.And(va, vb, default));
            Expect("|", a | b, exact.Or(va, vb, default));
            Expect("xor", a ^ b, exact.Xor(va, vb, default));
            Expect("~", ~a, exact.Not(va, default));
            // floor(a·2^count): BigInteger's shifts are arithmetic, so they floor too.
            Expect("<<", count >= 0 ? a << count : a >> -count, exact.ShiftLeft(va, vc, default));
            Expect(">>", count >= 0 ? a >> count : a << -count, exact.ShiftRight(va, vc, default));

            void Expect(string name, BigInteger expected, Value actual)
            {
                var n = Assert.IsType<IntValue>(actual);
                if (Big(n.Number) != expected) Assert.Fail($"{a} {name} {b} (count {count}): expected {expected}, got {n.Number}");
            }
        }
    }

    [Fact]
    public void AShiftPastTheSizeBudgetIsAFloat()
    {
        var budgets = new ExactBudgets(IntegerBits: 1000);
        Assert.IsType<IntValue>(V("1 << 999", budgets));
        var over = Assert.IsType<FloatValue>(V("3 << 999", budgets)); // 1,001 bits
        Assert.True(over.ExactTooLarge);
        Assert.True(Q(over) == new Rational(3, 1) * Rational.Pow2(999));
    }

    [Fact]
    public void AnIntegerMeetingAFloatInABitOperatorKeepsEveryBit()
    {
        // 2^100 + 1 rounded to 53 bits first would be 2^100, and 2^100 & 3 is 0.
        var exact = new ExactArithmetic(Target.Bits(53), RoundingMode.ToNearestEven);
        Value big = new IntValue(Ap((BigInteger.One << 100) + 1)), three = new FloatValue(new ApFloat(3, 0, 53));
        Assert.True(Q(exact.And(big, three, default)) == new Rational(1, 1));
        Assert.True(Q(exact.Xor(three, big, default)) == Round(new Rational((BigInteger.One << 100) + 2, 1), 53, RoundingMode.ToNearestEven));
        Assert.IsType<FloatValue>(exact.And(big, three, default));
        Assert.Equal(ErrorKind.Domain, Assert.Throws<CalcError>(() => exact.And(RatioValue.Of(1, 2), three, default)).Kind);
    }

    [Fact]
    public void MaxAndMinPickAnArgumentAsItIs()
    {
        var exact = new ExactArithmetic(Target.Bits(53), RoundingMode.ToNearestEven);
        Value third = RatioValue.Of(1, 3), fifth = new FloatValue(((ApFloat)0.2).WithPrecision(53)), half = new FloatValue(((ApFloat)0.5).WithPrecision(53));
        Assert.Same(third, exact.Call("max", [third, fifth], default)); // exactly 1/3, not a float of it
        Assert.Same(fifth, exact.Call("min", [third, fifth], default));
        Assert.IsType<RatioValue>(exact.Call("max", [half, RatioValue.Of(1, 2)], default)); // a tie goes to the exact one
        Assert.IsType<RatioValue>(exact.Call("min", [RatioValue.Of(1, 2), half], default));
    }

    [Fact]
    public void AnExactValueComparesExactlyWithAFloat()
    {
        var random = new Random(83);
        var exact = Exact256();
        for (int i = 0; i < 5_000; i++)
        {
            Rational x = RandomFraction(random);
            FloatValue y = RandomFloat(random, random.Next(2, 120));
            if (random.Next(4) == 0) x = Q(y); // ties too
            int expected = Math.Sign(Compare(x, Q(y)));
            Assert.True(exact.Compare(ExactHelpers.Exact(x), y) == expected, $"{x} against {y}");
            Assert.True(exact.Compare(y, ExactHelpers.Exact(x)) == -expected, $"{y} against {x}");
        }
    }

    [Fact]
    public void ModRemAndFmaWithAFloatAreTheExactAnswerRoundedOnce()
    {
        var random = new Random(84);
        for (int i = 0; i < 5_000; i++)
        {
            int p = random.Next(2, 200);
            RoundingMode mode = Modes[random.Next(4)];
            var exact = new ExactArithmetic(Target.Bits(p), mode);
            Rational x = RandomFraction(random);
            FloatValue y = RandomFloat(random, p);
            Value vx = ExactHelpers.Exact(x);
            Rational fy = Q(y);
            string what = $"({x}, {y}) at {p} bits, {mode}";
            bool flip = random.Next(2) == 0;
            Rational a = flip ? fy : x, b = flip ? x : fy;
            Value va = flip ? y : vx, vb = flip ? vx : y;
            Expect("mod", a - b * Floor(a / b), exact.Mod(va, vb, default));
            Expect("rem", a - b * Truncate(a / b), exact.Rem(va, vb, default));
            Rational z = RandomFraction(random);
            Expect("fma", a * b + z, exact.Call("fma", [va, vb, ExactHelpers.Exact(z)], default));

            void Expect(string name, Rational value, Value actual)
            {
                Assert.IsType<FloatValue>(actual);
                Assert.False(((FloatValue)actual).ExactTooLarge, $"{name}{what}: a float came in, so it's no budget's doing");
                Rational expected = Round(value, p, mode);
                if (Q(actual) != expected) Assert.Fail($"{name}{what}: expected {expected}, got {Q(actual)}");
            }
        }
    }

    [Theory]
    [InlineData(RoundingMode.ToNearestEven)]
    [InlineData(RoundingMode.TowardZero)]
    [InlineData(RoundingMode.TowardPositive)]
    [InlineData(RoundingMode.TowardNegative)]
    public void AFloatModAFractionLandingExactlyOnAValueIsThatValue(RoundingMode mode)
    {
        // 2.5 mod 2/3 is exactly 1/2. Brackets around 2/3 straddle 1/2, so they'd never settle in a directed mode; the
        // float taken as the fraction it is gives 1/2 in every mode. 1.0 mod 1/3 is exactly 0, where the remainder jumps.
        var exact = new ExactArithmetic(Target.Bits(53), mode);
        Value twoAndAHalf = new FloatValue(((ApFloat)2.5).WithPrecision(53)), one = new FloatValue(((ApFloat)1.0).WithPrecision(53));
        Assert.True(Q(exact.Mod(twoAndAHalf, RatioValue.Of(2, 3), default)) == new Rational(1, 2));
        Assert.True(Q(exact.Rem(twoAndAHalf, RatioValue.Of(2, 3), default)) == new Rational(1, 2));
        Assert.True(Q(exact.Mod(one, RatioValue.Of(1, 3), default)).IsZero);
    }

    // sqrt

    [Fact]
    public void APowerOfTwoIsSettledFromItsExponent()
    {
        var clock = Stopwatch.StartNew();
        var root = Assert.IsType<IntValue>(V("sqrt(2^1000000)")); // SqrtRem alone took 11.8 s
        Assert.Equal(500_001, root.Number.BitLength);
        var odd = Assert.IsType<FloatValue>(V("sqrt(2^1000001)"));
        Assert.False(odd.ExactTooLarge); // an odd power of two is no square: irrational, not unknown
        Assert.True(clock.ElapsedMilliseconds < 1000, $"{clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void APerfectSquareIsExactWithinTheWorkBudget()
    {
        Assert.True(Q(V("sqrt(3^1000)")) == new Rational(BigInteger.Pow(3, 500), 1));
        Assert.True(Q(V("sqrt(3^1000/7^200)")) == Rational.Of(BigInteger.Pow(3, 500), BigInteger.Pow(7, 100)));
        // 3^1000 is 1,585 bits, and its square root costs 1,585² by SqrtRem: past a 2^20 budget, so it isn't tried.
        var budgets = new ExactBudgets(MultiplyWork: 1 << 20);
        var unknown = Assert.IsType<FloatValue>(V("sqrt(3^1000)", budgets));
        Assert.True(unknown.ExactTooLarge);
        Assert.True(Q(unknown) == Round(new Rational(BigInteger.Pow(3, 500), 1), 256, RoundingMode.ToNearestEven));
        Assert.True(Q(Assert.IsType<RatioValue>(V("sqrt(3^600/4)", budgets))) == Rational.Of(BigInteger.Pow(3, 300), 2)); // 951 bits: within
        // A part known not to be a square makes it irrational, whatever the budget: a plain float.
        Assert.False(Assert.IsType<FloatValue>(V("sqrt(3^1001)")).ExactTooLarge);
        Assert.False(Assert.IsType<FloatValue>(V("sqrt(2*3^1000)", budgets)).ExactTooLarge);
    }

    [Fact]
    public void SqrtOfAnExactValueIsCorrectlyRoundedForThatValue()
    {
        // The roots rounded down and up must bracket √x, sit one unit apart, and the nearest must be the closer: checked
        // by squaring, in exact fractions, for x exact, not x rounded first.
        var random = new Random(85);
        for (int i = 0; i < 2_000; i++)
        {
            int p = random.Next(2, 300);
            Rational x = RandomFraction(random);
            if (x.Num.Sign < 0) x = -x;
            Value vx = ExactHelpers.Exact(x);
            Rational Root(RoundingMode mode) => Q(new ExactArithmetic(Target.Bits(p), mode).Call("sqrt", [vx], default));
            Rational down = Root(RoundingMode.TowardNegative), up = Root(RoundingMode.TowardPositive), zero = Root(RoundingMode.TowardZero), near = Root(RoundingMode.ToNearestEven);
            string what = $"sqrt({x}) at {p} bits";
            Assert.True(Compare(down * down, x) <= 0 && Compare(up * up, x) >= 0, $"{what}: {down} and {up} don't bracket it");
            Assert.True(zero == down, $"{what}: toward zero gave {zero}, down {down}");
            if (down == up)
            {
                Assert.True(down * down == x, $"{what}: equal ends, but not exact");
                continue;
            }
            Rational mid = (down + up) * new Rational(1, 2);
            Assert.True(near == (Compare(mid * mid, x) > 0 ? down : up), $"{what}: nearest gave {near}");
        }
    }

    [Fact]
    public void SqrtOfASeventhAt53BitsIsRightInTheLastPlace()
    {
        // The review's case: 1/7 rounded to 53 bits first gives ...2720.
        Assert.Equal("0.37796447300922725", V("sqrt(1/7)", bits: 53).ToString());
        Assert.Equal("0.37796447300922725", V("(1/7)^(1/2)", bits: 53).ToString());
        Assert.Equal("0.3779644730092272", Evaluator.Run("sqrt(1/7)", Target.Bits(53), RoundingMode.ToNearestEven, exact: false).Value.ToString());
    }

    [Theory]
    [InlineData(RoundingMode.ToNearestEven, false)]
    [InlineData(RoundingMode.TowardZero, false)]
    [InlineData(RoundingMode.TowardNegative, false)]
    [InlineData(RoundingMode.TowardPositive, true)]
    public void SqrtOfOnePlusATinyBitInEveryMode(RoundingMode mode, bool above)
    {
        // √(1 + 10^-1000) is 1 + 5·10^-1001: 1 except rounding up, where it's the next value above 1.
        var value = Assert.IsType<FloatValue>(V("sqrt(1+1e-1000)", mode: mode));
        Rational expected = above ? new Rational(1, 1) + Rational.Pow2(-255) : new Rational(1, 1);
        Assert.True(Q(value) == expected, $"{mode}: {value}");
    }
}
