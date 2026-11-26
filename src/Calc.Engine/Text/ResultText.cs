using System.Globalization;
using System.Text;
using Calc.Engine.Eval;
using Calc.Engine.Values;
using Calc.Protocol;
using Natural;

namespace Calc.Engine.Text;

/// <summary>
/// A value as people see it: <see cref="Short"/> for a list row, under 40 characters; <see cref="Main"/> for the result
/// area; its <see cref="Kind"/>; the <see cref="Digits"/> its precision guarantees (rule 9); and whether Main holds them all.
/// </summary>
public sealed record ResultText(string Short, string Main, string Kind, int Digits, bool Complete)
{
    /// <summary>Main shows every guaranteed digit by G up to here: through 4,096 bits, whose 1,232 digits take 10 ms.</summary>
    public const int MainLimit = 1300;

    /// <summary>Past <see cref="MainLimit"/>, Main is a preview of this many digits, and C7's viewer has the rest.</summary>
    public const int PreviewDigits = 100;

    /// <summary>Previews and Short round to this many bits first: about 3 ms, where E99 of a full 2^20-bit value takes 330.</summary>
    private const int PreviewBits = 1024;

    /// <summary>Short stays under 40 characters.</summary>
    public const int ShortWidth = 39;

    /// <summary>Fixed notation falls back to scientific past this many digit places (rule 11: F of a huge value hangs).</summary>
    public const int FixedLimit = 1400;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static ResultText Of(Value value, Target target, TextOptions? options = null)
    {
        options ??= TextOptions.Default;
        if (value is not FloatValue { Number: var x })
            throw new ArgumentException($"No text for a {value.GetType().Name} yet.", nameof(value));
        try
        {
            // At bits a negative zero is just zero (rule: fold -0 at bits). At a format it's a real IEEE value, and stays.
            if (x.IsZero && x.IsNegative && !target.IsFormat) x = ApFloat.Abs(x);
            int digits = Protocol.Digits.For(target.Precision);
            if (x.IsNaN || x.IsInfinity)
            {
                string word = x.ToString("R", Invariant);
                return new ResultText(word, word, "float", digits, true);
            }
            return target.Format is IeeeFormat format
                ? OfFormat(x, format, digits, options)
                : OfBits(x, digits, options);
        }
        catch (OverflowException e)
        {
            // At the very top of the exponent range, decimal text can't be made (A2).
            throw ErrorMap.From(e, null);
        }
    }

    private static ResultText OfBits(ApFloat x, int digits, TextOptions options)
    {
        string shortText = ShortOf(x, digits);
        if (digits <= MainLimit)
        {
            string main = options.Notation == Notation.Auto
                ? x.ToString("G" + digits, Invariant)
                : Render(Parts.Of(x.ToString("E" + (digits - 1), Invariant)), options.Notation);
            return new ResultText(shortText, Group(main, options), "float", digits, true);
        }
        ApFloat preview = x.WithPrecision(PreviewBits);
        string text = options.Notation == Notation.Auto
            ? preview.ToString("G" + PreviewDigits, Invariant)
            : Render(Parts.Of(preview.ToString("E" + (PreviewDigits - 1), Invariant)), options.Notation);
        return new ResultText(shortText, Group(text, options), "float", digits, false);
    }

    /// <summary>
    /// At a format, the shortest text that reads back to the same bits: "R", within rule 10 since formats are at most
    /// 237 bits, except for subnormals, whose fewer bits R doesn't know about; for those, G1 upward until it reads back.
    /// </summary>
    private static ResultText OfFormat(ApFloat x, IeeeFormat format, int digits, TextOptions options)
    {
        string shortest = IsSubnormal(x, format) ? ShortestSubnormal(x, format) : x.ToString("R", Invariant);
        string main = options.Notation == Notation.Auto ? shortest : Render(Parts.Of(shortest), options.Notation);
        string shortText = shortest.Length <= ShortWidth ? shortest : ShortOf(x, digits);
        return new ResultText(shortText, Group(main, options), "float", digits, true);
    }

    /// <summary>G at up to 17 digits, fewer if the text would reach 40 characters: a 19-digit exponent leaves room for 15.</summary>
    private static string ShortOf(ApFloat x, int digits)
    {
        ApFloat rounded = x.WithPrecision(PreviewBits);
        for (int k = Math.Min(digits, 17); ; k--)
        {
            string text = rounded.ToString("G" + k, Invariant);
            if (text.Length <= ShortWidth || k == 1) return text;
        }
    }

    private static bool IsSubnormal(ApFloat x, IeeeFormat format)
    {
        if (x.IsZero) return false;
        long emin = 2 - (1L << (format.ExponentBits - 1));
        return x.Exponent + ApFloat.Abs(x).Significand.BitLength - 1 < emin;
    }

    private static string ShortestSubnormal(ApFloat x, IeeeFormat format)
    {
        var reader = new IeeeContext(format);
        for (int k = 1; k < format.Precision; k++)
        {
            string text = x.ToString("G" + k, Invariant);
            ApFloat back = reader.Parse(text, Invariant);
            if (back.CompareTo(x) == 0 && back.IsNegative == x.IsNegative) return text;
        }
        return x.ToString("R", Invariant);
    }

    /// <summary>A number's sign, its significant digits with no leading zeros, and the decimal exponent of the first.</summary>
    private readonly record struct Parts(bool Negative, string Digits, long Exponent)
    {
        /// <summary>From Natural's text in any of its shapes: 0.333, 123456.789, 1.23E+005, -0.000E+000.</summary>
        public static Parts Of(string text)
        {
            bool negative = text.StartsWith('-');
            string body = negative ? text[1..] : text;
            long exponent = 0;
            int e = body.IndexOf('E');
            if (e >= 0)
            {
                exponent = long.Parse(body[(e + 1)..], NumberStyles.AllowLeadingSign, Invariant);
                body = body[..e];
            }
            int point = body.IndexOf('.');
            string whole = point < 0 ? body : body[..point];
            string all = point < 0 ? body : whole + body[(point + 1)..];
            string significant = all.TrimStart('0');
            if (significant.Length == 0) return new Parts(negative, new string('0', all.Length), 0);
            int leadingZeros = all.Length - significant.Length;
            return new Parts(negative, significant, checked(exponent + whole.Length - 1 - leadingZeros));
        }

        public bool IsZero => Digits.TrimStart('0').Length == 0;
    }

    private static string Render(Parts parts, Notation notation)
    {
        // Aligned keeps every digit for its fixed width; the rest drop trailing zeros, keeping one.
        string digits = notation == Notation.Aligned ? parts.Digits : TrimZeros(parts.Digits);
        string sign = parts.Negative ? "-" : "";
        switch (notation)
        {
            case Notation.Engineering:
            {
                long exponent = parts.IsZero ? 0 : FloorToThree(parts.Exponent);
                int whole = (int)(parts.Exponent - exponent + 1);
                return sign + Point(digits, whole) + ExponentText(exponent);
            }
            case Notation.Fixed:
            {
                if (parts.IsZero) return sign + "0";
                long places = parts.Exponent >= 0 ? Math.Max(parts.Exponent + 1, digits.Length) : digits.Length - parts.Exponent;
                if (places > FixedLimit) return sign + Point(digits, 1) + ExponentText(parts.Exponent);
                if (parts.Exponent >= 0) return sign + Point(digits, (int)parts.Exponent + 1);
                return sign + "0." + new string('0', (int)(-parts.Exponent - 1)) + digits;
            }
            default:
                return sign + Point(digits, 1) + ExponentText(parts.IsZero ? 0 : parts.Exponent);
        }
    }

    /// <summary>Digits with the point after <paramref name="whole"/> of them, padding with zeros if there are fewer.</summary>
    private static string Point(string digits, int whole)
    {
        if (digits.Length <= whole) return digits + new string('0', whole - digits.Length);
        return digits[..whole] + "." + digits[whole..];
    }

    private static string TrimZeros(string digits)
    {
        string trimmed = digits.TrimEnd('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    /// <summary>The largest multiple of 3 at or below e, so -1 goes to -3, not 0.</summary>
    private static long FloorToThree(long e) => e >= 0 ? e - e % 3 : -((-e + 2) / 3 * 3);

    /// <summary>As Natural writes it: E, a sign, and at least three digits.</summary>
    private static string ExponentText(long exponent) =>
        "E" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("000", Invariant);

    /// <summary>
    /// Separators between groups of three digits, by string work only: the whole part from the point leftward, the
    /// fraction from the point rightward if asked. Removing them gives the ungrouped text back.
    /// </summary>
    private static string Group(string text, TextOptions options)
    {
        if (string.IsNullOrEmpty(options.Separator) || text.Length == 0 || !char.IsAsciiDigit(text[text.Length > 1 && text[0] == '-' ? 1 : 0]))
            return text;
        int e = text.IndexOf('E');
        string mantissa = e < 0 ? text : text[..e], tail = e < 0 ? "" : text[e..];
        string sign = mantissa.StartsWith('-') ? "-" : "";
        mantissa = mantissa[sign.Length..];
        int point = mantissa.IndexOf('.');
        string whole = point < 0 ? mantissa : mantissa[..point], fraction = point < 0 ? "" : mantissa[(point + 1)..];

        var grouped = new StringBuilder(sign);
        for (int i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0) grouped.Append(options.Separator);
            grouped.Append(whole[i]);
        }
        if (point >= 0)
        {
            grouped.Append('.');
            for (int i = 0; i < fraction.Length; i++)
            {
                if (options.GroupFraction && i > 0 && i % 3 == 0) grouped.Append(options.Separator);
                grouped.Append(fraction[i]);
            }
        }
        return grouped.Append(tail).ToString();
    }
}
