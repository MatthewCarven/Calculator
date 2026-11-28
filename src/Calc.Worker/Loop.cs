using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Calc.Engine.Eval;
using Calc.Engine.Text;
using Calc.Engine.Values;
using Calc.Protocol;
using Calc.Syntax;

namespace Calc.Worker;

/// <summary>
/// The worker's stdio loop: one JSON request a line in, one reply a line out (<see cref="Messages"/>). It survives anything
/// a line can bring: a malformed one gets an error reply, and so does an evaluation that throws, runs out of memory or is
/// stopped. Only the end of stdin ends it, so a worker whose Client has gone dies with it.
/// </summary>
/// <remarks>
/// <para>Three threads. The reader, the caller's, reads lines and answers ping and stop itself, so both work during a long
/// evaluation. Everything else queues for the compute thread, at BelowNormal priority with a 256 MiB stack (rule 13), which
/// parses and evaluates one request at a time, in order. Both write through one lock, a line at a time.</para>
/// <para>Stop is soft: it cancels the running evaluation's token, which the evaluator checks on every node, or marks a queued
/// one to reply Stopped without starting. An operation under way runs to its end; the Client's kill (B10) is for those.</para>
/// </remarks>
internal sealed class Loop
{
    private readonly TextWriter _output;
    private readonly object _writing = new();
    private readonly BlockingCollection<Request> _work = new();
    private readonly HeldValues _held = new();

    // Guarded by _stops: the running evaluation's id and token, and the evaluations queued but not started, true once stopped.
    private readonly object _stops = new();
    private long? _runningId;
    private CancellationTokenSource? _runningStop;
    private readonly Dictionary<long, bool> _queued = [];

    public Loop(TextWriter output) => _output = output;

    /// <summary>Reads requests until stdin ends, then returns: the caller exits, taking the compute thread with it.</summary>
    public void Run(TextReader input)
    {
        var compute = new Thread(Compute, 256 << 20) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "compute" };
        compute.Start();
        while (input.ReadLine() is string line)
        {
            if (line.Length == 0) continue;
            Request request;
            try
            {
                request = Messages.ReadRequest(line);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                Send(new ErrorReply(IdIn(line), ErrorKinds.BadRequest, $"That isn't a request: {e.Message}"));
                continue;
            }
            switch (request)
            {
                case PingRequest ping:
                    Send(new PongReply(ping.Id, Wire.Version, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes));
                    break;
                case StopRequest stop:
                    Stop(stop.Id);
                    break;
                case EvalRequest eval:
                    lock (_stops) _queued[eval.Id] = false;
                    _work.Add(eval);
                    break;
                default:
                    _work.Add(request);
                    break;
            }
        }
    }

    private void Stop(long id)
    {
        lock (_stops)
        {
            if (_runningId == id) _runningStop?.Cancel();
            else if (_queued.ContainsKey(id)) _queued[id] = true;
        }
    }

    private void Compute()
    {
        foreach (Request request in _work.GetConsumingEnumerable())
        {
            long? id = IdOf(request);
            try
            {
                Send(Handle(request));
            }
            catch (Exception e)
            {
                // Nothing a request does may end the loop. OutOfMemoryException becomes TooBig, through ErrorMap.
                Send(Error(id, ErrorMap.From(e, null)));
            }
        }
    }

    private Reply Handle(Request request) => request switch
    {
        EvalRequest eval => Evaluate(eval),
        FetchRequest fetch => _held.Get(fetch.HeldId) is var (value, target)
            ? new ValueReply(fetch.Id, Lossless.Write(value, target))
            : Gone(fetch.Id, fetch.HeldId),
        FunctionsRequest functions => new FunctionsReply(functions.Id,
            FunctionTable.All.Select(f => new FunctionInfo(f.Name, f.MinArguments, f.MaxArguments, f.WaitingFor)).ToArray()),
        DigitsRequest digits => new ErrorReply(digits.Id, nameof(ErrorKind.NotAvailable), "More digits come in C7."),
        ReformatRequest reformat => new ErrorReply(reformat.Id, nameof(ErrorKind.NotAvailable), "Reformatting comes in C6."),
        _ => new ErrorReply(IdOf(request), ErrorKinds.BadRequest, $"The worker can't do a {request.GetType().Name} here."),
    };

    private Reply Evaluate(EvalRequest request)
    {
        using var stop = new CancellationTokenSource();
        lock (_stops)
        {
            bool stopped = _queued.Remove(request.Id, out bool marked) && marked;
            if (stopped) return Error(request.Id, new CalcError(ErrorKind.Stopped, "Stopped.", null));
            _runningId = request.Id;
            _runningStop = stop;
        }
        try
        {
            Target target;
            ExactBudgets budgets;
            TextOptions options;
            try
            {
                target = Contexts.TargetOf(request.Context.Target);
                budgets = Contexts.BudgetsOf(request.Context.Budgets);
                options = Contexts.OptionsOf(request.Options);
            }
            catch (ArgumentException e)
            {
                return new ErrorReply(request.Id, ErrorKinds.BadRequest, $"That context can't be used: {e.Message}");
            }
            Value? ans = null;
            if (request.Ans is AnsValue given)
            {
                var (value, problem) = Ans(given, target);
                if (problem is not null) return problem with { Id = request.Id };
                ans = value;
            }

            var clock = Stopwatch.StartNew();
            // Parsed here, on the big stack (rule 13).
            Node tree = Parser.Parse(request.Source);
            string? understood = Printer.Understood(tree);
            Outcome outcome = Evaluator.Run(tree, target, Contexts.ModeOf(request.Context.Mode), ans, request.Context.Exact, budgets, stop.Token);
            ResultText text = ResultText.Of(outcome.Value, target, options);
            // The value goes on the wire up to the limit, estimated first so a huge one isn't written only to be held.
            string? lossless = EstimatedLength(outcome.Value) <= Wire.LosslessLimit ? Lossless.Write(outcome.Value, target) : null;
            long? held = null;
            if (lossless is null || lossless.Length > Wire.LosslessLimit)
            {
                lossless = null;
                _held.Add(request.Id, outcome.Value, target);
                held = request.Id;
            }
            return new ResultReply(request.Id, text.Short, text.Main, text.Kind, text.Caption, text.Digits, text.Complete,
                                   Contexts.FlagsOf(outcome.Flags), clock.Elapsed.TotalMilliseconds, [], lossless, held, understood);
        }
        catch (Exception e)
        {
            // CalcErrors keep their spans; OutOfMemoryException becomes TooBig.
            return Error(request.Id, ErrorMap.From(e, null));
        }
        finally
        {
            lock (_stops)
            {
                _runningId = null;
                _runningStop = null;
            }
        }
    }

    /// <summary>
    /// The Ans a request names, or the reply refusing it: a held value that's gone, text that isn't lossless, or a float
    /// from another target, which can't leave it (rule 14). That's the second wall, behind the Client's own guard.
    /// </summary>
    private (Value? Value, ErrorReply? Problem) Ans(AnsValue given, Target target)
    {
        Value value;
        Target? from;
        if (given.Held is long heldId)
        {
            if (_held.Get(heldId) is not var (heldValue, heldTarget)) return (null, Gone(0, heldId));
            (value, from) = (heldValue, heldValue is FloatValue ? heldTarget : null);
        }
        else if (given.Lossless is string text)
        {
            try { (value, from) = Lossless.Read(text); }
            catch (FormatException e) { return (null, new ErrorReply(0, ErrorKinds.BadRequest, e.Message)); }
        }
        else
        {
            return (null, new ErrorReply(0, ErrorKinds.BadRequest, "An Ans needs its lossless text or a held id."));
        }
        if (value is FloatValue && from != target)
            return (null, new ErrorReply(0, ErrorKinds.BadRequest,
                $"That Ans was worked out at {from}, and this is {target}: a float can't leave its target, so replay its line (rule 14)."));
        return (value, null);
    }

    /// <summary>About how long a value's lossless text is: hex digits are 4 bits each.</summary>
    private static long EstimatedLength(Value value) => value switch
    {
        IntValue i => i.Number.BitLength / 4 + 4,
        RatioValue r => (r.Numerator.BitLength + r.Denominator.BitLength) / 4 + 6,
        FloatValue f => f.Number.Precision / 4 + 64,
        _ => 0,
    };

    private static ErrorReply Gone(long id, long heldId) =>
        new(id, ErrorKinds.ValueGone, $"The worker no longer holds value {heldId}: replay its line.");

    private static ErrorReply Error(long? id, CalcError error) =>
        new(id, error.Kind.ToString(), error.Message, error.Span?.Start, error.Span?.End);

    private static long? IdOf(Request request) => request switch
    {
        EvalRequest r => r.Id,
        DigitsRequest r => r.Id,
        ReformatRequest r => r.Id,
        FetchRequest r => r.Id,
        FunctionsRequest r => r.Id,
        StopRequest r => r.Id,
        PingRequest r => r.Id,
        _ => null,
    };

    /// <summary>A malformed line's id, if it has a readable one, so its error reply can name it.</summary>
    private static long? IdIn(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("id", out var id) && id.TryGetInt64(out long value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Send(Reply reply)
    {
        string line = Messages.Write(reply);
        lock (_writing)
        {
            _output.Write(line);
            _output.Write('\n');
            _output.Flush();
        }
    }
}

/// <summary>
/// Values too big for the wire, kept by the id of the reply that made them, oldest out first once they pass a quarter of
/// the heap cap (the Client sets the cap, B10). A miss is ValueGone, and the Client replays the line.
/// </summary>
internal sealed class HeldValues
{
    private readonly Dictionary<long, (Value Value, Target Target, long Bytes)> _values = [];
    private readonly Queue<long> _order = new();
    private long _bytes;

    public void Add(long id, Value value, Target target)
    {
        long bytes = Bytes(value);
        if (_values.Remove(id, out var old)) _bytes -= old.Bytes;
        _values[id] = (value, target, bytes);
        _order.Enqueue(id);
        _bytes += bytes;
        long limit = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 4;
        // The newest stays, whatever its size: its reply is about to name it.
        while (_bytes > limit && _order.Count > 1)
        {
            long oldest = _order.Dequeue();
            if (oldest != id && _values.Remove(oldest, out var evicted)) _bytes -= evicted.Bytes;
        }
    }

    public (Value Value, Target Target)? Get(long id) => _values.TryGetValue(id, out var held) ? (held.Value, held.Target) : null;

    private static long Bytes(Value value) => value switch
    {
        IntValue i => i.Number.BitLength / 8 + 64,
        RatioValue r => (r.Numerator.BitLength + r.Denominator.BitLength) / 8 + 64,
        FloatValue f => f.Number.Precision / 8 + 64,
        _ => 64,
    };
}
