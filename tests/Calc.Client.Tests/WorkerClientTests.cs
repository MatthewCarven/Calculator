using System.Diagnostics;
using Calc.Protocol;

namespace Calc.Client.Tests;

/// <summary>B10: the real client owning the real worker's life: Stop, death, the heap cap, and nothing left behind.</summary>
public class WorkerClientTests
{
    /// <summary>A sum of 30 exact powers, about 7 s in Debug, made of many nodes: the soft stop's kind of work.</summary>
    internal static readonly string Slow = string.Join("+", Enumerable.Range(0, 30).Select(i => $"3^{15000 + i}"));

    /// <summary>
    /// One 2,000,000-bit multiply, which no soft stop can interrupt: the kill's kind of work. Its operands take a few
    /// milliseconds (a power of two is one bit, a subtraction is linear), so a stop soon after the start lands inside it.
    /// </summary>
    private const string Multiply = "(2^2000000-1)*(2^2000000-3)";

    private static readonly EvalContext Huge = new(TargetSpec.Bits(2_000_000), Exact: false);

    /// <summary>Plenty of free commit, so the cap is the setting.</summary>
    private sealed class Commit(double gibibytes) : ICommitInfo
    {
        public ulong? AvailableBytes => (ulong)(gibibytes * (1L << 30));
    }

    private static bool Gone(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true; // no such process
        }
    }

    [Fact]
    public async Task AWorkerStartsWithTheFirstRequestAndAnswers()
    {
        using var client = new WorkerClient();
        Assert.Null(client.ProcessId);
        Assert.Equal(0, client.Started);
        var third = Assert.IsType<ResultReply>(await client.EvaluateAsync("1/3", EvalContext.Default));
        Assert.Equal(("exact = 1/3", "r:1/3"), (third.Kind, third.Lossless));
        Assert.NotNull(client.ProcessId);
        var one = Assert.IsType<ResultReply>(await client.EvaluateAsync("Ans*3", EvalContext.Default, new AnsValue(third.Lossless)));
        Assert.Equal("i:1", one.Lossless);
        Assert.Equal(1, client.Started);
    }

    [Fact]
    public async Task BigRepliesComeThroughWhole()
    {
        // The dedicated reader keeps the pipe from filling: an 830,003-character lossless text, and a 1,250,003-character fetch.
        using var client = new WorkerClient();
        var big = Assert.IsType<ResultReply>(await client.EvaluateAsync("2^3320000", EvalContext.Default));
        Assert.True(big.Lossless == "i:1" + new string('0', 830_000));
        var huge = Assert.IsType<ResultReply>(await client.EvaluateAsync("2^5000000", EvalContext.Default));
        var value = Assert.IsType<ValueReply>(await client.FetchAsync(huge.Held!.Value));
        Assert.Equal(1_250_003, value.Lossless.Length);
    }

    [Fact]
    public async Task StopInsideAHugeMultiplyEndsTheWorker()
    {
        using var client = new WorkerClient();
        Assert.IsType<ResultReply>(await client.EvaluateAsync("1", EvalContext.Default)); // started and warm
        int first = client.ProcessId!.Value;
        Task<Reply> running = client.EvaluateAsync(Multiply, Huge);
        await Task.Delay(500); // well into the multiply
        var clock = Stopwatch.StartNew();
        await client.StopAsync();
        var stopped = Assert.IsType<ErrorReply>(await running.WaitAsync(TimeSpan.FromSeconds(10)));
        long stopMs = clock.ElapsedMilliseconds;
        Assert.Equal("Stopped", stopped.Kind);
        // The soft stop can't reach inside one multiply, so it waits its 150 ms, then the kill takes a few more.
        Assert.InRange(stopMs, 140, 1000);
        Assert.True(Gone(first), "the stopped worker is still running");

        clock.Restart();
        Assert.Equal("2", Assert.IsType<ResultReply>(await client.EvaluateAsync("1+1", EvalContext.Default)).Main);
        long nextMs = clock.ElapsedMilliseconds;
        Assert.NotEqual(first, client.ProcessId);
        Assert.Equal(2, client.Started);
        Assert.True(nextMs < 5000, $"{nextMs} ms to the next answer");
        Console.WriteLine($"Stop to Stopped: {stopMs} ms; to the next answer: {nextMs} ms");
    }

    [Fact]
    public async Task ASoftStopNeedsNoKill()
    {
        using var client = new WorkerClient();
        Task<Reply> running = client.EvaluateAsync(Slow, EvalContext.Default);
        await Task.Delay(800);
        int first = client.ProcessId!.Value;
        var clock = Stopwatch.StartNew();
        await client.StopAsync();
        Assert.Equal("Stopped", Assert.IsType<ErrorReply>(await running).Kind);
        // Whether the node under way ended within the 150 ms decides it; either way the answer is Stopped. When it did,
        // the same worker carries on.
        bool soft = clock.ElapsedMilliseconds < 140;
        Assert.IsType<ResultReply>(await client.EvaluateAsync("1", EvalContext.Default));
        Assert.Equal(soft, client.ProcessId == first);
    }

    [Fact]
    public async Task AWorkerThatDiesSaysSoAndIsReplaced()
    {
        using var client = new WorkerClient();
        Task<Reply> running = client.EvaluateAsync(Slow, EvalContext.Default);
        await Task.Delay(300);
        int first = client.ProcessId!.Value;
        using (var process = Process.GetProcessById(first)) process.Kill();
        var died = Assert.IsType<ErrorReply>(await running.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(ErrorKinds.WorkerDied, died.Kind);
        Assert.StartsWith("The worker ended by itself with exit code", died.Message);

        Assert.Equal("2", Assert.IsType<ResultReply>(await client.EvaluateAsync("1+1", EvalContext.Default)).Main);
        Assert.NotEqual(first, client.ProcessId);
        Assert.Equal(2, client.Started);
    }

    [Fact]
    public async Task TheWorkersHeapIsTheCap()
    {
        using var client = new WorkerClient(new WorkerOptions(HeapCapBytes: 512L << 20, Commit: new Commit(100)));
        var pong = Assert.IsType<PongReply>(await client.PingAsync());
        Assert.Equal(512L << 20, pong.Memory);
    }

    [Theory]
    [InlineData(0.5)] // short by half a GiB: negative, which must not wrap
    [InlineData(1.0)] // exactly the reserve: zero, which the runtime would read as no cap
    [InlineData(1.1)] // 102 MiB over the reserve: below the floor
    public async Task WithTooLittleCommitFreeNoWorkerStarts(double gibibytesFree)
    {
        using var client = new WorkerClient(new WorkerOptions(Commit: new Commit(gibibytesFree)));
        var refused = Assert.IsType<ErrorReply>(await client.EvaluateAsync("1+1", EvalContext.Default));
        Assert.Equal(ErrorKinds.MemoryShort, refused.Kind);
        Assert.Contains("Memory is short", refused.Message);
        Assert.Equal(0, client.Started);
        Assert.Null(client.ProcessId);
    }

    [Theory]
    [InlineData(1.25, 256L << 20)] // the floor exactly
    [InlineData(3.0, 2L << 30)]    // free less the reserve
    [InlineData(5.0, 4L << 30)]    // the setting
    public async Task FreeCommitLessAGibibyteCapsTheWorker(double gibibytesFree, long cap)
    {
        using var client = new WorkerClient(new WorkerOptions(Commit: new Commit(gibibytesFree)));
        Assert.Equal(cap, Assert.IsType<PongReply>(await client.PingAsync()).Memory);
    }

    [Fact]
    public async Task OutOfMemoryUnderASmallCapReportsAndRecovers()
    {
        // 1/3 at 2^30 bits needs 128 MB a value, under the 256 MiB floor.
        using var client = new WorkerClient(new WorkerOptions(HeapCapBytes: HeapCap.Floor, Commit: new Commit(100)));
        var error = Assert.IsType<ErrorReply>(await client.EvaluateAsync("1/3", new EvalContext(TargetSpec.Bits(1 << 30), Exact: false)));
        Assert.Equal("TooBig", error.Kind);
        Assert.Equal("2", Assert.IsType<ResultReply>(await client.EvaluateAsync("1+1", EvalContext.Default)).Main);
        Assert.Equal(1, client.Started); // the same worker, recovered
    }

    [Fact]
    public async Task TwentyStopAndGoCyclesLeaveNoProcessBehind()
    {
        var processes = new List<int>();
        using (var client = new WorkerClient())
        {
            for (int cycle = 0; cycle < 20; cycle++)
            {
                // A started, warm worker each time, so the stop lands inside the multiply and has to kill it.
                Assert.IsType<ResultReply>(await client.EvaluateAsync("1", EvalContext.Default));
                Task<Reply> running = client.EvaluateAsync(Multiply, Huge);
                await Task.Delay(250);
                processes.Add(client.ProcessId!.Value);
                await client.StopAsync();
                Assert.Equal("Stopped", Assert.IsType<ErrorReply>(await running.WaitAsync(TimeSpan.FromSeconds(10))).Kind);
            }
            Assert.Equal("2", Assert.IsType<ResultReply>(await client.EvaluateAsync("1+1", EvalContext.Default)).Main);
            processes.Add(client.ProcessId!.Value);
            Assert.Equal(21, client.Started);
        }
        Assert.Equal(21, processes.Distinct().Count());
        Assert.All(processes, id => Assert.True(Gone(id), $"worker {id} is still running"));
    }

    [Fact]
    public async Task NoWorkerRemainsAfterDispose()
    {
        int id;
        Task<Reply> running;
        using (var client = new WorkerClient())
        {
            running = client.EvaluateAsync(Multiply, Huge);
            await Task.Delay(300);
            id = client.ProcessId!.Value;
        }
        Assert.True(Gone(id), "the worker outlived its client");
        Assert.Equal("Stopped", Assert.IsType<ErrorReply>(await running).Kind);
    }

    [Fact]
    public async Task AMissingWorkerIsWorkerDied()
    {
        using var client = new WorkerClient(new WorkerOptions(WorkerPath: Path.Combine(AppContext.BaseDirectory, "no-such-worker.exe")));
        var error = Assert.IsType<ErrorReply>(await client.EvaluateAsync("1", EvalContext.Default));
        Assert.Equal(ErrorKinds.WorkerDied, error.Kind);
        Assert.StartsWith("The worker couldn't start", error.Message);
    }
}
