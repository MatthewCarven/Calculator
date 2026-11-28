namespace Calc.Protocol;

/// <summary>What the Client and the worker agree on before any message: the version, and the worker's file name.</summary>
public static class Wire
{
    public const string Version = "1";

    /// <summary>
    /// A result's lossless text goes on the wire up to this many characters, 1 MiB; past it the worker holds the value
    /// and the reply names it. Without this, 30 values at 2^28 bits would be 4 GB in the Client.
    /// </summary>
    public const int LosslessLimit = 1 << 20;

    /// <summary>The worker's executable, which sits beside the Client's program (Calc.Cli, Calc.App and the tests).</summary>
    public static string WorkerFileName => OperatingSystem.IsWindows() ? "calc-worker.exe" : "calc-worker";
}
