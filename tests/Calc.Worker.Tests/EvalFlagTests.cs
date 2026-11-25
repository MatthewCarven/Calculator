using System.Diagnostics;
using Calc.Protocol;

namespace Calc.Worker.Tests;

public class EvalFlagTests
{
    [Fact]
    public async Task EvalPrintsOneThirdToSeventySixDigits()
    {
        var (code, stdout, stderr) = await RunWorker("--eval", "x");
        Assert.True(code == 0, $"exit {code}: {stderr}");
        Assert.Equal("0." + new string('3', 76), stdout.TrimEnd('\r', '\n'));
    }

    [Fact]
    public async Task NoArgumentsIsAUsageError()
    {
        var (code, stdout, stderr) = await RunWorker();
        Assert.Equal(2, code);
        Assert.Equal("", stdout);
        Assert.StartsWith("usage: calc-worker", stderr);
    }

    private static async Task<(int Code, string Stdout, string Stderr)> RunWorker(params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, Wire.WorkerFileName))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var worker = Process.Start(start)!;
        var stdout = worker.StandardOutput.ReadToEndAsync();
        var stderr = worker.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await worker.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            worker.Kill(entireProcessTree: true);
            Assert.Fail("calc-worker ran for 30 s.");
        }
        return (worker.ExitCode, await stdout, await stderr);
    }
}
