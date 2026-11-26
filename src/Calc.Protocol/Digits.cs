namespace Calc.Protocol;

/// <summary>
/// How many decimal digits a precision guarantees, and back (rule 9). In Protocol, so the Client can label precisions
/// without Natural: "256 bits, 76 digits".
/// </summary>
public static class Digits
{
    /// <summary>Natural's largest precision, 2^30 bits, here too so Protocol needs no Natural.</summary>
    public const int MaxBits = 1 << 30;

    /// <summary>floor(2^64 · log10 2). Shorter constants are wrong for about 6% of precisions.</summary>
    private const ulong Log10Of2 = 0x4D104D427DE7FBCCUL;

    /// <summary>
    /// max(1, floor((p − 1)·log10 2)): the digits every value at p bits is guaranteed to hold, 76 at 256 bits. The high
    /// 64 bits of (p − 1)·floor(2^64·log10 2), which is exact for every p to 2^30. Not Natural's DecimalDigitsFor, whose
    /// extra digits show rounding noise.
    /// </summary>
    public static int For(int bits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bits, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bits, MaxBits);
        return Math.Max(1, (int)Math.BigMul((ulong)(bits - 1), Log10Of2, out _));
    }

    /// <summary>
    /// The fewest bits that guarantee <paramref name="digits"/> digits: ceil(d·log2 10) + 1. For d = 1 it's 5, though
    /// <see cref="For"/> gives 1 digit from 1 bit up, since that's the floor For keeps.
    /// </summary>
    public static int BitsFor(int digits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, For(MaxBits));
        if (digits == 1) return 5;
        // An estimate, then settled against For, which is exact, so the answer is too.
        int bits = (int)Math.Min(MaxBits, Math.Ceiling(digits * 3.321928094887362) + 1);
        while (For(bits) < digits) bits++;
        while (bits > 1 && For(bits - 1) >= digits) bits--;
        return bits;
    }
}
