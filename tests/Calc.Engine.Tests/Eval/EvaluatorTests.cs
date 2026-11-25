using System.Globalization;
using System.Numerics;
using Calc.Engine.Eval;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Tests.Eval;

public class EvaluatorTests
{
    private static readonly Target Bits256 = Target.Bits(256);
    private static readonly Target Bits53 = Target.Bits(53);
    private static readonly Target Binary64 = Target.Of(IeeeFormat.Binary64);

    private static ApFloat Number(string source, Target target, RoundingMode mode = RoundingMode.ToNearestEven, Value? ans = null) =>
        ((FloatValue)Evaluator.Run(source, target, mode, ans).Value).Number;

    private static CalcError Error(string source, Target target, Value? ans = null) =>
        Assert.Throws<CalcError>(() => Evaluator.Run(source, target, RoundingMode.ToNearestEven, ans));

    [Fact]
    public void PointOnePlusPointTwoAt53Bits()
    {
        var outcome = Evaluator.Run("0.1+0.2", Bits53, RoundingMode.ToNearestEven);
        Assert.Equal(0.1 + 0.2, (double)((FloatValue)outcome.Value).Number);
        Assert.Equal("0.30000000000000004", outcome.Value.ToString());
        Assert.Equal(IeeeFlags.Inexact, outcome.Flags);
    }

    [Theory]
    [InlineData("0x1.8p+1", "3")]
    [InlineData("0X1.8P+1", "3")]
    [InlineData("0xFF", "255")]
    [InlineData("0x1e5", "485")]
    [InlineData("0b101", "5")]
    [InlineData("0B101", "5")]
    [InlineData("0b101.01p-3", "0.65625")]
    [InlineData("0o17", "15")]
    [InlineData("0O17", "15")]
    [InlineData("0o1_7", "15")]
    [InlineData("1_000", "1000")]
    [InlineData("1_000.000_5", "1000.0005")]
    [InlineData("1e1_0", "10000000000")]
    [InlineData("1.", "1")]
    [InlineData(".5", "0.5")]
    [InlineData("1E-5", "0.00001")]
    [InlineData("007", "7")]
    public void LiteralsInEveryForm(string literal, string expected) =>
        Assert.Equal(0, Number(literal, Bits256).CompareTo(ApFloat.Parse(expected, CultureInfo.InvariantCulture)));

    [Fact]
    public void ADecimalLiteralRoundsOnceStraightToTheTarget()
    {
        // 1 + 2^-53 is the midpoint between two 53-bit values; this is 10^-90 above it. Rounded first to 256 bits, it would
        // land exactly on the midpoint, then go down by ties-to-even. Rounded once, it goes up, as double.Parse does.
        const string midpoint = "1.00000000000000011102230246251565404236316680908203125";
        string literal = midpoint + new string('0', 90 - 53) + "1";
        Assert.Equal(double.Parse(literal, CultureInfo.InvariantCulture), (double)Number(literal, Bits53));
        Assert.Equal(1.0000000000000002, (double)Number(literal, Bits53));
    }

    [Fact]
    public void AWholeLiteralRoundsOnceStraightToTheTarget()
    {
        // (2^53 + 1)·2^300 + 1: just above a 53-bit midpoint, by far less than a 256-bit unit.
        BigInteger n = ((BigInteger.One << 53) + 1) * (BigInteger.One << 300) + 1;
        BigInteger up = ((BigInteger.One << 53) + 2) * (BigInteger.One << 300);
        Assert.Equal(0, Number(n.ToString(), Bits53).CompareTo((ApFloat)ApInt.Parse(up.ToString(), CultureInfo.InvariantCulture)));
        Assert.Equal(0, Number("0x" + n.ToString("x"), Bits53).CompareTo((ApFloat)ApInt.Parse(up.ToString(), CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ALongWholeLiteralMatchesDoubleParse()
    {
        const string literal = "123456789012345678901234567890";
        Assert.Equal(double.Parse(literal, CultureInfo.InvariantCulture), (double)Number(literal, Binary64));
    }

    [Theory]
    [InlineData(RoundingMode.ToNearestEven, 0.3333333333333333)]
    [InlineData(RoundingMode.TowardZero, 0.3333333333333333)]
    [InlineData(RoundingMode.TowardNegative, 0.3333333333333333)]
    [InlineData(RoundingMode.TowardPositive, 0.33333333333333337)]
    public void TheModeIsUsed(RoundingMode mode, double expected)
    {
        Assert.Equal(expected, (double)Number("1/3", Bits53, mode));
        Assert.Equal(-expected, (double)Number("-1/3", Bits53, Opposite(mode)));
    }

    private static RoundingMode Opposite(RoundingMode mode) => mode switch
    {
        RoundingMode.TowardPositive => RoundingMode.TowardNegative,
        RoundingMode.TowardNegative => RoundingMode.TowardPositive,
        _ => mode,
    };

    [Fact]
    public void EveryResultIsAtTheTarget()
    {
        Assert.Equal(53, Number("1/3", Bits53).Precision);
        Assert.Equal(53, Number("3", Bits53).Precision);
        Assert.Equal(300, Number("1+2*3-4/5", Target.Bits(300)).Precision);
    }

    [Theory]
    [InlineData("1+1", IeeeFlags.None)]
    [InlineData("1/3", IeeeFlags.Inexact)]
    [InlineData("0.1", IeeeFlags.Inexact)]
    public void FlagsAtBits(string source, IeeeFlags expected) =>
        Assert.Equal(expected, Evaluator.Run(source, Bits53, RoundingMode.ToNearestEven).Flags);

    [Theory]
    [InlineData("1/0", double.PositiveInfinity, IeeeFlags.DivideByZero)]
    [InlineData("-1/0", double.NegativeInfinity, IeeeFlags.DivideByZero)]
    [InlineData("0/0", double.NaN, IeeeFlags.Invalid)]
    [InlineData("1e308*10", double.PositiveInfinity, IeeeFlags.Overflow | IeeeFlags.Inexact)]
    [InlineData("1e-308/1e10", 1e-308 / 1e10, IeeeFlags.Underflow | IeeeFlags.Inexact)]
    [InlineData("1/0 - 1/0", double.NaN, IeeeFlags.DivideByZero | IeeeFlags.Invalid)]
    public void AtAFormatInfinitiesAndNaNStandAsValues(string source, double expected, IeeeFlags flags)
    {
        var outcome = Evaluator.Run(source, Binary64, RoundingMode.ToNearestEven);
        double actual = (double)((FloatValue)outcome.Value).Number;
        Assert.True(double.IsNaN(expected) ? double.IsNaN(actual) : BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual),
            $"expected {expected:R}, got {actual:R}");
        Assert.Equal(flags, outcome.Flags);
    }

    [Fact]
    public void ZeroTimesMinusOneIsMinusZero()
    {
        var zero = Number("0 * -1", Bits256);
        Assert.True(zero.IsZero && zero.IsNegative);
    }

    [Fact]
    public void UnaryPlusAndImplicitProductsWork()
    {
        Assert.Equal(0, Number("+2", Bits256).CompareTo(2));
        Assert.Equal(0, Number("(2)(3)", Bits256).CompareTo(6));
    }

    [Fact]
    public void CompareOrdersAndFindsNaNUnordered()
    {
        var floats = new FloatArithmetic(Binary64, RoundingMode.ToNearestEven);
        Value V(string literal) => floats.Literal(literal, default);
        Value nan = Evaluator.Run("0/0", Binary64, RoundingMode.ToNearestEven).Value;
        Assert.Equal(-1, floats.Compare(V("1"), V("2")));
        Assert.Equal(1, floats.Compare(V("2e10"), V("1")));
        Assert.Equal(0, floats.Compare(V("0.5"), V(".5")));
        Assert.Equal(0, floats.Compare(V("0"), floats.Negate(V("0"), default)));
        Assert.Null(floats.Compare(nan, V("1")));
        Assert.Null(floats.Compare(V("1"), nan));
    }

    [Fact]
    public void AnsIsTheGivenValue()
    {
        var ans = Evaluator.Run("1/3", Bits256, RoundingMode.ToNearestEven).Value;
        Assert.Equal(0, Number("Ans*3", Bits256, ans: ans).CompareTo(1));
    }

    // One test per error kind, and the span each points at.

    [Fact]
    public void AnUnknownNameIsASyntaxError()
    {
        var error = Error("2 + y", Bits256);
        Assert.Equal(ErrorKind.Syntax, error.Kind);
        Assert.Equal(new TextSpan(4, 5), error.Span);
        Assert.Equal("Unknown name 'y'.", error.Message);
    }

    [Theory]
    [InlineData("1/0", 1, 2)]
    [InlineData("0/0", 1, 2)]
    [InlineData("2 + 1/(1-1)", 5, 6)]
    public void DivisionByZeroAtBitsIsAnError(string source, int start, int end)
    {
        var error = Error(source, Bits256);
        Assert.Equal(ErrorKind.DivideByZero, error.Kind);
        Assert.Equal(new TextSpan(start, end), error.Span);
    }

    [Fact]
    public void InvalidAtBitsIsADomainError()
    {
        // Only a value from outside can be infinite at bits: here an infinite Ans.
        var infinity = new FloatValue(new IeeeContext(256).Parse("Infinity"));
        var error = Error("Ans - Ans", Bits256, infinity);
        Assert.Equal(ErrorKind.Domain, error.Kind);
        Assert.Equal(new TextSpan(4, 5), error.Span);
    }

    [Fact]
    public void ExponentOverflowAtBitsIsAnOverflowError()
    {
        var error = Error("1e2000000000000000000 * 1e2000000000000000000", Bits256);
        Assert.Equal(ErrorKind.Overflow, error.Kind);
        Assert.Equal(new TextSpan(22, 23), error.Span);
        Assert.Equal(ErrorKind.Overflow, Error("1e2776511644261678567", Bits256).Kind);
    }

    [Theory]
    [InlineData("2^3", 1, 2)]
    [InlineData("2²", 1, 2)]
    [InlineData("3!", 1, 2)]
    [InlineData("50%", 2, 3)]
    [InlineData("7 mod 2", 2, 5)]
    [InlineData("7 rem 2", 2, 5)]
    [InlineData("5 & 3", 2, 3)]
    [InlineData("1 << 3", 2, 4)]
    [InlineData("~5", 0, 1)]
    [InlineData("√4", 0, 1)]
    [InlineData("sqrt(4)", 0, 7)]
    [InlineData("π", 0, 1)]
    [InlineData("2e", 1, 2)]
    [InlineData("255 to hex", 4, 6)]
    [InlineData("Ans", 0, 3)]
    public void NotYetBuiltIsNotAvailable(string source, int start, int end)
    {
        var error = Error(source, Bits256);
        Assert.Equal(ErrorKind.NotAvailable, error.Kind);
        Assert.Equal(new TextSpan(start, end), error.Span);
    }

    [Fact]
    public void AnAnsFromAnotherTargetIsRefused()
    {
        var ans = Evaluator.Run("1/3", Bits53, RoundingMode.ToNearestEven).Value;
        var error = Error("Ans", Bits256, ans);
        Assert.Equal(ErrorKind.Internal, error.Kind);
    }

    [Fact]
    public void LeftIsEvaluatedBeforeRight()
    {
        Assert.Equal(new TextSpan(1, 2), Error("1/0 + 1/0", Bits256).Span);
        Assert.Equal(new TextSpan(0, 1), Error("y + 1/0", Bits256).Span);
        Assert.Equal(new TextSpan(4, 5), Error("1 + y + z", Bits256).Span);
    }

    [Theory]
    [InlineData(typeof(FormatException), ErrorKind.Syntax)]
    [InlineData(typeof(OverflowException), ErrorKind.Overflow)]
    [InlineData(typeof(DivideByZeroException), ErrorKind.DivideByZero)]
    [InlineData(typeof(ArithmeticException), ErrorKind.Domain)]
    [InlineData(typeof(OutOfMemoryException), ErrorKind.TooBig)]
    [InlineData(typeof(InsufficientExecutionStackException), ErrorKind.TooBig)]
    [InlineData(typeof(OperationCanceledException), ErrorKind.Stopped)]
    [InlineData(typeof(ArgumentOutOfRangeException), ErrorKind.Internal)]
    [InlineData(typeof(ArgumentException), ErrorKind.Internal)]
    [InlineData(typeof(InvalidOperationException), ErrorKind.Internal)]
    [InlineData(typeof(NotSupportedException), ErrorKind.Internal)]
    public void ErrorMapFollowsReportOnesTable(Type exception, ErrorKind kind)
    {
        var span = new TextSpan(3, 4);
        var error = ErrorMap.From((Exception)Activator.CreateInstance(exception)!, span);
        Assert.Equal(kind, error.Kind);
        Assert.Equal(span, error.Span);
    }

    [Fact]
    public void ErrorMapPassesACalcErrorThrough()
    {
        var original = new CalcError(ErrorKind.Domain, "x", new TextSpan(1, 2));
        Assert.Same(original, ErrorMap.From(original, new TextSpan(5, 6)));
    }
}
