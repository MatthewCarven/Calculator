using Calc.Protocol;

namespace Calc.Client.Tests;

/// <summary>
/// What every <see cref="IWorker"/> promises, run against the real client and the fake alike, so the fake B11 tests
/// against can't drift from the real thing.
/// </summary>
public abstract class WorkerContract
{
    private protected abstract IWorker Create();

    /// <summary>Starts an evaluation that won't finish by itself for a good while, and returns its task.</summary>
    private protected abstract Task<Reply> StartLong(IWorker worker);

    private static readonly EvalContext Context = EvalContext.Default;

    [Fact]
    public async Task IdsCountUpAndRepliesEchoThem()
    {
        using var worker = Create();
        var first = Assert.IsType<ResultReply>(await worker.EvaluateAsync("1+1", Context).Soon());
        var second = Assert.IsType<ResultReply>(await worker.EvaluateAsync("2+2", Context).Soon());
        Assert.True(second.Id > first.Id, $"{first.Id} then {second.Id}");
        var pong = Assert.IsType<PongReply>(await worker.PingAsync().Soon());
        Assert.True(pong.Id > second.Id);
        Assert.Equal(Wire.Version, pong.Version);
        Assert.IsType<FunctionsReply>(await worker.FunctionsAsync().Soon());
        Assert.Equal(ErrorKinds.ValueGone, Assert.IsType<ErrorReply>(await worker.FetchAsync(999).Soon()).Kind);
    }

    [Fact]
    public async Task StopCompletesWhatsOutstandingAsStopped()
    {
        using var worker = Create();
        Task<Reply> running = StartLong(worker);
        await Task.Delay(200);
        Assert.False(running.IsCompleted);
        await worker.StopAsync();
        var stopped = Assert.IsType<ErrorReply>(await running.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("Stopped", stopped.Kind);
        // And it carries on.
        Assert.IsType<ResultReply>(await worker.EvaluateAsync("3", Context).WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task StopWithNothingOutstandingDoesNothing()
    {
        using var worker = Create();
        await worker.StopAsync();
        Assert.IsType<ResultReply>(await worker.EvaluateAsync("1", Context).Soon());
        await worker.StopAsync();
        Assert.IsType<ResultReply>(await worker.EvaluateAsync("2", Context).Soon());
    }

    [Fact]
    public async Task DisposeStopsWhatsOutstandingAndThenRefuses()
    {
        var worker = Create();
        Task<Reply> running = StartLong(worker);
        await Task.Delay(200);
        worker.Dispose();
        Assert.Equal("Stopped", Assert.IsType<ErrorReply>(await running.WaitAsync(TimeSpan.FromSeconds(10))).Kind);
        Assert.Throws<ObjectDisposedException>(() => { _ = worker.EvaluateAsync("1", Context); });
        worker.Dispose(); // twice is fine
    }
}

public sealed class RealWorkerContract : WorkerContract
{
    private protected override IWorker Create() => new WorkerClient();

    private protected override Task<Reply> StartLong(IWorker worker) => worker.EvaluateAsync(WorkerClientTests.Slow, EvalContext.Default);
}

public sealed class FakeWorkerContract : WorkerContract
{
    private protected override IWorker Create() => new FakeWorker();

    private protected override Task<Reply> StartLong(IWorker worker)
    {
        var fake = (FakeWorker)worker;
        fake.Hold = true;
        Task<Reply> held = fake.EvaluateAsync("held", EvalContext.Default);
        fake.Hold = false;
        return held;
    }
}
