using Calc.Engine.Eval;
using Calc.Engine.Text;
using Calc.Engine.Values;
using Calc.Protocol;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Tests.Eval;

/// <summary>B9: Protocol's records to the Engine's types, and the evaluator's soft stop.</summary>
public class ContextsTests
{
    [Theory]
    [InlineData(256, null)]
    [InlineData(1, null)]
    [InlineData(53, 11)]
    [InlineData(237, 19)]
    [InlineData(11, 5)]
    public void TargetsMapBothWays(int precision, int? exponentBits)
    {
        var spec = new TargetSpec(precision, exponentBits);
        Target target = Contexts.TargetOf(spec);
        Assert.Equal(spec, Contexts.SpecOf(target));
        Assert.Equal(exponentBits is not null, target.IsFormat);
        Assert.Equal(precision, target.Precision);
    }

    [Fact]
    public void ABinary64SpecIsTheBinary64Target()
    {
        Assert.Equal(Target.Of(IeeeFormat.Binary64), Contexts.TargetOf(TargetSpec.Format(11, 53)));
        Assert.NotEqual(Target.Bits(53), Contexts.TargetOf(TargetSpec.Format(11, 53)));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(53, 0)]
    public void ATargetNaturalCantMakeIsAnArgumentException(int precision, int? exponentBits) =>
        Assert.ThrowsAny<ArgumentException>(() => Contexts.TargetOf(new TargetSpec(precision, exponentBits)));

    [Fact]
    public void ModesBudgetsOptionsAndFlags()
    {
        Assert.Equal(
            [RoundingMode.ToNearestEven, RoundingMode.TowardZero, RoundingMode.TowardPositive, RoundingMode.TowardNegative],
            Enum.GetValues<Rounding>().Select(Contexts.ModeOf));
        Assert.Same(ExactBudgets.Default, Contexts.BudgetsOf(null));
        Assert.Equal(ExactBudgets.Default, Contexts.BudgetsOf(new BudgetSpec()));
        Assert.Equal(new ExactBudgets(10, 20, 30), Contexts.BudgetsOf(new BudgetSpec(10, 20, 30)));
        Assert.Same(TextOptions.Default, Contexts.OptionsOf(null));
        Assert.Equal(new TextOptions(Notation.Engineering, ",", true), Contexts.OptionsOf(new TextStyle(NotationStyle.Engineering, ",", true)));
        Assert.Equal(Enum.GetNames<Notation>(), Enum.GetNames<NotationStyle>());
        Assert.Empty(Contexts.FlagsOf(IeeeFlags.None));
        Assert.Equal(["Inexact", "Overflow"], Contexts.FlagsOf(IeeeFlags.Overflow | IeeeFlags.Inexact));
    }

    [Fact]
    public void AStoppedTokenStopsBeforeTheFirstNode()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var error = Assert.Throws<CalcError>(() => Evaluator.Run(Parser.Parse("1+1"), Target.Bits(53), RoundingMode.ToNearestEven, null, true, null, stop.Token));
        Assert.Equal(ErrorKind.Stopped, error.Kind);
        Assert.Null(error.Span);
    }

    [Fact]
    public void AStopMidwayEndsAtTheNextNode()
    {
        // The arithmetic cancels the token on its tenth addition; the evaluator must stop at the next node, not finish.
        using var stop = new CancellationTokenSource();
        var counting = new CountingArithmetic(new FloatArithmetic(Target.Bits(53), RoundingMode.ToNearestEven), () => stop.Cancel(), 10);
        Node tree = Parser.Parse(string.Join("+", Enumerable.Repeat("1", 100)));
        var error = Assert.Throws<CalcError>(() => new Evaluator(counting, new Names(), stop.Token).Evaluate(tree));
        Assert.Equal(ErrorKind.Stopped, error.Kind);
        Assert.Equal(10, counting.Additions);
    }

    /// <summary>Float arithmetic that runs an action on its nth addition.</summary>
    private sealed class CountingArithmetic(FloatArithmetic inner, Action action, int at) : IArithmetic
    {
        public int Additions { get; private set; }
        public IeeeFlags Flags => inner.Flags;
        public Value Literal(string text, TextSpan span) => inner.Literal(text, span);
        public Value Import(Value value, TextSpan span) => inner.Import(value, span);
        public Value Add(Value a, Value b, TextSpan span)
        {
            if (++Additions == at) action();
            return inner.Add(a, b, span);
        }
        public Value Subtract(Value a, Value b, TextSpan span) => inner.Subtract(a, b, span);
        public Value Multiply(Value a, Value b, TextSpan span) => inner.Multiply(a, b, span);
        public Value Divide(Value a, Value b, TextSpan span) => inner.Divide(a, b, span);
        public Value Negate(Value a, TextSpan span) => inner.Negate(a, span);
        public Value Power(Value a, Value b, TextSpan span) => inner.Power(a, b, span);
        public Value Mod(Value a, Value b, TextSpan span) => inner.Mod(a, b, span);
        public Value Rem(Value a, Value b, TextSpan span) => inner.Rem(a, b, span);
        public Value Percent(Value a, TextSpan span) => inner.Percent(a, span);
        public Value Factorial(Value a, TextSpan span) => inner.Factorial(a, span);
        public Value And(Value a, Value b, TextSpan span) => inner.And(a, b, span);
        public Value Or(Value a, Value b, TextSpan span) => inner.Or(a, b, span);
        public Value Xor(Value a, Value b, TextSpan span) => inner.Xor(a, b, span);
        public Value ShiftLeft(Value a, Value b, TextSpan span) => inner.ShiftLeft(a, b, span);
        public Value ShiftRight(Value a, Value b, TextSpan span) => inner.ShiftRight(a, b, span);
        public Value Not(Value a, TextSpan span) => inner.Not(a, span);
        public int? Compare(Value a, Value b) => inner.Compare(a, b);
        public Value Call(string name, IReadOnlyList<Value> arguments, TextSpan span) => inner.Call(name, arguments, span);
    }
}
