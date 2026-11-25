namespace Calc.Protocol;

/// <summary>What the Client and the worker agree on before any message: the version, and the worker's file name.</summary>
public static class Wire
{
    public const string Version = "1";

    /// <summary>The worker's executable, which sits beside the Client's program (Calc.Cli, Calc.App and the tests).</summary>
    public static string WorkerFileName => OperatingSystem.IsWindows() ? "calc-worker.exe" : "calc-worker";
}
