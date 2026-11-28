using System.Diagnostics;
using System.Text;
using Calc.Protocol;

namespace Calc.Worker.Tests;

/// <summary>B9: the real calc-worker's stdio loop, through every request, and everything a line or an evaluation can do to it.</summary>
public class LoopTests
{
    private static EvalContext Bits(int bits) => new(TargetSpec.Bits(bits));

    /// <summary>A sum of 30 exact powers, each about a quarter of a second in Debug: long, but made of many nodes.</summary>
    private static readonly string Slow = string.Join("+", Enumerable.Range(0, 30).Select(i => $"3^{15000 + i}"));

    [Fact]
    public async Task ARoundTrip()
    {
        using var worker = WorkerProcess.Start();
        worker.Send(new PingRequest(1));
        var pong = await worker.NextAsync<PongReply>();
        Assert.Equal(1, pong.Id);
        Assert.Equal(Wire.Version, pong.Version);

        var two = await worker.ResultAsync(2, "1+1");
        Assert.Equal(("2", "2", "exact", "exact, 1 digit", "i:2"), (two.Short, two.Main, two.Kind, two.Caption, two.Lossless));
        Assert.Null(two.Held);
        Assert.Empty(two.Flags);
        Assert.True(two.Ms >= 0);
        Assert.Null(two.Understood);

        var error = Assert.IsType<ErrorReply>(await worker.EvalAsync(3, "1/0"));
        Assert.Equal((3L, "DivideByZero", 1, 2), (error.Id!.Value, error.Kind, error.From!.Value, error.To!.Value));

        var implicitProduct = await worker.ResultAsync(4, "6/2(1+2)");
        Assert.Equal("9", implicitProduct.Main);
        Assert.Equal("(6/2)·(1 + 2)", implicitProduct.Understood);
    }

    [Fact]
    public async Task IdsAreEchoedInOrder()
    {
        using var worker = WorkerProcess.Start();
        long[] ids = [907, 3, 55, 1_000_000_000_000, 4];
        foreach (long id in ids) worker.Send(new EvalRequest(id, $"{id % 1000}+1", EvalContext.Default));
        foreach (long id in ids)
        {
            var result = await worker.NextAsync<ResultReply>();
            Assert.Equal(id, result.Id);
            Assert.Equal($"{id % 1000 + 1}", result.Main);
        }
    }

    [Fact]
    public async Task TwoHundredEvaluationsInARow()
    {
        using var worker = WorkerProcess.Start();
        for (int i = 1; i <= 200; i++) worker.Send(new EvalRequest(i, $"{i}*2", Bits(53)));
        for (int i = 1; i <= 200; i++)
        {
            var result = await worker.NextAsync<ResultReply>();
            Assert.Equal((i, $"{2 * i}"), (result.Id, result.Main));
        }
    }

    [Fact]
    public async Task AMalformedLineGetsAnErrorAndTheLoopGoesOn()
    {
        using var worker = WorkerProcess.Start();
        string[] lines =
        [
            "garbage",
            "{}",
            "[1, 2]",
            """{"type":"nosuch","id":5}""",
            """{"type":"eval","id":6}""",
            """{"type":"eval","id":7,"source":"1","context":{"target":{"precision":0}}}""",
            """{"type":"eval","id":8,"source":"1","context":{"target":{"precision":53}},"ans":{"lossless":"x:1"}}""",
            """{"type":"eval","id":9,"source":"Ans","context":{"target":{"precision":53}},"ans":{}}""",
            """{"type":"eval","id":10,"source":null,"context":{"target":{"precision":53}}}""",
        ];
        foreach (string line in lines) worker.SendLine(line);
        var errors = new List<ErrorReply>();
        for (int i = 0; i < lines.Length; i++) errors.Add(await worker.NextAsync<ErrorReply>());
        Assert.All(errors, e => Assert.Equal(ErrorKinds.BadRequest, e.Kind));
        Assert.Equal(3, errors.Count(e => e.Id is null)); // no readable id
        Assert.Equal([5L, 6, 7, 8, 9, 10], errors.Where(e => e.Id is not null).Select(e => e.Id!.Value).Order());

        // Still answering.
        Assert.Equal("4", (await worker.ResultAsync(11, "2+2")).Main);
    }

    [Fact]
    public async Task StopEndsAnEvaluationBetweenNodes()
    {
        using var worker = WorkerProcess.Start();
        worker.Send(new EvalRequest(1, Slow, EvalContext.Default));
        await Task.Delay(600);
        var clock = Stopwatch.StartNew();
        worker.Send(new StopRequest(1));
        var stopped = await worker.NextAsync<ErrorReply>(TimeSpan.FromSeconds(10));
        Assert.Equal((1L, "Stopped"), (stopped.Id!.Value, stopped.Kind));
        // Within one node of the stop: a power takes about a quarter of a second, where the whole sum takes several.
        Assert.True(clock.ElapsedMilliseconds < 3000, $"{clock.ElapsedMilliseconds} ms after the stop");
        Assert.Equal("2", (await worker.ResultAsync(2, "1+1")).Main);
    }

    [Fact]
    public async Task AStoppedQueuedEvaluationNeverStarts()
    {
        using var worker = WorkerProcess.Start();
        worker.Send(new EvalRequest(1, Slow, EvalContext.Default));
        worker.Send(new EvalRequest(2, Slow, EvalContext.Default));
        worker.Send(new EvalRequest(3, "5", EvalContext.Default));
        await Task.Delay(300);
        worker.Send(new StopRequest(2));
        worker.Send(new StopRequest(1));
        worker.Send(new StopRequest(77)); // neither queued nor running: ignored
        var clock = Stopwatch.StartNew();
        var first = await worker.NextAsync<ErrorReply>(TimeSpan.FromSeconds(10));
        var second = await worker.NextAsync<ErrorReply>(TimeSpan.FromSeconds(10));
        var third = await worker.NextAsync<ResultReply>(TimeSpan.FromSeconds(10));
        Assert.Equal((1L, "Stopped"), (first.Id!.Value, first.Kind));
        Assert.Equal((2L, "Stopped"), (second.Id!.Value, second.Kind));
        Assert.Equal((3L, "5"), (third.Id, third.Main));
        Assert.True(clock.ElapsedMilliseconds < 3000, $"{clock.ElapsedMilliseconds} ms: the second sum ran");
    }

    [Fact]
    public async Task PingIsAnsweredDuringALongEvaluation()
    {
        using var worker = WorkerProcess.Start();
        worker.Send(new EvalRequest(1, Slow, EvalContext.Default));
        await Task.Delay(300);
        var clock = Stopwatch.StartNew();
        worker.Send(new PingRequest(2));
        var pong = await worker.NextAsync<PongReply>(TimeSpan.FromSeconds(5));
        Assert.Equal(2, pong.Id);
        Assert.True(clock.ElapsedMilliseconds < 1000, $"{clock.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingStdinEndsTheProcessWithinASecond(bool busy)
    {
        using var worker = WorkerProcess.Start();
        Assert.Equal("2", (await worker.ResultAsync(1, "1+1")).Main);
        if (busy)
        {
            worker.Send(new EvalRequest(2, Slow, EvalContext.Default));
            await Task.Delay(300);
        }
        var clock = Stopwatch.StartNew();
        worker.CloseInput();
        Assert.True(worker.Process.WaitForExit(5000), "still running 5 s after stdin closed");
        Assert.True(clock.ElapsedMilliseconds < 1000, $"{clock.ElapsedMilliseconds} ms");
        Assert.Equal(0, worker.Process.ExitCode);
    }

    [Fact]
    public async Task An830000CharacterReplyArrivesIntact()
    {
        using var worker = WorkerProcess.Start();
        var result = await worker.ResultAsync(1, "2^3320000");
        Assert.Equal(830_003, result.Lossless!.Length);
        Assert.True(result.Lossless == "i:1" + new string('0', 830_000), "the lossless text came back changed");
        Assert.Equal("2", (await worker.ResultAsync(2, "1+1")).Main); // and the line after it is whole too
    }

    [Fact]
    public async Task AnExactAnsStaysExactThroughItsLosslessText()
    {
        using var worker = WorkerProcess.Start();
        var third = await worker.ResultAsync(1, "1/3");
        Assert.Equal("r:1/3", third.Lossless);
        var one = await worker.ResultAsync(2, "Ans*3", ans: new AnsValue(third.Lossless));
        Assert.Equal(("1", "exact", "i:1"), (one.Main, one.Kind, one.Lossless));

        var big = await worker.ResultAsync(3, "2^1000000");
        Assert.Equal("i:1" + new string('0', 250_000), big.Lossless);
        var next = await worker.ResultAsync(4, "Ans+1", ans: new AnsValue(big.Lossless));
        Assert.Equal("exact", next.Kind);
        Assert.True(next.Lossless == "i:1" + new string('0', 249_999) + "1", "2^1000000 + 1 isn't exact");

        // An exact Ans is fine at any target: it converts at use.
        var floatThird = await worker.ResultAsync(5, "Ans", Bits(53) with { Exact = false }, new AnsValue(third.Lossless));
        Assert.Equal("f:53:0X1.5555555555555P-2", floatThird.Lossless);
    }

    [Fact]
    public async Task AFloatAnsFromAnotherTargetIsRefused()
    {
        using var worker = WorkerProcess.Start();
        var root = await worker.ResultAsync(1, "sqrt(2)", Bits(53));
        Assert.StartsWith("f:53:", root.Lossless);
        var same = await worker.ResultAsync(2, "Ans", Bits(53), new AnsValue(root.Lossless));
        Assert.Equal(root.Lossless, same.Lossless);
        foreach (var other in new[] { Bits(64), new EvalContext(TargetSpec.Format(11, 53)) })
        {
            var refused = Assert.IsType<ErrorReply>(await worker.EvalAsync(3, "Ans", other, new AnsValue(root.Lossless)));
            Assert.Equal((3L, ErrorKinds.BadRequest), (refused.Id!.Value, refused.Kind));
            Assert.Contains("rule 14", refused.Message);
        }
    }

    [Fact]
    public async Task AHugeValueIsHeldAndCanBeNamedAndFetched()
    {
        using var worker = WorkerProcess.Start();
        // 2^5000000 is 1,250,001 hex digits: past the 1 MiB limit, so the worker holds it.
        var huge = await worker.ResultAsync(1, "2^5000000");
        Assert.Null(huge.Lossless);
        Assert.Equal(1, huge.Held);
        Assert.Equal("exact", huge.Kind);
        var next = await worker.ResultAsync(2, "Ans+1", ans: new AnsValue(Held: 1));
        Assert.Equal(("exact", 2L), (next.Kind, next.Held!.Value));

        worker.Send(new FetchRequest(3, 2));
        var value = await worker.NextAsync<ValueReply>();
        Assert.Equal(3, value.Id);
        Assert.True(value.Lossless == "i:1" + new string('0', 1_249_999) + "1", "the fetched value isn't 2^5000000 + 1");
        worker.Send(new FetchRequest(4, 1));
        Assert.Equal(1_250_003, (await worker.NextAsync<ValueReply>()).Lossless.Length);

        // One it never held: ValueGone, by fetch and by Ans alike.
        worker.Send(new FetchRequest(5, 99));
        var gone = await worker.NextAsync<ErrorReply>();
        Assert.Equal((5L, ErrorKinds.ValueGone), (gone.Id!.Value, gone.Kind));
        var missing = Assert.IsType<ErrorReply>(await worker.EvalAsync(6, "Ans", ans: new AnsValue(Held: 99)));
        Assert.Equal((6L, ErrorKinds.ValueGone), (missing.Id!.Value, missing.Kind));
    }

    [Fact]
    public async Task HeldValuesPastAQuarterOfTheHeapCapGoOldestFirst()
    {
        // A 128 MiB cap, written in hex, as the runtime reads it (B10's brief): a quarter is 32 MiB, and each 2^16000000
        // is 2 MB, so of twenty the first ones go.
        using var worker = WorkerProcess.Start(new Dictionary<string, string> { ["DOTNET_GCHeapHardLimit"] = "0x8000000" });
        for (int i = 1; i <= 20; i++) Assert.Equal(i, (await worker.ResultAsync(i, $"2^16000000+{i}")).Held);
        var gone = Assert.IsType<ErrorReply>(await worker.EvalAsync(21, "Ans", ans: new AnsValue(Held: 1)));
        Assert.Equal(ErrorKinds.ValueGone, gone.Kind);
        Assert.Equal("0", (await worker.ResultAsync(22, "Ans*0", ans: new AnsValue(Held: 20))).Main);
    }

    [Fact]
    public async Task RunningOutOfMemoryIsTooBigAndTheLoopGoesOn()
    {
        // 1/3 at 2^29 bits needs 64 MB a value, under a 64 MiB cap.
        using var worker = WorkerProcess.Start(new Dictionary<string, string> { ["DOTNET_GCHeapHardLimit"] = "0x4000000" });
        var error = Assert.IsType<ErrorReply>(await worker.EvalAsync(1, "1/3", new EvalContext(TargetSpec.Bits(1 << 29), Exact: false)));
        Assert.Equal((1L, "TooBig"), (error.Id!.Value, error.Kind));
        Assert.Equal("2", (await worker.ResultAsync(2, "1+1")).Main);
    }

    [Fact]
    public async Task ADeepSumAnswersAndADeeperOneIsACleanError()
    {
        using var worker = WorkerProcess.Start();
        string deep = string.Join("+", Enumerable.Repeat("1", 199_999));
        Assert.Equal("199999", (await worker.ResultAsync(1, deep)).Main);
        string deeper = string.Join("+", Enumerable.Repeat("1", 1_000_000));
        var error = Assert.IsType<ErrorReply>(await worker.EvalAsync(2, deeper));
        Assert.Equal((2L, "TooBig"), (error.Id!.Value, error.Kind));
        Assert.Equal("3", (await worker.ResultAsync(3, "1+2")).Main);
    }

    [Fact]
    public async Task TheContextIsHonoured()
    {
        using var worker = WorkerProcess.Start();
        var up = await worker.ResultAsync(1, "1/3", new EvalContext(TargetSpec.Format(11, 53), Rounding.Up));
        Assert.Equal(("0.33333333333333337", "f:11/53:0X1.5555555555556P-2", "float"), (up.Main, up.Lossless, up.Kind));
        Assert.Equal(["Inexact"], up.Flags);
        var floats = await worker.ResultAsync(2, "0.1+0.2", Bits(53) with { Exact = false });
        Assert.Equal(("0.3", "float"), (floats.Main, floats.Kind));
        var budget = await worker.ResultAsync(3, "2^1000", Bits(256) with { Budgets = new BudgetSpec(IntegerBits: 1000) });
        Assert.Equal("float (exact too large)", budget.Kind);
        var styled = await worker.ResultAsync(4, "1234567", options: new TextStyle(NotationStyle.Auto, ","));
        Assert.Equal("1,234,567", styled.Main);
        var scientific = await worker.ResultAsync(5, "1/3", Bits(53), options: new TextStyle(NotationStyle.Engineering));
        Assert.Equal("333.333333333333E-003", scientific.Main);
    }

    [Fact]
    public async Task FunctionsAndWhatsNotHereYet()
    {
        using var worker = WorkerProcess.Start();
        worker.Send(new FunctionsRequest(1));
        var functions = await worker.NextAsync<FunctionsReply>();
        Assert.Equal(1, functions.Id);
        Assert.Contains(functions.List, f => f is { Name: "sqrt", MinArguments: 1, MaxArguments: 1, WaitingFor: null });
        Assert.Contains(functions.List, f => f is { Name: "exp", WaitingFor: "W1" });
        Assert.Contains(functions.List, f => f is { Name: "max", MaxArguments: int.MaxValue });

        worker.Send(new DigitsRequest(2, "i:1", "E5"));
        worker.Send(new ReformatRequest(3, "i:1", NotationStyle.Scientific, 10));
        Assert.Equal("NotAvailable", (await worker.NextAsync<ErrorReply>()).Kind);
        Assert.Equal("NotAvailable", (await worker.NextAsync<ErrorReply>()).Kind);
    }

    [Fact]
    public async Task TextOutsideAsciiSurvivesTheTripBothWays()
    {
        using var worker = WorkerProcess.Start();
        var root = await worker.ResultAsync(1, "√(9/4) + 2²");
        Assert.Equal("r:B/2", root.Lossless); // 11/2, in hex
        Assert.Equal("5.5", root.Main);
        var raw = await worker.NextLineAsyncAfter(new EvalRequest(2, "2(3)", EvalContext.Default));
        Assert.Contains("\"understood\":\"2·3\"", raw); // relaxed escaping: · as itself
        var pi = Assert.IsType<ErrorReply>(await worker.EvalAsync(3, "2π"));
        Assert.Equal(("NotAvailable", "π isn't available yet."), (pi.Kind, pi.Message));
    }
}

internal static class WorkerProcessExtensions
{
    public static async Task<string> NextLineAsyncAfter(this WorkerProcess worker, Request request)
    {
        worker.Send(request);
        return await worker.NextLineAsync();
    }
}
