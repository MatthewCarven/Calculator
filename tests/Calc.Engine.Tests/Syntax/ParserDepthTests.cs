using Calc.Syntax;

namespace Calc.Engine.Tests.Syntax;

/// <summary>
/// Runs the classes that build very deep stacks on their own. Every garbage collection scans every thread's stack, so
/// two 200,000-frame stacks side by side, or one beside the fuzzes, slowed each other from about 2 s a test to 20 s.
/// </summary>
[CollectionDefinition("Deep stacks", DisableParallelization = true)]
public class DeepStacks;

/// <summary>The depth limit, on a 256 MiB stack like the worker's compute thread (rule 13). A default stack dies far sooner.</summary>
[Collection("Deep stacks")]
public class ParserDepthTests
{
    private static string Sum(int terms) => string.Join("+", Enumerable.Repeat("1", terms));

    private static string Wrapped(string open, string inner, string close, int times) =>
        string.Concat(Enumerable.Repeat(open, times)) + inner + string.Concat(Enumerable.Repeat(close, times));

    [Fact]
    public void A199999TermSumParsesAndPrints() => OnBigStack(() =>
    {
        string source = Sum(199_999);
        Node tree = Parser.Parse(source);
        Assert.Equal(199_999, tree.Depth);
        Assert.Equal(source.Replace("+", " + "), Printer.Print(tree));
    });

    [Fact]
    public void TheLimitIsExactly200000Levels() => OnBigStack(() =>
    {
        Assert.Equal(200_000, Parser.Parse(Sum(200_000)).Depth);
        var error = Assert.Throws<CalcError>(() => Parser.Parse(Sum(200_001)));
        Assert.Equal(ErrorKind.TooBig, error.Kind);
        Assert.Equal(new TextSpan(399_999, 400_000), error.Span); // the 200,000th +, which made the 200,001st level

        Assert.Equal(1, Parser.Parse(Wrapped("(", "1", ")", 199_999)).Depth);
        Assert.Throws<CalcError>(() => Parser.Parse(Wrapped("(", "1", ")", 200_000)));
    });

    [Fact]
    public void WideIsNotDeep()
    {
        // 300,000 arguments are 300,000 sub-expressions, but only two levels: the counter must come back down after each.
        // A flat sum can't show this, because its depth is its length. The battery's P11 survived until this test came.
        Node call = Parser.Parse("f(" + string.Join(",", Enumerable.Repeat("1", 300_000)) + ")");
        Assert.Equal(2, call.Depth);
        Assert.Equal(300_000, ((CallNode)call).Arguments.Count);
    }

    [Fact]
    public void AMillionTermSumIsACleanError() => OnBigStack(() =>
    {
        var error = Assert.Throws<CalcError>(() => Parser.Parse(Sum(1_000_000)));
        Assert.Equal(ErrorKind.TooBig, error.Kind);
        Assert.StartsWith("This is nested too deeply: more than 200,000 levels.", error.Message);
        Assert.Equal(new TextSpan(399_999, 400_000), error.Span);
    });

    [Theory]
    [InlineData("(", "1", ")")]
    [InlineData("-", "1", "")]
    [InlineData("2^", "2", "")]
    [InlineData("f(", "1", ")")]
    public void AMillionLevelsOfRecursionIsACleanError(string open, string inner, string close) => OnBigStack(() =>
    {
        var error = Assert.Throws<CalcError>(() => Parser.Parse(Wrapped(open, inner, close, 1_000_000)));
        Assert.Equal(ErrorKind.TooBig, error.Kind);
        Assert.NotNull(error.Span);
    });

    [Theory]
    [InlineData("(", "1", ")")]
    [InlineData("-", "1", "")]
    [InlineData("2^", "2", "")]
    [InlineData("f(", "1", ")")]
    public void JustUnderTheLimitParsesOnTheBigStack(string open, string inner, string close) => OnBigStack(() =>
        Parser.Parse(Wrapped(open, inner, close, 199_998)));

    [Theory]
    [InlineData("(", "1", ")")]
    [InlineData("f(", "1", ")")]
    [InlineData("-", "1", "")]
    public void OnASmallStackItRefusesBeforeTheStackRunsOut(string open, string inner, string close)
    {
        // 199,998 levels need up to 157 MiB in Debug. On 1 MiB the stack guard has to refuse, cleanly, long before the counter would.
        CalcError? error = null;
        var thread = new Thread(() =>
        {
            try { Parser.Parse(Wrapped(open, inner, close, 199_998)); }
            catch (CalcError e) { error = e; }
        }, 1 << 20);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "ran for 60 s");
        Assert.NotNull(error);
        Assert.Equal(ErrorKind.TooBig, error.Kind);
        Assert.Equal("This is nested too deeply to work through.", error.Message);
    }

    private static void OnBigStack(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { failure = e; }
        }, 256 << 20);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(120)), "ran for 120 s");
        if (failure is not null) throw failure;
    }
}
