using System.Diagnostics;
using Calc.Protocol;

namespace Calc.Worker.Tests;

public class EvalFlagTests
{
    [Fact]
    public async Task EvaluatesAtTheBitsAsked()
    {
        // B4's Done-when, now through B6's text: 53 bits guarantee 15 digits (rule 9), so the noise in the 17th goes. --raw
        // and binary64 show every digit that reads back.
        var (code, stdout, stderr) = await RunWorker("--eval", "0.1+0.2", "--bits", "53", "--exact", "off");
        Assert.True(code == 0, $"exit {code}: {stderr}");
        Assert.Equal("0.3", stdout.TrimEnd('\r', '\n'));
        Assert.Equal("flags: Inexact", stderr.TrimEnd('\r', '\n'));
        Assert.Equal("0.30000000000000004", (await RunWorker("--eval", "0.1+0.2", "--bits", "53", "--raw", "--exact", "off")).Stdout.TrimEnd('\r', '\n'));
        Assert.Equal("0.30000000000000004", (await RunWorker("--eval", "0.1+0.2", "--format", "binary64")).Stdout.TrimEnd('\r', '\n'));

        // Exact is on by default, as in the window (B7): 0.1 + 0.2 is exactly 3/10, with no flags.
        var exact = await RunWorker("--eval", "0.1+0.2", "--bits", "53", "--raw");
        Assert.Equal("3/10", exact.Stdout.TrimEnd('\r', '\n'));
        Assert.Equal("", exact.Stderr);
    }

    [Theory]
    [InlineData("1+1", "2")]
    // Up is 0.33333333333333337; its 15 shown digits round to nearest, since the mode governs arithmetic, not text.
    [InlineData("1/3 --mode up --bits 53", "0.333333333333333")]
    [InlineData("1/3 --mode up --bits 53 --raw --exact off", "0.33333333333333337")]
    [InlineData("1/3 --raw", "1/3")]
    [InlineData("2^100 --raw", "1267650600228229401496703205376")]
    [InlineData("1/3 --mode down --bits 53", "0.333333333333333")]
    [InlineData("1/0 --format binary64", "Infinity")]
    [InlineData("0/0 --format binary64", "NaN")]
    [InlineData("0x1.8p+1+0o17", "18")]
    [InlineData("1/3 --bits 53 --notation engineering", "333.333333333333E-003")]
    [InlineData("1234567.5 --bits 53 --group ,", "1,234,567.5")]
    public async Task PrintsTheResult(string arguments, string expected)
    {
        string[] parts = arguments.Split(' ');
        var (code, stdout, stderr) = await RunWorker(["--eval", parts[0], .. parts[1..]]);
        Assert.True(code == 0, $"exit {code}: {stderr}");
        Assert.Equal(expected, stdout.TrimEnd('\r', '\n'));
    }

    [Fact]
    public async Task ExactValuesShowAsTheWindowWillShowThem()
    {
        // B8's Done-when: exactly 3/10 shows as 0.3, and 2^1000 in full; --caption adds the line under the result.
        var tenths = await RunWorker("--eval", "0.1+0.2", "--caption");
        Assert.Equal(["0.3", "exact = 3/10"], tenths.Stdout.TrimEnd('\r', '\n').Split(Environment.NewLine));
        Assert.Equal("", tenths.Stderr);
        var power = await RunWorker("--eval", "2^1000", "--caption");
        string[] lines = power.Stdout.TrimEnd('\r', '\n').Split(Environment.NewLine);
        Assert.Equal(302, lines[0].Length);
        Assert.StartsWith("10715086071862673209484250490600018105614048117055336074437503883703510511249361224931983788156958581275946729175531468251871452856923140435984577574698574803934567774824230985421074605062371141877954182153046474983581941267398767559165543946077062914571196477686542167660429831652624386837205668069376", lines[0]);
        Assert.Equal("exact, 302 digits", lines[1]);
        var root = await RunWorker("--eval", "sqrt(2)", "--bits", "53", "--caption");
        Assert.Equal(["1.4142135623731", "float, 15 digits"], root.Stdout.TrimEnd('\r', '\n').Split(Environment.NewLine));
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
    [InlineData("--eval", "1", "--notation", "roman")]
    [InlineData("--eval", "1", "--notation", "7")]
    [InlineData("--eval", "1", "--exact", "maybe")]
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
