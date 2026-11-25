using System.Diagnostics;
using Calc.Protocol;

namespace Calc.Worker.Tests;

public class EvalFlagTests
{
    [Fact]
    public async Task EvaluatesAtTheBitsAsked()
    {
        // B4's Done-when.
        var (code, stdout, stderr) = await RunWorker("--eval", "0.1+0.2", "--bits", "53");
        Assert.True(code == 0, $"exit {code}: {stderr}");
        Assert.Equal("0.30000000000000004", stdout.TrimEnd('\r', '\n'));
        Assert.Equal("flags: Inexact", stderr.TrimEnd('\r', '\n'));
    }

    [Theory]
    [InlineData("1+1", "2")]
    [InlineData("1/3 --mode up --bits 53", "0.33333333333333337")]
    [InlineData("1/3 --mode down --bits 53", "0.3333333333333333")]
    [InlineData("1/0 --format binary64", "Infinity")]
    [InlineData("0/0 --format binary64", "NaN")]
    [InlineData("0x1.8p+1+0o17", "18")]
    public async Task PrintsTheResult(string arguments, string expected)
    {
        string[] parts = arguments.Split(' ');
        var (code, stdout, stderr) = await RunWorker(["--eval", parts[0], .. parts[1..]]);
        Assert.True(code == 0, $"exit {code}: {stderr}");
        Assert.Equal(expected, stdout.TrimEnd('\r', '\n'));
    }

    [Fact]
    public async Task AnErrorGoesToStderrWithItsKindAndSpan()
    {
        var (code, stdout, stderr) = await RunWorker("--eval", "1/0");
        Assert.Equal(1, code);
        Assert.Equal("", stdout);
        Assert.Equal("DivideByZero at 1..2: Division by zero.", stderr.TrimEnd('\r', '\n'));
    }

    [Theory]
    [InlineData]
    [InlineData("--eval")]
    [InlineData("--eval", "1", "--bits")]
    [InlineData("--eval", "1", "--bits", "0")]
    [InlineData("--eval", "1", "--format", "binary63")]
    [InlineData("--eval", "1", "--mode", "sideways")]
    public async Task BadArgumentsAreAUsageError(params string[] arguments)
    {
        var (code, stdout, stderr) = await RunWorker(arguments);
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
