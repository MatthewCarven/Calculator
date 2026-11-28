using System.Globalization;
using System.Runtime.InteropServices;

namespace Calc.Client;

/// <summary>How much commit is free: the memory Windows can still promise, RAM and page file together.</summary>
public interface ICommitInfo
{
    /// <summary>Free commit in bytes, or null when it can't be told.</summary>
    ulong? AvailableBytes { get; }
}

/// <summary>
/// Free commit from <c>GlobalMemoryStatusEx</c>'s <c>ullAvailPageFile</c>. This laptop runs out of commit before it runs
/// out of RAM, so that's the figure that says how big a worker can safely get.
/// </summary>
public sealed class CommitInfo : ICommitInfo
{
    public static CommitInfo System { get; } = new();

    public ulong? AvailableBytes
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return null;
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? status.AvailPageFile : null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

/// <summary>
/// The worker's heap cap, <c>DOTNET_GCHeapHardLimit</c>: the setting, 4 GiB by default, or free commit less 1 GiB if
/// that's smaller, worked in Int128 so a shortfall is negative rather than wrapping. Below 256 MiB no worker starts: the
/// runtime reads 0 as no cap, wraps a negative one to no cap, and won't start at all under a tiny one.
/// </summary>
public static class HeapCap
{
    public const long Default = 4L << 30;
    public const long Floor = 256L << 20;
    public const long Reserve = 1L << 30;

    /// <summary>The cap to start a worker with, or null if that would be below <see cref="Floor"/>.</summary>
    public static long? For(long setting, ulong? availableCommit)
    {
        Int128 cap = setting;
        if (availableCommit is ulong free) cap = Int128.Min(cap, (Int128)free - Reserve);
        return cap < Floor ? null : (long)cap;
    }

    /// <summary>
    /// The variable's text. The runtime reads it as hex, so "4294967296" would be 266 GiB, which is no cap at all:
    /// 4 GiB is "0x100000000".
    /// </summary>
    public static string EnvironmentValue(long cap) => "0x" + cap.ToString("X", CultureInfo.InvariantCulture);
}
