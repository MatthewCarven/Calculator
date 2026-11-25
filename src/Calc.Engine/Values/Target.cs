using Natural;

namespace Calc.Engine.Values;

/// <summary>
/// What every result rounds to: a precision in bits, with an exponent bounded only by a long, or an IEEE format, with
/// its subnormals, infinities and NaN. The policy follows: Mathematical at bits, IEEE at a format.
/// </summary>
public readonly record struct Target
{
    private readonly int _bits;

    private Target(int bits, IeeeFormat? format)
    {
        _bits = bits;
        Format = format;
    }

    /// <summary>The format, or null for a precision in bits.</summary>
    public IeeeFormat? Format { get; }

    public int Precision => _bits;

    public bool IsFormat => Format is not null;

    public static Target Bits(int bits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bits, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bits, ApFloat.MaxPrecision);
        return new Target(bits, null);
    }

    public static Target Of(IeeeFormat format) => new(format.Precision, format);

    /// <summary>The evaluation's one context (rule 4): its target overrides every operand's precision.</summary>
    public IeeeContext NewContext(RoundingMode mode) =>
        Format is IeeeFormat format ? new IeeeContext(format, mode) : new IeeeContext(_bits, mode);

    public override string ToString() => Format is IeeeFormat format ? format.ToString() : $"{_bits} bits";
}
