using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Calc.Protocol;

namespace Calc.Client;

/// <summary>Where the worker is and how big it may grow.</summary>
/// <param name="HeapCapBytes">The heap cap setting, 4 GiB by default; free commit less 1 GiB, if smaller, wins at spawn.</param>
/// <param name="WorkerPath">The worker's exe, beside this program by default.</param>
/// <param name="Commit">Free commit, the system's by default; tests put their own figure in.</param>
public sealed record WorkerOptions(long HeapCapBytes = HeapCap.Default, string? WorkerPath = null, ICommitInfo? Commit = null);

/// <summary>
/// The real <see cref="IWorker"/>: it owns the calc-worker process's life, so nothing above it thinks about processes. A
/// worker starts with the first request, and again with the first request after one has gone, whether Stop ended it or
/// it ended by itself.
/// </summary>
/// <remarks>
/// <para>Each request gets the next id, and its task waits in the live worker's table until a reply with that id comes; a
/// reply with an id that isn't waiting is dropped. One dedicated thread reads each worker's stdout, so a big reply can never
/// fill the pipe and stall it, and stderr is read too, for the message if it dies.</para>
/// <para>Stop sends a soft stop for each outstanding evaluation, waits up to 150 ms, and then kills the worker's process tree;
/// everything outstanding completes as Stopped. The spike timed the kill at 2 to 3 ms and a new worker's first answer at
/// about 250 ms. One lock covers the live worker and every table, so a reply, a Stop, a death and a new request can't
/// interleave badly: a request either joins a worker's table before its end is seen, and is completed by it, or after,
/// and starts another.</para>
/// </remarks>
public sealed class WorkerClient(WorkerOptions? options = null) : IWorker
{
    /// <summary>How long Stop waits for the soft stop before ending the worker.</summary>
    public static readonly TimeSpan StopGrace = TimeSpan.FromMilliseconds(150);

    private readonly WorkerOptions _options = options ?? new WorkerOptions();
    private readonly object _gate = new();
    private Connection? _live;
    private long _nextId;
    private bool _disposed;

    /// <summary>The live worker's process id, if one is running. For diagnostics and tests.</summary>
    public int? ProcessId
    {
        get { lock (_gate) return _live?.ProcessId; }
    }

    /// <summary>How many workers this client has started.</summary>
    public int Started { get; private set; }

    public Task<Reply> EvaluateAsync(string source, EvalContext context, AnsValue? ans = null, TextStyle? options = null) =>
        Send(id => new EvalRequest(id, source, context, ans, options), isEval: true);

    public Task<Reply> FetchAsync(long heldId) => Send(id => new FetchRequest(id, heldId), isEval: false);

    public Task<Reply> FunctionsAsync() => Send(id => new FunctionsRequest(id), isEval: false);

    public Task<Reply> PingAsync() => Send(id => new PingRequest(id), isEval: false);

    private Task<Reply> Send(Func<long, Request> make, bool isEval)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long id = ++_nextId;
            if (_live is null && Start(id) is ErrorReply refused) return Task.FromResult<Reply>(refused);
            Connection connection = _live!;
            var reply = new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Waiting[id] = (reply, isEval);
            // If the worker has just died, the write fails or the line is lost; either way its reader sees the end and
            // completes this task, since it joined the table first.
            connection.TryWrite(Messages.Write(make(id)));
            return reply.Task;
        }
    }

    public async Task StopAsync()
    {
        Connection? connection;
        Task[] outstanding;
        lock (_gate)
        {
            connection = _live;
            if (_disposed || connection is null || connection.Waiting.Count == 0) return;
            outstanding = connection.Waiting.Values.Select(w => (Task)w.Reply.Task).ToArray();
            foreach (var (id, waiting) in connection.Waiting)
                if (waiting.IsEval) connection.TryWrite(Messages.Write(new StopRequest(id)));
        }
        Task all = Task.WhenAll(outstanding);
        if (await Task.WhenAny(all, Task.Delay(StopGrace)).ConfigureAwait(false) == all) return;
        End(connection, "Stopped.");
    }

    public void Dispose()
    {
        Connection? connection;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            connection = _live;
        }
        if (connection is null) return;
        End(connection, "Stopped: the calculator closed.");
        // Until it has really gone: nothing of the calculator outlives it.
        connection.Finished.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Starts a worker as the live one, or says why not: memory is short, or it wouldn't start. Under the lock.</summary>
    private ErrorReply? Start(long id)
    {
        ulong? free = (_options.Commit ?? CommitInfo.System).AvailableBytes;
        if (HeapCap.For(_options.HeapCapBytes, free) is not long cap)
        {
            string freeText = free is ulong f ? $"{f >> 20:N0} MiB of memory is free" : "the memory setting is too small";
            return new ErrorReply(id, ErrorKinds.MemoryShort,
                string.Create(CultureInfo.InvariantCulture, $"Memory is short: {freeText}, and a worker needs {HeapCap.Reserve >> 20:N0} MiB to spare plus {HeapCap.Floor >> 20:N0} MiB for itself. Close something, or lower the memory setting, and try again."));
        }
        var utf8 = new UTF8Encoding(false);
        var start = new ProcessStartInfo(_options.WorkerPath ?? Path.Combine(AppContext.BaseDirectory, Wire.WorkerFileName))
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        start.Environment["DOTNET_GCHeapHardLimit"] = HeapCap.EnvironmentValue(cap);
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Process.Start gave no process.");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new ErrorReply(id, ErrorKinds.WorkerDied, $"The worker couldn't start: {e.Message}");
        }
        _live = new Connection(this, process);
        Started++;
        _live.StartReading();
        return null;
    }

    /// <summary>
    /// Ends a worker by killing its process tree, and completes everything it had outstanding as Stopped at once, rather
    /// than when its reader sees the end. The next request starts another.
    /// </summary>
    private void End(Connection connection, string message)
    {
        List<(long Id, TaskCompletionSource<Reply> Reply)> orphans;
        lock (_gate)
        {
            connection.Killed = true;
            if (_live == connection) _live = null;
            orphans = connection.TakeWaiting();
        }
        connection.Kill();
        foreach (var (id, reply) in orphans) reply.TrySetResult(new ErrorReply(id, "Stopped", message));
    }

    /// <summary>One worker process: its table of waiting requests, and the thread reading its replies.</summary>
    private sealed class Connection(WorkerClient owner, Process process)
    {
        private readonly Queue<string> _stderr = new();
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _life = new();
        private bool _gone;

        /// <summary>Completes once the process has exited and its reader has finished.</summary>
        public Task Finished => _finished.Task;

        /// <summary>Requests waiting for their replies, by id. Under the owner's lock.</summary>
        public Dictionary<long, (TaskCompletionSource<Reply> Reply, bool IsEval)> Waiting { get; } = [];

        /// <summary>The client ended it, so what was waiting is Stopped, not WorkerDied. Under the owner's lock.</summary>
        public bool Killed { get; set; }

        public int ProcessId { get; } = process.Id;

        public void StartReading()
        {
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (_stderr)
                {
                    _stderr.Enqueue(e.Data);
                    while (_stderr.Count > 20) _stderr.Dequeue();
                }
            };
            process.BeginErrorReadLine();
            new Thread(Read) { IsBackground = true, Name = $"calc-worker {ProcessId} replies" }.Start();
        }

        public void TryWrite(string line)
        {
            try
            {
                process.StandardInput.Write(line);
                process.StandardInput.Write('\n');
                process.StandardInput.Flush();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The worker has gone; its reader completes what was waiting.
            }
        }

        public List<(long Id, TaskCompletionSource<Reply> Reply)> TakeWaiting()
        {
            var taken = Waiting.Select(w => (w.Key, w.Value.Reply)).ToList();
            Waiting.Clear();
            return taken;
        }

        /// <summary>Kills the process tree. Its reader then sees the end, waits for the exit and disposes the process.</summary>
        public void Kill()
        {
            lock (_life)
            {
                if (_gone) return;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Already gone.
                }
            }
        }

        private void Read()
        {
            try
            {
                while (process.StandardOutput.ReadLine() is string line)
                {
                    Reply reply;
                    try
                    {
                        reply = Messages.ReadReply(line);
                    }
                    catch (Exception e) when (e is JsonException or NotSupportedException)
                    {
                        continue; // not a reply: nothing waits on it
                    }
                    // Progress (B12, C5) comes before the evaluation's own reply, under its id, and doesn't end it.
                    if (reply is ProgressReply) continue;
                    TaskCompletionSource<Reply>? waiting = null;
                    lock (owner._gate)
                    {
                        if (IdOf(reply) is long id && Waiting.Remove(id, out var entry)) waiting = entry.Reply;
                    }
                    // A reply nothing waits for, stale or unknown, is dropped.
                    waiting?.TrySetResult(reply);
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The pipe broke: the worker has gone.
            }
            Ended();
        }

        /// <summary>Its stdout has ended: whatever still waits completes as Stopped if the client ended it, else WorkerDied.</summary>
        private void Ended()
        {
            List<(long Id, TaskCompletionSource<Reply> Reply)> orphans;
            bool killed;
            lock (owner._gate)
            {
                killed = Killed;
                if (owner._live == this) owner._live = null;
                orphans = TakeWaiting();
            }
            string message = killed ? "Stopped." : DiedMessage();
            foreach (var (id, reply) in orphans) reply.TrySetResult(new ErrorReply(id, killed ? "Stopped" : ErrorKinds.WorkerDied, message));
            lock (_life)
            {
                _gone = true;
                try { process.WaitForExit(2000); }
                catch (InvalidOperationException) { }
                process.Dispose();
            }
            _finished.TrySetResult();
        }

        private string DiedMessage()
        {
            string code = "";
            try
            {
                if (process.WaitForExit(2000)) code = string.Create(CultureInfo.InvariantCulture, $" with exit code {process.ExitCode}");
            }
            catch (InvalidOperationException)
            {
            }
            string said;
            lock (_stderr) said = _stderr.Count == 0 ? "" : ": " + string.Join(" ", _stderr).Trim();
            if (said.Length > 400) said = said[..400] + "…";
            return $"The worker ended by itself{code}{said}. The next calculation starts a new one.";
        }

        private static long? IdOf(Reply reply) => reply switch
        {
            ResultReply r => r.Id,
            DigitsReply r => r.Id,
            ValueReply r => r.Id,
            ErrorReply r => r.Id,
            ProgressReply r => r.Id,
            FunctionsReply r => r.Id,
            PongReply r => r.Id,
            _ => null,
        };
    }
}
