using System.Diagnostics;
using System.Text;
using Calc.Protocol;

namespace Calc.Worker.Tests;

/// <summary>The real calc-worker, from beside the tests, in its loop: requests in, replies out, every wait capped (rule 16).</summary>
internal sealed class WorkerProcess : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private readonly Process _process;

    private WorkerProcess(Process process) => _process = process;

    public static WorkerProcess Start(IReadOnlyDictionary<string, string>? environment = null)
    {
        var utf8 = new UTF8Encoding(false);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, Wire.WorkerFileName))
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
        };
        foreach (var (name, value) in environment ?? new Dictionary<string, string>()) start.Environment[name] = value;
        var process = Process.Start(start)!;
        process.StandardInput.AutoFlush = true;
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return new WorkerProcess(process);
    }

    public Process Process => _process;

    public void Send(Request request) => SendLine(Messages.Write(request));

    public void SendLine(string line)
    {
        _process.StandardInput.Write(line);
        _process.StandardInput.Write('\n');
    }

    public async Task<string> NextLineAsync(TimeSpan? timeout = null)
    {
        using var cancel = new CancellationTokenSource(timeout ?? Patience);
        try
        {
            return await _process.StandardOutput.ReadLineAsync(cancel.Token) ?? throw new Xunit.Sdk.XunitException("The worker's stdout ended.");
        }
        catch (OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException($"No reply within {(timeout ?? Patience).TotalSeconds} s.");
        }
    }

    public async Task<Reply> NextAsync(TimeSpan? timeout = null) => Messages.ReadReply(await NextLineAsync(timeout));

    public async Task<T> NextAsync<T>(TimeSpan? timeout = null) where T : Reply
    {
        Reply reply = await NextAsync(timeout);
        return reply as T ?? throw new Xunit.Sdk.XunitException($"Expected a {typeof(T).Name}, got {Messages.Write(reply)}");
    }

    /// <summary>Evaluates one line and returns its reply.</summary>
    public async Task<Reply> EvalAsync(long id, string source, EvalContext? context = null, AnsValue? ans = null, TextStyle? options = null)
    {
        Send(new EvalRequest(id, source, context ?? EvalContext.Default, ans, options));
        return await NextAsync();
    }

    public async Task<ResultReply> ResultAsync(long id, string source, EvalContext? context = null, AnsValue? ans = null, TextStyle? options = null)
    {
        Reply reply = await EvalAsync(id, source, context, ans, options);
        var result = reply as ResultReply ?? throw new Xunit.Sdk.XunitException($"{source}: {Messages.Write(reply)}");
        Assert.Equal(id, result.Id);
        return result;
    }

    public void CloseInput() => _process.StandardInput.Close();

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
        }
        _process.Dispose();
    }
}
