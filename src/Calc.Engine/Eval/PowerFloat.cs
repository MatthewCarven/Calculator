using System.Numerics;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Eval;

/// <summary>
/// x^y, rounded once into the context's target, for every integer y; y = 1/2 by sqrt. Other y wait for Natural's Pow,
/// when W2 retires the bounds route. (Matthew's plan, B5's brief.)
/// </summary>
/// <remarks>
/// <para>T10's table first, the cases whose answer doesn't depend on y's size: x^±0 = 1 even for NaN; 1^y = 1 even for NaN;
/// (-1)^±∞ = 1; 0^y by y's sign and oddness, negative y raising DivideByZero; ±∞^y likewise; x^±∞ by |x| against 1;
/// a negative x to a non-integer is NaN and Invalid.</para>
/// <para>Then, for an integer n that fits a long, with x = ±m·2^e, m odd: the exact route when |n| ≤ min(4(p + 64), 2^30) /
/// bitlen(m), computing m^|n| exactly and rounding once; otherwise the bounds route, square-and-multiply on |x| rounded down
/// and up at W = p + 64 + 2·bitlen(n) bits, doubling W until both ends round to the same value. A y past a long gives
/// Overflow at bits, and ∞ or 0 at a format, when |x| ≥ 2 or |x| ≤ 1/2; between those, x^y is too near 1 for this to tell.</para>
/// </remarks>
internal static class PowerFloat
{
    public static ApFloat Power(IeeeContext ieee, ApFloat x, ApFloat y, TextSpan at)
    {
        if (y.IsZero || IsExactly(x, 1)) return ieee.Convert(1);
        if (x.IsNaN || y.IsNaN) return ieee.Convert(ApFloat.NaN);

        bool integer = ApFloat.IsInteger(y);
        bool odd = integer && y.Exponent == 0; // m is odd, so y is odd exactly when it's m·2^0
        if (y.IsInfinity)
        {
            if (IsExactly(x, -1)) return ieee.Convert(1);
            bool huge = (ApFloat.Abs(x).CompareTo(1) > 0) == !y.IsNegative;
            return ieee.Convert(huge ? ApFloat.PositiveInfinity : ApFloat.Zero);
        }
        if (x.IsZero)
        {
            // ±∞ for an odd negative y, +∞ for any other negative y, both by dividing by zero, which raises DivideByZero.
            if (y.IsNegative) return ieee.Divide(1, odd ? x : ApFloat.Zero);
            return ieee.Convert(odd ? x : ApFloat.Zero);
        }
        if (x.IsInfinity)
        {
            ApFloat magnitude = y.IsNegative ? ApFloat.Zero : ApFloat.PositiveInfinity;
            return ieee.Convert(odd && x.IsNegative ? -magnitude : magnitude);
        }
        if (!integer)
        {
            if (x.IsNegative)
            {
                ieee.Flags |= IeeeFlags.Invalid;
                return ieee.Convert(ApFloat.NaN);
            }
            if (y.CompareTo(0.5) == 0) return ieee.Sqrt(x);
            throw new CalcError(ErrorKind.NotAvailable, "Powers with a fraction aren't available yet, apart from ^0.5.", at);
        }
        if (IsExactly(x, -1)) return ieee.Convert(odd ? -1 : 1);

        if (Top(y) >= 63)
        {
            ApFloat magnitude = ApFloat.Abs(x);
            if (magnitude.CompareTo(2) < 0 && magnitude.CompareTo(0.5) > 0)
                throw new CalcError(ErrorKind.NotAvailable, "That power isn't available yet.", at);
            if (ieee.Format is not IeeeFormat format)
                throw new CalcError(ErrorKind.Overflow, "The result is too large: its exponent doesn't fit.", at);
            bool huge = (magnitude.CompareTo(1) > 0) == !y.IsNegative;
            ApFloat far = Beyond(format, huge);
            return ieee.Convert(x.IsNegative && odd ? -far : far);
        }
        return Integer(ieee, x, long.CreateChecked((ApInt)y));
    }

    private static ApFloat Integer(IeeeContext ieee, ApFloat x, long n)
    {
        bool negative = x.IsNegative && (n & 1) != 0;
        ulong k = n < 0 ? (ulong)(-(n + 1)) + 1 : (ulong)n;
        ApFloat magnitude = ApFloat.Abs(x);
        ApInt m = magnitude.Significand;
        long limit = Math.Min(4L * (ieee.Precision + 64), 1L << 30);

        // The exact route. A division, so the guard itself can't wrap.
        if (k <= (ulong)(limit / m.BitLength))
        {
            // The public constructor keeps m^k whole at MaxPrecision; it has no flags and ignores a format's range,
            // so the one rounding is ieee's.
            ApFloat exact = new(ApInt.Pow(m, int.CreateChecked(k)), checked(magnitude.Exponent * (long)k), ApFloat.MaxPrecision);
            if (negative) exact = -exact;
            return n > 0 ? ieee.Convert(exact) : ieee.Divide(1, exact);
        }
        return Bounds(ieee, magnitude, k, inverse: n < 0, negative);
    }

    /// <summary>The bounds route: |x|^k from below and above, doubling the width until both ends round alike.</summary>
    private static ApFloat Bounds(IeeeContext ieee, ApFloat magnitude, ulong k, bool inverse, bool negative)
    {
        IeeeFormat? format = ieee.Format;
        long width = ieee.Precision + 64L + 2L * (64 - BitOperations.LeadingZeroCount(k));
        while (true)
        {
            if (width > ApFloat.MaxPrecision)
                throw new CalcError(ErrorKind.Internal, "A power didn't settle within Natural's largest precision.");
            int w = (int)width;
            ApFloat? down = Raise(magnitude, k, w, RoundingMode.TowardNegative, format, inverse, out bool downHuge);
            ApFloat? up = Raise(magnitude, k, w, RoundingMode.TowardPositive, format, inverse, out bool upHuge);

            // Bounds on the final |x^n|: a stand-in far outside the format where a side was cut short.
            ApFloat low, high;
            if (!inverse)
            {
                low = down ?? Beyond(format!.Value, downHuge);
                high = up ?? Beyond(format!.Value, upHuge);
            }
            else
            {
                low = up is ApFloat u ? new IeeeContext(w, RoundingMode.TowardNegative).Divide(1, u) : Beyond(format!.Value, upHuge);
                high = down is ApFloat d ? new IeeeContext(w, RoundingMode.TowardPositive).Divide(1, d) : Beyond(format!.Value, downHuge);
            }
            if (negative) (low, high) = (-high, -low);

            if (Settle(ieee, low, high) is ApFloat result) return result;
            width *= 2;
        }
    }

    /// <summary>
    /// |x|^k by square-and-multiply, every step rounded the one way at <paramref name="width"/> bits, so the result is a
    /// bound. At a format, it stops once the partial product is past a threshold, which it can only go further past,
    /// and returns null, with <paramref name="finalHuge"/> saying whether x^n, after any inverse, overflows or underflows.
    /// Without that, the squarings would run out of exponent where hardware just gives ∞ or 0.
    /// </summary>
    private static ApFloat? Raise(ApFloat magnitude, ulong k, int width, RoundingMode direction, IeeeFormat? format, bool inverse, out bool finalHuge)
    {
        var context = new IeeeContext(width, direction);
        bool growing = magnitude.CompareTo(1) > 0;
        long overflowTop = 0, underflowTop = 0;
        if (format is IeeeFormat f)
        {
            long emax = Emax(f), smallest = SmallestSubnormal(f);
            // Thresholds on |x|^k's top bit, for the final x^n = |x|^k, or 1/|x|^k when inverse.
            overflowTop = inverse ? 2 - smallest : emax + 1;
            underflowTop = inverse ? -(emax + 2) : smallest - 2;
        }

        ApFloat r = ApFloat.One;
        for (int bit = 63 - BitOperations.LeadingZeroCount(k); bit >= 0; bit--)
        {
            r = context.Multiply(r, r);
            if (((k >> bit) & 1) != 0) r = context.Multiply(r, magnitude);
            if (format is not null)
            {
                long top = Top(r);
                if (growing && top > overflowTop) { finalHuge = !inverse; return null; }
                if (!growing && top < underflowTop) { finalHuge = inverse; return null; }
            }
        }
        finalHuge = false;
        return r;
    }

    /// <summary>
    /// Both ends rounded into the target: the value if they agree, with the union of their flags, and Inexact if the
    /// ends differ, since then the true value isn't representable; null, flags untouched, if they don't.
    /// </summary>
    private static ApFloat? Settle(IeeeContext ieee, ApFloat low, ApFloat high)
    {
        IeeeFlags before = ieee.Flags;
        ApFloat a = ieee.Convert(low);
        IeeeFlags lowFlags = ieee.Flags;
        ieee.Flags = before;
        ApFloat b = ieee.Convert(high);
        IeeeFlags highFlags = ieee.Flags;
        if (a.CompareTo(b) != 0 || a.IsNegative != b.IsNegative)
        {
            ieee.Flags = before;
            return null;
        }
        ieee.Flags = lowFlags | highFlags | (low.CompareTo(high) == 0 ? IeeeFlags.None : IeeeFlags.Inexact);
        return a;
    }

    /// <summary>A power of two far enough past the format that it rounds, in any mode, as overflow or underflow does.</summary>
    private static ApFloat Beyond(IeeeFormat format, bool huge) =>
        new(1, huge ? Emax(format) + 2 : SmallestSubnormal(format) - 3, 1);

    private static long Emax(IeeeFormat format) => (1L << (format.ExponentBits - 1)) - 1;

    /// <summary>The smallest subnormal's exponent: emin − (p − 1), where emin = 1 − emax.</summary>
    private static long SmallestSubnormal(IeeeFormat format) => 1 - Emax(format) - (format.Precision - 1);

    /// <summary>The exponent of a finite non-zero value's leading bit.</summary>
    private static long Top(ApFloat x) => x.Exponent + ApFloat.Abs(x).Significand.BitLength - 1;

    private static bool IsExactly(ApFloat x, long value) => !x.IsNaN && !x.IsInfinity && x.CompareTo(value) == 0;
}
