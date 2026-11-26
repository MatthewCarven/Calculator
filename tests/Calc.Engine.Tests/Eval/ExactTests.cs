using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Calc.Engine.Eval;
using Calc.Engine.Text;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Tests.Eval;

public class ExactTests
{
    private static readonly Target Bits256 = Target.Bits(256);

    private static Outcome Run(string source, ExactBudgets? budgets = null, Value? ans = null) =>
        Evaluator.Run(source, Bits256, RoundingMode.ToNearestEven, ans, exact: true, budgets);

    private static Value V(string source, ExactBudgets? budgets = null) => Run(source, budgets).Value;

    private static Rational AsRational(Value v) => v switch
    {
        IntValue i => new Rational(Big(i.Number), 1),
        RatioValue r => new Rational(Big(r.Numerator), Big(r.Denominator)),
        _ => throw new Xunit.Sdk.XunitException($"{v} isn't exact"),
    };

    private static BigInteger Big(ApInt n) => BigInteger.Parse(n.ToString(), CultureInfo.InvariantCulture);

    /// <summary>A tree's exact value, by fractions, with nothing rounded: an oracle sharing no code with ExactArithmetic.</summary>
    private static Rational ExactOf(Node node) => node switch
    {
        NumberNode n => RationalOracle.Exact(n.Text),
        PrefixNode { Op: PrefixOp.Negate } p => -ExactOf(p.Operand),
        PrefixNode p => ExactOf(p.Operand),
        BinaryNode b => b.Op switch
        {
            BinaryOp.Add => ExactOf(b.Left) + ExactOf(b.Right),
            BinaryOp.Subtract => ExactOf(b.Left) - ExactOf(b.Right),
            BinaryOp.Multiply => ExactOf(b.Left) * ExactOf(b.Right),
            _ => ExactOf(b.Right) is var right && right.IsZero ? throw new OracleDivideByZero() : ExactOf(b.Left) / right,
        },
        _ => throw new ArgumentException(node.GetType().Name),
    };

    [Fact]
    public void RandomTreesAreExactlyRight()
    {
        var generator = new ArithmeticTreeGenerator(77);
        int divisionsByZero = 0, fractions = 0;
        for (int i = 0; i < 5_000; i++)
        {
            Node tree = generator.Next(6);
            Rational? expected;
            try { expected = ExactOf(tree); }
            catch (OracleDivideByZero) { expected = null; }
            var exact = new ExactArithmetic(Bits256, RoundingMode.ToNearestEven);
            Value? actual = null;
            CalcError? error = null;
            try { actual = new Evaluator(exact, new Names()).Evaluate(tree); }
            catch (CalcError e) { error = e; }
            string what = Printer.Print(tree);
            if (expected is null)
            {
                divisionsByZero++;
                Assert.True(error?.Kind == ErrorKind.DivideByZero, $"{what}: expected DivideByZero, got {error?.Kind.ToString() ?? actual!.ToString()}");
                continue;
            }
            if (error is not null) Assert.Fail($"{what}: {error.Kind}: {error.Message}");
            Assert.True(actual is IntValue or RatioValue, $"{what}: came back as a {actual!.GetType().Name}");
            if (actual is RatioValue r)
            {
                fractions++;
                Assert.True(r.Denominator > ApInt.One && ApInt.GreatestCommonDivisor(ApInt.Abs(r.Numerator), r.Denominator) == ApInt.One, $"{what}: {r} isn't in lowest terms");
            }
            Assert.True(AsRational(actual) == expected, $"{what}: expected {expected}, got {actual}");
            Assert.Equal(IeeeFlags.None, exact.Flags); // exact work raises no flags
        }
        // Exactly, a zero divisor is rare: no underflow, no x - x rounding to 0.
        Assert.InRange(divisionsByZero, 0, 500);
        Assert.InRange(fractions, 1_000, 4_900);
    }

    [Theory]
    [InlineData("0.1+0.2", "3/10")]
    [InlineData("1/3*3", "1")]
    [InlineData("1/3 + 0.5", "5/6")]
    [InlineData("200 + 10%", "2001/10")]
    [InlineData("1.5e-3", "3/2000")]
    [InlineData("0x1.8p-3", "3/16")]
    [InlineData("0b1.1p3", "12")]
    [InlineData("0o17", "15")]
    [InlineData("1_000.5", "2001/2")]
    [InlineData("6/-4", "-3/2")]
    [InlineData("-6/-4", "3/2")]
    [InlineData("2^-10", "1/1024")]
    [InlineData("(2/3)^-3", "27/8")]
    [InlineData("(-2/3)^3", "-8/27")]
    [InlineData("(-2)^-3", "-1/8")]
    [InlineData("0^0", "1")]
    [InlineData("1^(10^30)", "1")]
    [InlineData("(-1)^(10^30)", "1")]
    [InlineData("(-1)^(10^30+1)", "-1")]
    [InlineData("0^(10^30)", "0")]
    [InlineData("10^6000/10/10^5998", "10")]
    public void ExactResults(string source, string expected)
    {
        Value value = V(source);
        // A float 1 prints as 1 too, so the kind is checked as well as the text.
        Assert.True(value is IntValue or RatioValue, $"{source} came back as a {value.GetType().Name}");
        Assert.Equal(expected, value.ToString());
    }

    [Fact]
    public void TwoToTheThousandIsExact()
    {
        var v = Assert.IsType<IntValue>(V("2^1000"));
        Assert.Equal(BigInteger.Pow(2, 1000), Big(v.Number));
        Assert.IsType<IntValue>(V("10^6000/10"));
    }

    [Fact]
    public void ARatioThatNormalisesToAWholeNumberIsAnInteger()
    {
        Assert.IsType<IntValue>(V("6/3"));
        Assert.IsType<IntValue>(V("1/4 + 3/4"));
        Assert.IsType<IntValue>(V("(1/2)^-3"));
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("0/0")]
    [InlineData("(1/2)/0")]
    [InlineData("0^-1")]
    [InlineData("(1-1)^-3")]
    public void DivisionByZeroOnTheExactPath(string source) =>
        Assert.Equal(ErrorKind.DivideByZero, Assert.Throws<CalcError>(() => Run(source)).Kind);

    [Fact]
    public void MinusOneToAHalfIsDomain() =>
        Assert.Equal(ErrorKind.Domain, Assert.Throws<CalcError>(() => Run("(-1)^(1/2)")).Kind);

    // Budget crossings, either side. Small budgets keep the costly side quick; the logic is the same.

    [Fact]
    public void TheIntegerSizeBudget()
    {
        var budgets = new ExactBudgets(IntegerBits: 1000);
        Assert.IsType<IntValue>(V("2^999", budgets)); // 1,000 bits: exempt from work as a power of two, within size
        var over = Assert.IsType<FloatValue>(V("2^1000", budgets));
        Assert.True(over.ExactTooLarge);
        Assert.IsType<IntValue>(V("3^630", budgets)); // 999 bits
        Assert.True(Assert.IsType<FloatValue>(V("3^631", budgets)).ExactTooLarge);
        Assert.True(Assert.IsType<FloatValue>(V("2^999 + 2^999", budgets)).ExactTooLarge); // 1,001 bits
        Assert.IsType<IntValue>(V("2^998 + 2^998", budgets));
    }

    [Fact]
    public void TheWorkBudgetOnAProduct()
    {
        // 3^600 is 951 bits and 3^700 is 1,110: their product's work, 1,055,610, is just past 2^20.
        var budgets = new ExactBudgets(MultiplyWork: 1 << 20);
        Assert.True(Assert.IsType<FloatValue>(V("3^600 * 3^700", budgets)).ExactTooLarge);
        Assert.IsType<IntValue>(V("3^600 * 3^600", budgets)); // 951·951 = 904,401, within
    }

    [Fact]
    public void TheWorkBudgetOnALastSquaring()
    {
        // 2^20 work: the last squaring's operand at most 1,024 bits, so 3^k exact while bits(3^k)/2 + 1 ≤ 1,024.
        var budgets = new ExactBudgets(MultiplyWork: 1 << 20);
        Assert.IsType<IntValue>(V("3^1290", budgets));
        Assert.True(Assert.IsType<FloatValue>(V("3^1300", budgets)).ExactTooLarge);
        Assert.IsType<IntValue>(V("2^100000", budgets)); // a power of two is exempt from the work budget
    }

    [Fact]
    public void TheRatioBudget()
    {
        Assert.IsType<RatioValue>(V("1/3^10000")); // 15,850 bits, within 2^14
        Assert.True(Assert.IsType<FloatValue>(V("1/3^10400")).ExactTooLarge); // 16,484 bits
        Assert.IsType<RatioValue>(V("3^-10000"));
    }

    [Fact]
    public void TheDecimalLiteralBudget()
    {
        Assert.IsType<IntValue>(V("1e300"));
        Assert.IsType<RatioValue>(V("1e-300"));
        Assert.IsType<RatioValue>(V("1.5e-4000"));
        Assert.True(Assert.IsType<FloatValue>(V("1e-5000")).ExactTooLarge); // 10^5000 is past 2^14 bits
        var budgets = new ExactBudgets(MultiplyWork: 1 << 20);
        Assert.IsType<IntValue>(V("1e600", budgets));
        Assert.True(Assert.IsType<FloatValue>(V("1e700", budgets)).ExactTooLarge);
    }

    [Theory]
    [InlineData("3^100000")]
    [InlineData("1e1000000")]
    [InlineData("1e1000000000000000000")]
    [InlineData("7^(10^9)")]
    public void PastTheDefaultBudgetsItsAFloatAtOnce(string source)
    {
        // Checked from bit lengths before anything is computed (Matthew, 2026-11-27): 3^100000 exactly would take 2.4 s.
        var clock = Stopwatch.StartNew();
        var value = Assert.IsType<FloatValue>(V(source));
        Assert.True(value.ExactTooLarge);
        Assert.True(clock.ElapsedMilliseconds < 1000, $"{source}: {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void TwoToTheTrillionLightsNoInexactLamp()
    {
        var outcome = Run("2^(10^12)");
        Assert.True(Assert.IsType<FloatValue>(outcome.Value).ExactTooLarge);
        Assert.Equal(IeeeFlags.None, outcome.Flags);
    }

    [Fact]
    public void TwoToTheMillionIsExactAndQuick()
    {
        var clock = Stopwatch.StartNew();
        var v = Assert.IsType<IntValue>(V("2^1000000"));
        Assert.Equal(1_000_001, v.Number.BitLength);
        Assert.True(clock.ElapsedMilliseconds < 1000, $"{clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void MullersSequenceStaysExactToOneHundred()
    {
        // x(n+1) = 108 - (815 - 1500/x(n-1))/x(n), from 4 and 4.25. Exactly it goes to 5; in floats it goes to 100.
        var exact = new ExactArithmetic(Target.Bits(53), RoundingMode.ToNearestEven);
        var floats = new FloatArithmetic(Target.Bits(53), RoundingMode.ToNearestEven);
        Value Step(IArithmetic a, Value previous, Value current) =>
            a.Subtract(a.Literal("108", default), a.Divide(a.Subtract(a.Literal("815", default), a.Divide(a.Literal("1500", default), previous, default), default), current, default), default);

        Value e0 = exact.Literal("4", default), e1 = exact.Literal("4.25", default);
        Value f0 = floats.Literal("4", default), f1 = floats.Literal("4.25", default);
        Rational r0 = new(4, 1), r1 = new(17, 4);
        for (int n = 2; n <= 100; n++)
        {
            (e0, e1) = (e1, Step(exact, e0, e1));
            (f0, f1) = (f1, Step(floats, f0, f1));
            (r0, r1) = (r1, new Rational(108, 1) - (new Rational(815, 1) - new Rational(1500, 1) / r0) / r1);
        }
        Assert.IsType<RatioValue>(e1);
        Assert.True(AsRational(e1) == r1);
        double last = (double)Big(((RatioValue)e1).Numerator) / (double)Big(((RatioValue)e1).Denominator);
        Assert.InRange(last, 4.99, 5.01);
        Assert.InRange((double)((FloatValue)f1).Number, 99.9, 100.1); // the famous wrong answer, with Exact off
    }

    [Fact]
    public void ExactOffUsesFloatsAndAFormatAlwaysDoes()
    {
        Assert.IsType<FloatValue>(Evaluator.Run("0.1+0.2", Bits256, RoundingMode.ToNearestEven, exact: false).Value);
        Assert.IsType<FloatValue>(Evaluator.Run("0.1+0.2", Target.Of(IeeeFormat.Binary64), RoundingMode.ToNearestEven, exact: true).Value);
        Assert.Throws<ArgumentException>(() => new ExactArithmetic(Target.Of(IeeeFormat.Binary64), RoundingMode.ToNearestEven));
    }

    [Fact]
    public void FloatsTakeExactOperandsConvertingThemAtTheTarget()
    {
        // An exact Ans with Exact off: 1/3 rounds once to 53 bits, then times 3.
        var third = V("1/3");
        var outcome = Evaluator.Run("Ans*3", Target.Bits(53), RoundingMode.ToNearestEven, third, exact: false);
        Assert.Equal(1.0, (double)((FloatValue)outcome.Value).Number);
        Assert.Equal(IeeeFlags.Inexact, outcome.Flags);
        var alone = Evaluator.Run("Ans", Target.Bits(53), RoundingMode.ToNearestEven, third, exact: false).Value;
        Assert.Equal(1.0 / 3, (double)Assert.IsType<FloatValue>(alone).Number);
        // A mixed operation converts the exact side at the target (B8 makes this honest).
        Assert.IsType<FloatValue>(V("1/3 + sqrt(2)"));
    }

    [Fact]
    public void AnExactIntegerPowerPastTheBudgetKeepsItsExponentExact()
    {
        // (-3)^(2^60 + 1) at 53 bits: past the budget, so the float path does it, and must see the odd exponent. Rounded to
        // 53 bits first, the exponent would be 2^60, even, and the answer positive.
        var exact = Evaluator.Run("(-3)^(2^60+1)", Target.Bits(53), RoundingMode.ToNearestEven, exact: true).Value;
        var number = Assert.IsType<FloatValue>(exact);
        Assert.True(number.ExactTooLarge);
        Assert.True(number.Number.IsNegative);
        var floats = Evaluator.Run("(-3)^(2^60+1)", Target.Bits(53), RoundingMode.ToNearestEven, exact: false).Value;
        Assert.False(((FloatValue)floats).Number.IsNegative); // Exact off: 2^60 + 1 rounds to 2^60 as a literal sum
        Assert.Equal(ErrorKind.Overflow, Assert.Throws<CalcError>(() => Run("3^(2^70+1)")).Kind);
    }

    [Fact]
    public void TextForExactValues()
    {
        var third = ResultText.Of(V("1/3"), Bits256);
        Assert.Equal("exact", third.Kind);
        Assert.Equal("0." + new string('3', 76), third.Main);
        var big = ResultText.Of(V("2^1000"), Bits256);
        Assert.Equal(BigInteger.Pow(2, 1000).ToString(), big.Main);
        Assert.Equal(302, big.Digits);
        Assert.True(big.Short.Length < 40);
        var huge = ResultText.Of(V("2^100000"), Bits256);
        Assert.False(huge.Complete);
        Assert.Equal("float (exact too large)", ResultText.Of(V("3^100000"), Bits256).Kind);
        Assert.Equal("float", ResultText.Of(V("sqrt(2)"), Bits256).Kind);
    }
}
