namespace Calc.Client.Tests;

internal static class SoonExtensions
{
    /// <summary>The task's result, or a failure after the given seconds (rule 16: a hang should fail, not stall the suite).</summary>
    public static Task<T> Soon<T>(this Task<T> task, int seconds = 30) => task.WaitAsync(TimeSpan.FromSeconds(seconds));
}
