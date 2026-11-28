using System.Text.Json;
using Calc.Protocol;

namespace Calc.Worker.Tests;

/// <summary>B9's wire: every message to its line and back, and the lines a hand might write.</summary>
public class MessagesTests
{
    public static TheoryData<Request> Requests => new()
    {
        new EvalRequest(1, "1+1", EvalContext.Default),
        new EvalRequest(2, "Ans*√2", new EvalContext(TargetSpec.Format(11, 53), Rounding.Down, false, AngleUnit.Radians, new BudgetSpec(1000, 100, 1e6)),
                        new AnsValue("r:1/3"), new TextStyle(NotationStyle.Engineering, " ", true)),
        new EvalRequest(3, "Ans", EvalContext.Default, new AnsValue(Held: 7)),
        new DigitsRequest(4, "i:FF", "E99"),
        new ReformatRequest(5, "r:1/3", NotationStyle.Aligned, 20),
        new FetchRequest(6, 2),
        new FunctionsRequest(7),
        new StopRequest(8),
        new PingRequest(9),
    };

    public static TheoryData<Reply> Replies => new()
    {
        new ResultReply(1, "0.3", "0.3", "exact = 3/10", "exact = 3/10", 15, true, [], 1.5, [], "r:3/A"),
        new ResultReply(2, "1.07150860718626E+301", "1.07…", "exact", "exact, about 1,000 digits", 1000, false, ["Inexact", "Overflow"], 2, ["big"], Held: 2, Understood: "2·x"),
        new DigitsReply(3, "0.333"),
        new ValueReply(4, "i:1"),
        new ErrorReply(5, "DivideByZero", "Division by zero.", 1, 2),
        new ErrorReply(null, ErrorKinds.BadRequest, "That isn't a request."),
        new ProgressReply(6, 0.25, 3),
        new ProgressReply(7, 0.5),
        new FunctionsReply(8, [new FunctionInfo("sqrt", 1, 1), new FunctionInfo("exp", 1, 1, "W1")]),
        new PongReply(9, Wire.Version),
    };

    [Theory]
    [MemberData(nameof(Requests))]
    public void ARequestRoundTrips(Request request)
    {
        string line = Messages.Write(request);
        Assert.DoesNotContain('\n', line);
        Assert.Equal(line, Messages.Write(Messages.ReadRequest(line)));
        Assert.Equal(request.GetType(), Messages.ReadRequest(line).GetType());
    }

    [Theory]
    [MemberData(nameof(Replies))]
    public void AReplyRoundTrips(Reply reply)
    {
        string line = Messages.Write(reply);
        Assert.DoesNotContain('\n', line);
        Assert.Equal(line, Messages.Write(Messages.ReadReply(line)));
    }

    [Fact]
    public void TheLinesReadAsAHandWouldWriteThem()
    {
        // The type tag first or not, nulls left out, enums by name, camelCase.
        var eval = Assert.IsType<EvalRequest>(Messages.ReadRequest(
            """{"id":3,"source":"1/3","type":"eval","context":{"target":{"precision":53,"exponentBits":11},"mode":"Up"}}"""));
        Assert.Equal((3L, "1/3", Rounding.Up, true, AngleUnit.Degrees), (eval.Id, eval.Source, eval.Context.Mode, eval.Context.Exact, eval.Context.Angle));
        Assert.Equal(TargetSpec.Format(11, 53), eval.Context.Target);
        Assert.Null(eval.Ans);
        Assert.Equal("""{"type":"pong","id":1,"version":"1"}""", Messages.Write(new PongReply(1, "1")));
        Assert.Equal("""{"type":"error","id":null,"kind":"BadRequest","message":"x"}""", Messages.Write(new ErrorReply(null, ErrorKinds.BadRequest, "x")));
        Assert.Contains("\"understood\":\"(1/2)·x\"", Messages.Write(new ResultReply(1, "", "", "", "", 1, true, [], 0, [], Understood: "(1/2)·x")));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"type":"nosuch","id":1}""")]
    [InlineData("""{"type":"eval","id":1,"context":{"target":{"precision":53}}}""")]
    [InlineData("""{"type":"eval","id":1,"source":null,"context":{"target":{"precision":53}}}""")]
    [InlineData("""{"type":"eval","id":"one","source":"1","context":{"target":{"precision":53}}}""")]
    [InlineData("""{"type":"eval","id":1,"source":"1","context":{"target":{"precision":53},"mode":"Sideways"}}""")]
    public void AMalformedLineThrowsWhatTheLoopCatches(string line)
    {
        Exception? e = Record.Exception(() => Messages.ReadRequest(line));
        Assert.True(e is JsonException or NotSupportedException, $"{line}: {e?.GetType().Name ?? "no exception"}");
    }

    [Fact]
    public void EqualContextsAreEqualKeys()
    {
        // EvalContext keys the worker's caches (B9's brief), so equal settings must make equal records.
        var a = new EvalContext(TargetSpec.Bits(256), Budgets: new BudgetSpec());
        var b = new EvalContext(TargetSpec.Bits(256), Budgets: new BudgetSpec());
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a with { Mode = Rounding.Up });
        Assert.NotEqual(TargetSpec.Bits(53), TargetSpec.Format(11, 53));
        Assert.Equal("11/53", TargetSpec.Format(11, 53).ToString());
    }
}
