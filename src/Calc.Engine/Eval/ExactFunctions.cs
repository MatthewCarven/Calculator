using Natural;

namespace Calc.Engine.Eval;

/// <summary>
/// B5's rounding functions and comparison on exact values, exactly: n/d in lowest terms with d &gt; 0, an integer when d
/// is 1. Nothing here rounds, and nothing grows, since each answer is no bigger than its argument, so no budget applies.
/// The operations that can grow, and sqrt, which can cost a lot, are in <see cref="ExactArithmetic"/>, with the budgets.
/// </summary>
internal static class ExactFunctions
{
    /// <summary>The largest integer at or below n/d: floor(-7/2) is -4, where truncating gives -3.</summary>
    public static ApInt Floor(ApInt n, ApInt d)
    {
        if (d == ApInt.One) return n;
        var (q, r) = ApInt.DivRem(n, d);
        return r.IsNegative ? q - ApInt.One : q;
    }

    /// <summary>The smallest integer at or above n/d.</summary>
    public static ApInt Ceiling(ApInt n, ApInt d)
    {
        if (d == ApInt.One) return n;
        var (q, r) = ApInt.DivRem(n, d);
        return !r.IsZero && !r.IsNegative ? q + ApInt.One : q;
    }

    /// <summary>n/d with its fraction dropped, toward zero.</summary>
    public static ApInt Truncate(ApInt n, ApInt d) => d == ApInt.One ? n : ApInt.DivRem(n, d).Quotient;

    /// <summary>The nearest integer, a tie going to the even one, as the float round does: round(5/2) is 2, round(-3/2) is -2.</summary>
    public static ApInt Round(ApInt n, ApInt d)
    {
        if (d == ApInt.One) return n;
        var (q, r) = ApInt.DivRem(n, d);
        int half = (ApInt.Abs(r) << 1).CompareTo(d);
        bool away = half > 0 || (half == 0 && !(q & ApInt.One).IsZero);
        if (!away) return q;
        return r.IsNegative ? q - ApInt.One : q + ApInt.One;
    }

    /// <summary>
    /// -1, 0 or 1 as n1/d1 is below, at or above n2/d2. Signs first, then sizes from bit lengths, and only then the cross
    /// products, which by then are about the same size, so the cost stays near that of the operands.
    /// </summary>
    public static int Compare(ApInt n1, ApInt d1, ApInt n2, ApInt d2)
    {
        if (n1.Sign != n2.Sign) return n1.Sign.CompareTo(n2.Sign);
        if (n1.IsZero) return 0;
        // |n/d| lies in (2^(bits(n) - bits(d) - 1), 2^(bits(n) - bits(d) + 1)), so a gap of 2 or more decides it.
        long gap = checked((n1.BitLength - d1.BitLength) - (n2.BitLength - d2.BitLength));
        int magnitude = gap >= 2 ? 1
            : gap <= -2 ? -1
            : (ApInt.Abs(n1) * d2).CompareTo(ApInt.Abs(n2) * d1);
        return n1.IsNegative ? -Math.Sign(magnitude) : Math.Sign(magnitude);
    }
}
