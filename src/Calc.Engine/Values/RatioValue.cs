using Natural;

namespace Calc.Engine.Values;

/// <summary>
/// An exact fraction, always in lowest terms, with the sign on the numerator and a denominator above 1. Make one with
/// <see cref="Of"/>, which gives an <see cref="IntValue"/> when the fraction is whole.
/// </summary>
public sealed record RatioValue : Value
{
    private RatioValue(ApInt numerator, ApInt denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public ApInt Numerator { get; }
    public ApInt Denominator { get; }

    /// <summary>n/d in lowest terms: an <see cref="IntValue"/> when d divides n. d must not be zero.</summary>
    public static Value Of(ApInt numerator, ApInt denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException();
        if (denominator.IsNegative)
        {
            numerator = -numerator;
            denominator = -denominator;
        }
        ApInt g = ApInt.GreatestCommonDivisor(ApInt.Abs(numerator), denominator);
        if (!g.IsZero && g != ApInt.One)
        {
            numerator /= g;
            denominator /= g;
        }
        return denominator == ApInt.One ? new IntValue(numerator) : new RatioValue(numerator, denominator);
    }

    /// <summary>Raw n/d, for tests, logs and <c>--raw</c>.</summary>
    public override string ToString() => $"{Numerator}/{Denominator}";
}
