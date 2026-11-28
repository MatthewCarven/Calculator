using Calc.Protocol;

namespace Calc.Client;

/// <summary>
/// The worker as everything above it sees it: requests in, replies out, as tasks, with no processes in sight. Each task
/// completes with the worker's reply, or with an <see cref="ErrorReply"/> the client made: kind Stopped after
/// <see cref="StopAsync"/>, <see cref="ErrorKinds.WorkerDied"/> if the worker ended by itself, or
/// <see cref="ErrorKinds.MemoryShort"/> if one couldn't be started safely. None of them throws for any of that.
/// </summary>
/// <remarks><see cref="WorkerClient"/> is the real one; the tests' FakeWorker stands in for it in B11's Session tests.</remarks>
public interface IWorker : IDisposable
{
    /// <summary>Evaluates a line. A <see cref="ResultReply"/>'s <c>Held</c>, when set, is the id an Ans can name.</summary>
    Task<Reply> EvaluateAsync(string source, EvalContext context, AnsValue? ans = null, TextStyle? options = null);

    /// <summary>A held value's lossless text: a <see cref="ValueReply"/>, or ValueGone.</summary>
    Task<Reply> FetchAsync(long heldId);

    /// <summary>The function table: a <see cref="FunctionsReply"/>.</summary>
    Task<Reply> FunctionsAsync();

    /// <summary>A <see cref="PongReply"/>, answered even during a long evaluation.</summary>
    Task<Reply> PingAsync();

    /// <summary>
    /// Stops everything outstanding: softly first, and if that hasn't ended it within 150 ms, by ending the worker. Every
    /// outstanding task completes as Stopped; the next request starts a new worker if this one had to go.
    /// </summary>
    Task StopAsync();
}
