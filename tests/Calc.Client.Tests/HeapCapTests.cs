namespace Calc.Client.Tests;

/// <summary>B10's heap cap: the smaller of the setting and free commit less 1 GiB, in Int128, never below 256 MiB, written in hex.</summary>
public class HeapCapTests
{
    private const long GiB = 1L << 30, MiB = 1L << 20;

    [Theory]
    [InlineData(4 * GiB, null, 4 * GiB)]                 // free commit unknown: the setting
    [InlineData(4 * GiB, 100 * GiB, 4 * GiB)]            // plenty free: the setting
    [InlineData(4 * GiB, 3 * GiB, 2 * GiB)]              // free less the reserve
    [InlineData(4 * GiB, 5 * GiB / 4, 256 * MiB)]        // the floor exactly
    [InlineData(4 * GiB, 5 * GiB / 4 - 1, null)]         // a byte under it
    [InlineData(4 * GiB, GiB, null)]                     // zero: the runtime would read it as no cap
    [InlineData(4 * GiB, GiB / 2, null)]                 // negative: unsigned, it would wrap to no cap
    [InlineData(4 * GiB, 0L, null)]
    [InlineData(100 * MiB, 100 * GiB, null)]             // a setting under the floor
    [InlineData(long.MaxValue, 8 * GiB, 7 * GiB)]
    public void TheCap(long setting, long? free, long? expected) =>
        Assert.Equal(expected, HeapCap.For(setting, free is long f ? (ulong)f : null));

    [Fact]
    public void AllTheCommitThereIsDoesntOverflow() => Assert.Equal(4 * GiB, HeapCap.For(4 * GiB, ulong.MaxValue));

    [Theory]
    [InlineData(4 * GiB, "0x100000000")]
    [InlineData(256 * MiB, "0x10000000")]
    [InlineData(512 * MiB, "0x20000000")]
    public void TheVariableIsHex(long cap, string text) => Assert.Equal(text, HeapCap.EnvironmentValue(cap));

    [Fact]
    public void ThisMachinesCommitCanBeRead()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.InRange(CommitInfo.System.AvailableBytes!.Value, 1UL << 20, ulong.MaxValue);
    }
}
