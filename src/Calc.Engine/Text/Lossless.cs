using System.Globalization;
using Calc.Engine.Values;
using Natural;

namespace Calc.Engine.Text;

/// <summary>
/// One tagged text form for every kind of value, exact and complete, for the wire (B9) and saved sessions (H6):
/// <c>i:&lt;sign&gt;&lt;hex&gt;</c>, <c>r:&lt;sign&gt;&lt;hex&gt;/&lt;hex&gt;</c>, and <c>f:&lt;target&gt;:&lt;A text&gt;</c>, where the target is
/// bits (<c>256</c>) or a format's exponent bits and precision (<c>11/53</c>).
/// </summary>
/// <remarks>Hex is magnitude and sign, by rule 6: Natural's "X" is two's complement, so -255 would be F01. Here it's -FF.</remarks>
public static class Lossless
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Write(Value value, Target target) => value switch
    {
        IntValue i => "i:" + Signed(i.Number),
        RatioValue r => "r:" + Signed(r.Numerator) + "/" + Hex(r.Denominator),
        FloatValue f => "f:" + TargetText(target) + ":" + f.Number.ToString("A", Invariant),
        _ => throw new ArgumentException($"No lossless form for a {value.GetType().Name}.", nameof(value)),
    };

    /// <summary>The value, and for a float the target it belongs to (rule 14).</summary>
    /// <exception cref="FormatException">It isn't lossless text.</exception>
    public static (Value Value, Target? Target) Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.StartsWith("i:", StringComparison.Ordinal))
            return (new IntValue(ReadSigned(text[2..])), null);
        if (text.StartsWith("r:", StringComparison.Ordinal))
        {
            int slash = text.IndexOf('/');
            if (slash < 0) throw Bad(text);
            ApInt numerator = ReadSigned(text[2..slash]), denominator = ReadSigned(text[(slash + 1)..]);
            if (denominator.Sign <= 0) throw Bad(text);
            Value ratio = RatioValue.Of(numerator, denominator);
            // Written in lowest terms with a denominator above 1, so it must read back as a ratio, unchanged.
            if (ratio is not RatioValue r || r.Numerator != numerator || r.Denominator != denominator) throw Bad(text);
            return (ratio, null);
        }
        if (text.StartsWith("f:", StringComparison.Ordinal))
        {
            int colon = text.IndexOf(':', 2);
            if (colon < 0) throw Bad(text);
            Target target = ReadTarget(text[2..colon]) ?? throw Bad(text);
            // A text is exact at its precision, so this reads it back without rounding.
            ApFloat number = target.NewContext(RoundingMode.ToNearestEven).Parse(text[(colon + 1)..], Invariant);
            return (new FloatValue(number), target);
        }
        throw Bad(text);
    }

    private static string Signed(ApInt n) => (n.IsNegative ? "-" : "") + Hex(n);

    private static string Hex(ApInt n)
    {
        string hex = ApInt.Abs(n).ToString("X", Invariant).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    private static ApInt ReadSigned(string text)
    {
        bool negative = text.StartsWith('-');
        string hex = negative ? text[1..] : text;
        if (hex.Length == 0 || !hex.All(char.IsAsciiHexDigitUpper)) throw Bad(text);
        // 0x is Natural's own unsigned syntax, unlike the X style.
        ApInt n = ApInt.Parse("0x" + hex, Invariant);
        return negative ? -n : n;
    }

    private static string TargetText(Target target) =>
        target.Format is IeeeFormat f ? $"{f.ExponentBits}/{f.Precision}" : target.Precision.ToString(Invariant);

    private static Target? ReadTarget(string text)
    {
        int slash = text.IndexOf('/');
        if (slash < 0)
            return int.TryParse(text, NumberStyles.None, Invariant, out int bits) && bits >= 1 && bits <= ApFloat.MaxPrecision ? Target.Bits(bits) : null;
        if (!int.TryParse(text[..slash], NumberStyles.None, Invariant, out int exponentBits)
            || !int.TryParse(text[(slash + 1)..], NumberStyles.None, Invariant, out int precision))
            return null;
        try { return Target.Of(new IeeeFormat(exponentBits, precision)); }
        catch (ArgumentException) { return null; }
    }

    private static FormatException Bad(string text) =>
        new($"\"{(text.Length > 40 ? text[..40] + "…" : text)}\" isn't lossless text.");
}
