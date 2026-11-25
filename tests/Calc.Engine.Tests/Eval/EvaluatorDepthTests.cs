using Calc.Engine.Eval;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Tests.Eval;

/// <summary>The evaluator on the deepest trees the parser lets through, on a 256 MiB stack like the worker's (rule 13).</summary>
[Collection("Deep stacks")]
public class EvaluatorDepthTests
{
    private static string Wrapped(string open, string inner, string close, int times) =>
        string.Concat(Enumerable.Repeat(open, times)) + inner + string.Concat(Enumerable.Repeat(close, times));

    [Fact]
    public void A199999TermSumEvaluates() => OnStack(256, () =>
    {
        var outcome = Evaluator.Run(string.Join("+", Enumerable.Repeat("1", 199_999)), Target.Bits(256), RoundingMode.ToNearestEven);
        Assert.Equal(0, ((FloatValue)outcome.Value).Number.CompareTo(199_999));
    });

    [Theory]
    [InlineData("-", "1", "")]
    [InlineData("(", "1", ")")]
    [InlineData("+", "1", "")]
    public void TheDeepestNestingEvaluates(string open, string inner, string close) => OnStack(256, () =>
        Evaluator.Run(Wrapped(open, inner, close, 199_998), Target.Bits(256), RoundingMode.ToNearestEven));

    [Fact]
    public void TheDeepestCallsComeBackAsACleanError() => OnStack(256, () =>
    {
        // Calls aren't built yet, but the arguments are evaluated first, all 199,998 deep.
        var error = Assert.Throws<CalcError>(() => Evaluator.Run(Wrapped("f(", "1", ")", 199_998), Target.Bits(256), RoundingMode.ToNearestEven));
        Assert.Equal(ErrorKind.NotAvailable, error.Kind);
    });

    [Fact]
    public void OnASmallStackADeepTreeIsAClean_TooBig()
    {
        Node tree = null!;
        OnStack(256, () => tree = Parser.Parse(string.Join("+", Enumerable.Repeat("1", 150_000))));
        OnStack(1, () =>
        {
            var evaluator = new Evaluator(new FloatArithmetic(Target.Bits(64), RoundingMode.ToNearestEven), new Names());
            var error = Assert.Throws<CalcError>(() => evaluator.Evaluate(tree));
            Assert.Equal(ErrorKind.TooBig, error.Kind);
            Assert.Equal("This is nested too deeply to work through.", error.Message);
        });
    }

    private static void OnStack(int mebibytes, Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { failure = e; }
        }, mebibytes << 20);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(120)), "ran for 120 s");
        if (failure is not null) throw failure;
    }
}
