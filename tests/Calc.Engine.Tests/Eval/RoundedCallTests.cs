using System.Numerics;
using Calc.Engine.Eval;
using Calc.Engine.Text;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;
using static Calc.Engine.Tests.Eval.ExactHelpers;

namespace Calc.Engine.Tests.Eval;

/// <summary>
/// B8's RoundedCall, by stand-in functions, since Natural has no ln yet; and every place an exact value meets a float or
/// outgrows a budget, against the exact answer rounded once.
/// </summary>
public class RoundedCallTests
{
    private static Value Three => new FloatValue(new ApFloat(3, 0, 2));

    private static int Width(Value v) => ((FloatValue)v).Number.Precision;

    [Fact]
    public void ItStopsAtTheBoundAndTakesTheNearest()
    {
        // 3 × (1/3) is exactly 1, where rounding up changes: the corners give 1 and the value above it at every width, so
        // it never settles. W starts at 256 + 64 = 320; the bound is 320 + bits(1) + bits(3) = 323. Two corners a width,
        // then one call on 1/3 rounded to nearest at the bound.
        var floats = new FloatArithmetic(Target.Bits(256), RoundingMode.TowardPositive);
        var widths = new List<int>();
        RoundedCall.Of(floats, [RatioValue.Of(1, 3)], v => { widths.Add(Width(v[0])); return floats.Multiply(v[0], Three, default); });
        Assert.Equal([320, 320, 323, 323, 323], widths);
    }

    [Fact]
    public void WidthsDoubleUpToTheBound()
    {
        // 3 × 2^2000/3 is exactly 2^2000. Relative only, so W starts at 320, doubles to 640 and 1,280, and stops at the
        // bound, 320 + bits(2^2000) + bits(3) = 2,323.
        var floats = new FloatArithmetic(Target.Bits(256), RoundingMode.TowardNegative);
        var widths = new List<int>();
        RoundedCall.Of(floats, [RatioValue.Of(ApInt.One << 2000, 3)], v => { widths.Add(Width(v[0])); return floats.Multiply(v[0], Three, default); }, absolute: false);
        Assert.Equal([320, 320, 640, 640, 1280, 1280, 2323, 2323, 2323], widths);
        // Absolute, W covers the whole part too, bits(n) - bits(d) = 2,001 - 2: 320 + 1,999 = 2,319, and the bound 2,319 + 2,003.
        widths.Clear();
        RoundedCall.Of(floats, [RatioValue.Of(ApInt.One << 2000, 3)], v => { widths.Add(Width(v[0])); return floats.Multiply(v[0], Three, default); });
        Assert.Equal([2319, 2319, 4322, 4322, 4322], widths);
    }

    [Fact]
    public void AnEndThatErrorsCountsAsDisagreeing()
    {
        // 1/(1 - x) at x = 1 - 10^-100 is 10^100. At W = 320, x rounds up to exactly 1, and that end divides by zero; at
        // 640 both ends are below 1 and agree. Taking the end that didn't error would give about 2^320.
        var floats = new FloatArithmetic(Target.Bits(256), RoundingMode.ToNearestEven);
        BigInteger googol = BigInteger.Pow(10, 100);
        var widths = new List<int>();
        Value Pole(IReadOnlyList<Value> v)
        {
            widths.Add(Width(v[0]));
            ApFloat x = ((FloatValue)v[0]).Number;
            return floats.Divide(new FloatValue(1), new FloatValue(ApFloat.Add(1, -x, 4096)), default);
        }
        Value result = RoundedCall.Of(floats, [ExactHelpers.Exact(Rational.Of(googol - 1, googol))], Pole);
        Assert.True(Q(result) == new Rational(googol, 1), $"got {result}");
        Assert.Equal([320, 320, 640, 640], widths);
        Assert.Equal(IeeeFlags.Inexact, floats.Flags); // the failed end's DivideByZero is put back
    }

    [Fact]
    public void AnEndThatGivesInfinityCountsAsDisagreeingToo()
    {
        // A precision target can't give ∞, so a stand-in returns it for its first two calls. Each ends its width at once,
        // at 117 and at the bound, 121; two infinities would have agreed. Then the nearest, the third call, gives 2.
        var floats = new FloatArithmetic(Target.Bits(53), RoundingMode.ToNearestEven);
        int calls = 0;
        Value result = RoundedCall.Of(floats, [RatioValue.Of(2, 3)], v =>
        {
            calls++;
            return calls <= 2 ? new FloatValue(ApFloat.PositiveInfinity) : floats.Multiply(v[0], Three, default);
        });
        Assert.True(Q(result) == new Rational(2, 1));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void AFailedEndsFlagsAreNotKept()
    {
        // sqrt(x - 1 - 2^-400) at x = 1 + 10^-100: at W = 320 the low end is exactly 1, so the root's argument is
        // negative, raising Invalid; at 640 both ends agree. Only the agreeing ends' flags count.
        var floats = new FloatArithmetic(Target.Bits(256), RoundingMode.ToNearestEven);
        BigInteger googol = BigInteger.Pow(10, 100);
        ApFloat shift = ApFloat.Add(1, new ApFloat(1, -400, 2), 1000);
        Value result = RoundedCall.Of(floats, [ExactHelpers.Exact(Rational.Of(googol + 1, googol))],
            v => floats.Call("sqrt", [new FloatValue(ApFloat.Add(((FloatValue)v[0]).Number, -shift, 4096))], default));
        Assert.IsType<FloatValue>(result);
        Assert.Equal(IeeeFlags.Inexact, floats.Flags);
    }

    [Fact]
    public void OtherErrorsGoStraightUp()
    {
        var floats = new FloatArithmetic(Target.Bits(53), RoundingMode.ToNearestEven);
        int calls = 0;
        var error = Assert.Throws<CalcError>(() => RoundedCall.Of(floats, [RatioValue.Of(1, 3)], _ =>
        {
            calls++;
            throw new CalcError(ErrorKind.Stopped, "Stopped.", null);
        }));
        Assert.Equal(ErrorKind.Stopped, error.Kind);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void TwoExactArgumentsMeanFourCornersAndHeldOnesNone()
    {
        var floats = new FloatArithmetic(Target.Bits(256), RoundingMode.ToNearestEven);
        int calls = 0;
        Value product = RoundedCall.Of(floats, [RatioValue.Of(1, 3), RatioValue.Of(1, 7)], v => { calls++; return floats.Multiply(v[0], v[1], default); });
        Assert.Equal(4, calls);
        Assert.True(Q(product) == Round(new Rational(1, 21), 256, RoundingMode.ToNearestEven));
        // An integer, and a fraction over a power of two, are held exactly: one call, on the values themselves.
        calls = 0;
        Value exactly = RoundedCall.Of(floats, [new IntValue(5), RatioValue.Of(3, 4)], v => { calls++; return floats.Multiply(v[0], v[1], default); });
        Assert.Equal(1, calls);
        Assert.True(Q(exactly) == new Rational(15, 4));
    }

    // Where the engine uses it, and the other routes to one rounding.

    [Fact]
    public void MixedOperatorsAreTheExactAnswerRoundedOnce()
    {
        // x op y for an exact x and a float y, against the exact answer rounded once. Rounding x to the target first, as
        // B7 did, is counted too, to show the test can tell: it differs in about 30% of cases for + × ÷.
        var random = new Random(86);
        int roundedFirstDiffers = 0, cases = 0;
        string[] ops = ["+", "-", "*", "/"];
        for (int i = 0; i < 5_000; i++)
        {
            int p = random.Next(2, 300);
            RoundingMode mode = Modes[random.Next(4)];
            var exact = new ExactArithmetic(Target.Bits(p), mode);
            var floats = new FloatArithmetic(Target.Bits(p), mode);
            Rational x = RandomFraction(random);
            FloatValue y = RandomFloat(random, p);
            bool flip = random.Next(2) == 0;
            string op = ops[random.Next(ops.Length)];
            Value vx = ExactHelpers.Exact(x);
            Value va = flip ? y : vx, vb = flip ? vx : y;
            Rational a = flip ? Q(y) : x, b = flip ? x : Q(y);
            Rational value = op switch { "+" => a + b, "-" => a - b, "*" => a * b, _ => a / b };
            Value actual = Apply(exact, op, va, vb);
            Rational expected = Round(value, p, mode);
            Assert.True(Q(actual) == expected, $"{(flip ? $"{y} {op} {x}" : $"{x} {op} {y}")} at {p} bits, {mode}: expected {expected}, got {Q(actual)}");
            Assert.False(((FloatValue)actual).ExactTooLarge);
            cases++;
            if (Q(Apply(floats, op, va, vb)) != expected) roundedFirstDiffers++;
        }
        Assert.InRange(roundedFirstDiffers, cases / 10, cases);
    }

    private static Value Apply(IArithmetic arithmetic, string op, Value a, Value b) => op switch
    {
        "+" => arithmetic.Add(a, b, default),
        "-" => arithmetic.Subtract(a, b, default),
        "*" => arithmetic.Multiply(a, b, default),
        _ => arithmetic.Divide(a, b, default),
    };

    [Theory]
    [InlineData(RoundingMode.TowardPositive)]
    [InlineData(RoundingMode.TowardNegative)]
    public void AThirdTimesAFloatThreeIsExactlyOne(RoundingMode mode)
    {
        // An exact answer from a mixed product: the multiply-then-divide route gives it in every mode, where brackets
        // around 1/3 would straddle 1 forever.
        var exact = new ExactArithmetic(Target.Bits(53), mode);
        Assert.True(Q(exact.Multiply(RatioValue.Of(1, 3), Three, default)) == new Rational(1, 1));
        Assert.True(Q(exact.Divide(Three, RatioValue.Of(3, 1), default)) == new Rational(1, 1));
        Assert.True(Q(exact.Divide(RatioValue.Of(2, 3), new FloatValue(new ApFloat(2, 0, 2)), default)) == Round(new Rational(1, 3), 53, mode));
        Assert.Equal(ErrorKind.DivideByZero, Assert.Throws<CalcError>(() => exact.Divide(RatioValue.Of(2, 3), new FloatValue(ApFloat.Zero), default)).Kind);
    }

    [Theory]
    [InlineData(RoundingMode.ToNearestEven)]
    [InlineData(RoundingMode.TowardZero)]
    [InlineData(RoundingMode.TowardPositive)]
    [InlineData(RoundingMode.TowardNegative)]
    public void OnePointOneToTheFiveThousandIsTheExactBaseRaised(RoundingMode mode)
    {
        // 11^5000/10^5000 is past the ratio budget, so it's a float, from the exact 1.1, with y held exactly.
        var value = Assert.IsType<FloatValue>(Evaluator.Run("1.1^5000", Target.Bits(53), mode, exact: true).Value);
        Assert.True(value.ExactTooLarge);
        Rational expected = Round(Rational.Of(BigInteger.Pow(11, 5000), BigInteger.Pow(10, 5000)), 53, mode);
        Assert.True(Q(value) == expected, $"{mode}: {value}");
        if (mode == RoundingMode.ToNearestEven)
        {
            Assert.Equal("9.19233389907065E+206", ResultText.Of(value, Target.Bits(53)).Main);
            // Rounded first, 1.1 is 1.100000000000000088817841970012523, and the answer's last 3 shown digits go wrong.
            var roundedFirst = Evaluator.Run("1.1^5000", Target.Bits(53), mode, exact: false).Value;
            Assert.NotEqual("9.19233389907065E+206", ResultText.Of(roundedFirst, Target.Bits(53)).Main);
        }
    }

    [Fact]
    public void PowersOfExactBasesPastTheBudgetMatchTheOracle()
    {
        var random = new Random(87);
        var budgets = new ExactBudgets(RatioBits: 256, IntegerBits: 512);
        for (int i = 0; i < 300; i++)
        {
            int p = random.Next(2, 200);
            RoundingMode mode = Modes[random.Next(4)];
            Rational x = RandomFraction(random, 40);
            int k = random.Next(-60, 60);
            if (k == 0) k = 7;
            Value value = Evaluator.Run($"({x})^({k})", Target.Bits(p), mode, exact: true, budgets: budgets).Value;
            Rational exactPower = k > 0 ? Rational.Of(BigInteger.Pow(x.Num, k), BigInteger.Pow(x.Den, k)) : Rational.Of(BigInteger.Pow(x.Den, -k), BigInteger.Pow(x.Num, -k));
            Rational expected = value is FloatValue ? Round(exactPower, p, mode) : exactPower;
            Assert.True(Q(value) == expected, $"({x})^{k} at {p} bits, {mode}: expected {expected}, got {Q(value)}");
        }
    }

    [Fact]
    public void PastABudgetEachOperationIsTheExactAnswerRoundedOnce()
    {
        // Small budgets push + − × ÷, mod and fma past them: unreduced fractions within the work budget, RoundedCall past it.
        var random = new Random(88);
        foreach (var budgets in new[] { new ExactBudgets(IntegerBits: 100, RatioBits: 40), new ExactBudgets(RatioBits: 40, MultiplyWork: 1000) })
        {
            for (int i = 0; i < 2_000; i++)
            {
                int p = random.Next(2, 120);
                RoundingMode mode = Modes[random.Next(4)];
                var exact = new ExactArithmetic(Target.Bits(p), mode, budgets);
                // Built by the engine with default budgets, so the parts may already be past these.
                Rational x = RandomFraction(random, 80), y = RandomFraction(random, 80), z = RandomFraction(random, 80);
                Value vx = ExactHelpers.Exact(x), vy = ExactHelpers.Exact(y), vz = ExactHelpers.Exact(z);
                string what = $"({x}, {y}, {z}) at {p} bits, {mode}, {budgets}";
                Expect("+", x + y, exact.Add(vx, vy, default));
                Expect("-", x - y, exact.Subtract(vx, vy, default));
                Expect("*", x * y, exact.Multiply(vx, vy, default));
                Expect("/", x / y, exact.Divide(vx, vy, default));
                Expect("mod", x - y * Floor(x / y), exact.Mod(vx, vy, default));
                Expect("fma", x * y + z, exact.Call("fma", [vx, vy, vz], default));

                void Expect(string name, Rational value, Value actual)
                {
                    Rational expected = actual is FloatValue f ? Round(value, p, mode) : value;
                    if (actual is FloatValue g) Assert.True(g.ExactTooLarge, $"{name}{what}: a float, but not chipped");
                    if (Q(actual) != expected) Assert.Fail($"{name}{what}: expected {expected}, got {Q(actual)}");
                }
            }
        }
    }
}
