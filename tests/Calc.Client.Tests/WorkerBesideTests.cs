using System.Diagnostics;
using Calc.Protocol;

namespace Calc.Client.Tests;

public class WorkerBesideTests
{
    // B10 spawns the worker from here. The SDK copies a referenced program's exe and config files by itself,
    // but not its DLLs or Natural, so an exe beside the tests proves nothing: run it.
    [Fact]
    public async Task TheWorkerRunsFromBesideTheTests()
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, Wire.WorkerFileName), ["--eval", "x"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
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
        Assert.True(worker.ExitCode == 0, $"exit {worker.ExitCode}: {await stderr}");
        Assert.StartsWith("0.3333", await stdout);
    }
}
