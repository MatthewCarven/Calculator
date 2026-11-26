using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Eval;

/// <summary>
/// A float operation on exact arguments, right for the exact values rather than for the values rounded first. Rounding
/// first throws away what the calculator holds: at 256 bits ln(1+1e-80) would be 0, and sqrt(1/7) at 53 bits would be
/// wrong in the last place. The helper every W session uses.
/// </summary>
/// <remarks>
/// <para>Each exact argument is rounded down and up at W = p + 64 + max(0, top(x)) bits, and f is called on every corner:
/// two for one argument that the width can't hold, four for two. If every corner gives the same value, the true answer,
/// which lies between them for a monotone f, rounds to it too. If not, W doubles. Once W reaches the bound, p + 64 +
/// max(0, top(x)) + bits(n) + bits(d), summed over the arguments, the corners still disagreeing, the answer is taken from
/// the arguments rounded to nearest at the bound, and stays a float: log10(0.001) in a directed mode never settles,
/// because its answer, -3, is exactly where the rounding changes. Past that width an argument is as close as it can come
/// to such a point without being on it.</para>
/// <para>A corner that errors, or gives infinity or NaN, or raises DivideByZero or Invalid, counts as disagreeing until
/// the bound: 1-1e-100 rounds up to exactly 1 at W = 320, and atanh of that has a finite answer.</para>
/// <para>An argument the width holds exactly, such as a small integer or a fraction over a power of two, isn't a corner:
/// f gets it as itself. A float argument is passed unchanged. Format targets and Exact off don't come here: they keep
/// hardware semantics.</para>
/// </remarks>
public static class RoundedCall
{
    /// <param name="floats">The evaluation's float arithmetic: f rounds through its context, and its flags are kept.</param>
    /// <param name="arguments">Exact and float values.</param>
    /// <param name="f">The float operation, given each argument as a <see cref="FloatValue"/>, which may be wider than the
    /// target: it rounds once into the target.</param>
    /// <param name="absolute">Whether W covers the argument's whole part too, for functions that care about absolute error,
    /// as sin and x + y do. Operations that care only about relative error, as × ÷ √ and ^ do, pass false, so a 2^20-bit
    /// integer is rounded to about p bits, not passed whole.</param>
    /// <param name="extraBits">Added to W and to the bound: for x^y, bits(y), since y multiplies the relative error.</param>
    /// <param name="unsettled">What to give, in place of the nearest, when the corners still disagree at the bound: the
    /// exact answer, where the caller can find one there. A remainder whose quotient is whole can, being 0 where it jumps;
    /// so will log10 of a power of ten.</param>
    public static Value Of(FloatArithmetic floats, IReadOnlyList<Value> arguments, Func<IReadOnlyList<Value>, Value> f,
                           bool absolute = true, long extraBits = 0, Func<Value>? unsettled = null)
    {
        long top = 0, sizes = 0;
        bool anyExact = false;
        foreach (Value argument in arguments)
        {
            if (Fraction(argument) is not var (n, d)) continue;
            anyExact = true;
            // The bits of the whole part, near enough: n/d lies within a factor of 2 of 2^(bits(n) - bits(d)).
            if (absolute) top = Math.Max(top, n.BitLength - d.BitLength);
            sizes = checked(sizes + n.BitLength + d.BitLength);
        }
        if (!anyExact) return f(arguments);

        long start = Math.Min(checked(floats.Precision + 64L + extraBits + top), ApFloat.MaxPrecision);
        long bound = Math.Min(checked(start + sizes), ApFloat.MaxPrecision);
        IeeeFlags before = floats.Flags;
        for (long width = start; ; width = Math.Min(checked(width * 2), bound))
        {
            if (Settled(floats, arguments, f, (int)width, before) is Value agreed) return agreed;
            if (width >= bound) break;
        }
        floats.Flags = before;
        return unsettled is not null ? unsettled() : f(Rounded(arguments, (int)bound, RoundingMode.ToNearestEven));
    }

    /// <summary>
    /// f at every corner of the arguments' brackets at this width: the value if all agree, with the union of their flags;
    /// otherwise null, with the flags as they were.
    /// </summary>
    private static Value? Settled(FloatArithmetic floats, IReadOnlyList<Value> arguments, Func<IReadOnlyList<Value>, Value> f,
                                  int width, IeeeFlags before)
    {
        Value[] low = Rounded(arguments, width, RoundingMode.TowardNegative);
        Value[] high = Rounded(arguments, width, RoundingMode.TowardPositive);
        var open = new List<int>();
        for (int i = 0; i < arguments.Count; i++)
            if (!Same(low[i], high[i])) open.Add(i);
        // Every argument held exactly at this width: one call on the exact values, whose answer, or error, is final.
        if (open.Count == 0)
        {
            floats.Flags = before;
            return f(low);
        }

        Value? agreed = null;
        IeeeFlags flags = before;
        for (int corner = 0; corner < 1 << open.Count; corner++)
        {
            var at = (Value[])low.Clone();
            for (int j = 0; j < open.Count; j++)
                if (((corner >> j) & 1) != 0) at[open[j]] = high[open[j]];

            floats.Flags = before;
            Value result;
            try
            {
                result = f(at);
            }
            catch (Exception e) when (e is ArithmeticException || e is CalcError { Kind: ErrorKind.Domain or ErrorKind.DivideByZero or ErrorKind.Overflow or ErrorKind.NotAvailable })
            {
                // An end at a pole or past an edge, which a wider end may not be. Anything else, such as Stopped, isn't
                // about the end, so it goes on up.
                floats.Flags = before;
                return null;
            }
            IeeeFlags raised = floats.Flags & ~before;
            bool erred = (raised & (IeeeFlags.DivideByZero | IeeeFlags.Invalid)) != 0
                         || result is FloatValue { Number: { IsFinite: false } };
            if (erred || (agreed is not null && !Same(agreed, result)))
            {
                floats.Flags = before;
                return null;
            }
            agreed ??= result;
            flags |= floats.Flags;
        }
        floats.Flags = flags;
        return agreed;
    }

    /// <summary>Each exact argument rounded to the width in the mode, as a float; floats as they are.</summary>
    private static Value[] Rounded(IReadOnlyList<Value> arguments, int width, RoundingMode mode)
    {
        var context = new IeeeContext(width, mode);
        var result = new Value[arguments.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = arguments[i] switch
            {
                IntValue n => new FloatValue(context.Convert(n.Number)),
                RatioValue r => new FloatValue(context.Divide(r.Numerator, r.Denominator)),
                var other => other,
            };
        }
        return result;
    }

    /// <summary>The same value, -0 and +0 told apart.</summary>
    private static bool Same(Value a, Value b) => (a, b) switch
    {
        (FloatValue { Number: var x }, FloatValue { Number: var y }) =>
            x.IsNaN ? y.IsNaN : !y.IsNaN && x.CompareTo(y) == 0 && x.IsNegative == y.IsNegative,
        _ => a.Equals(b),
    };

    private static (ApInt Numerator, ApInt Denominator)? Fraction(Value v) => v switch
    {
        IntValue i => (i.Number, ApInt.One),
        RatioValue r => (r.Numerator, r.Denominator),
        _ => null,
    };
}
