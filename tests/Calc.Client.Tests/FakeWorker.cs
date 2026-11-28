using Calc.Protocol;

namespace Calc.Client.Tests;

/// <summary>
/// An <see cref="IWorker"/> with no process, for B11's Session tests: replies come from a function, and while
/// <see cref="Hold"/> is on they wait until <see cref="Release"/> or Stop. It keeps <see cref="WorkerContract"/>, the same
/// tests the real client passes.
/// </summary>
internal sealed class FakeWorker(Func<EvalRequest, Reply>? evaluate = null) : IWorker
{
    private readonly Func<EvalRequest, Reply> _evaluate = evaluate ?? Echo;
    private readonly object _gate = new();
    private readonly Dictionary<long, (EvalRequest Request, TaskCompletionSource<Reply> Reply)> _waiting = [];
    private long _nextId;
    private bool _disposed;

    /// <summary>Every request, in order.</summary>
    public List<Request> Requests { get; } = [];

    /// <summary>While on, evaluations wait until <see cref="Release"/> or Stop.</summary>
    public bool Hold { get; set; }

    /// <summary>How many times Stop found something to stop.</summary>
    public int Stops { get; private set; }

    /// <summary>The source back as the result, exactly: enough to tell replies apart.</summary>
    public static Reply Echo(EvalRequest request) =>
        new ResultReply(request.Id, request.Source, request.Source, "exact", "exact", request.Source.Length, true, [], 0, [], "i:0");

    public Task<Reply> EvaluateAsync(string source, EvalContext context, AnsValue? ans = null, TextStyle? options = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var request = new EvalRequest(++_nextId, source, context, ans, options);
            Requests.Add(request);
            if (!Hold) return Task.FromResult(_evaluate(request));
            var reply = new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting[request.Id] = (request, reply);
            return reply.Task;
        }
    }

    public Task<Reply> FetchAsync(long heldId) => Answer(id => new FetchRequest(id, heldId), id => new ErrorReply(id, ErrorKinds.ValueGone, "The fake holds nothing."));

    public Task<Reply> FunctionsAsync() => Answer(id => new FunctionsRequest(id), id => new FunctionsReply(id, [new FunctionInfo("sqrt", 1, 1)]));

    public Task<Reply> PingAsync() => Answer(id => new PingRequest(id), id => new PongReply(id, Wire.Version));

    private Task<Reply> Answer(Func<long, Request> request, Func<long, Reply> reply)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long id = ++_nextId;
            Requests.Add(request(id));
            return Task.FromResult(reply(id));
        }
    }

    /// <summary>Lets every waiting evaluation reply.</summary>
    public void Release()
    {
        foreach (var (request, reply) in Take()) reply.TrySetResult(_evaluate(request));
    }

    public Task StopAsync()
    {
        var taken = Take();
        if (taken.Count > 0)
        {
            lock (_gate) Stops++;
        }
        foreach (var (request, reply) in taken) reply.TrySetResult(new ErrorReply(request.Id, "Stopped", "Stopped."));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        foreach (var (request, reply) in Take()) reply.TrySetResult(new ErrorReply(request.Id, "Stopped", "Stopped: the calculator closed."));
    }

    private List<(EvalRequest Request, TaskCompletionSource<Reply> Reply)> Take()
    {
        lock (_gate)
        {
            var taken = _waiting.Values.ToList();
            _waiting.Clear();
            return taken;
        }
    }
}
