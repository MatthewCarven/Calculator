using System.Globalization;
using System.Text;
using Calc.Engine.Eval;
using Calc.Engine.Values;
using Calc.Protocol;
using Natural;

namespace Calc.Engine.Text;

/// <summary>
/// A value as people see it: <see cref="Short"/> for a list row, under 40 characters; <see cref="Main"/> for the result
/// area; its <see cref="Kind"/>, the chip; the <see cref="Digits"/> its precision guarantees (rule 9), or an exact
/// integer's digit count; and whether Main holds them all.
/// </summary>
/// <remarks>
/// The chips are "exact", "exact = n/d" for a fraction whose parts are short, "float", and "float (exact too large)".
/// <see cref="Caption"/> is the line under the result: "float, 76 digits", "exact, 302 digits", "exact, about 301,030
/// digits", "exact = 1/3".
/// </remarks>
public sealed record ResultText(string Short, string Main, string Kind, int Digits, bool Complete)
{
    /// <summary>The words under the result, for the window and the console calculator alike.</summary>
    public string Caption { get; init; } = Kind;

    /// <summary>A fraction's chip shows it as n/d while that's at most this long: "exact = 22/7".</summary>
    public const int FractionWidth = 20;

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
        if (value is IntValue or RatioValue)
        {
            try { return OfExact(value, target, options); }
            catch (OverflowException e) { throw ErrorMap.From(e, null); }
        }
        if (value is not FloatValue { Number: var x } floatValue)
            throw new ArgumentException($"No text for a {value.GetType().Name}.", nameof(value));
        // The chip: a float standing in for an exact answer past a budget says so.
        string kind = floatValue.ExactTooLarge ? "float (exact too large)" : "float";
        var text = OfFloat(x, target, options);
        return text with { Kind = kind, Caption = x.IsFinite ? $"{kind}, {Count(text.Digits)}" : kind };
    }

    /// <summary>
    /// An integer shows every digit while it has at most <see cref="MainLimit"/>, and past that a preview, as a float's
    /// would be, with about how many digits it has. A fraction shows its decimal, every digit worked out from n/d exactly,
    /// so the last one is right, not just right for the fraction rounded to the target first; its chip shows n/d too
    /// when that's short.
    /// </summary>
    private static ResultText OfExact(Value value, Target target, TextOptions options)
    {
        if (value is IntValue { Number: var n })
        {
            if (n.BitLength <= ExactDigitBits)
            {
                string whole = n.ToString();
                int count = whole.TrimStart('-').Length;
                string main = options.Notation == Notation.Auto ? whole : Render(Parts.Of(whole), options.Notation);
                string shortText = whole.Length <= ShortWidth ? whole : ShortOf(n, 17);
                return new ResultText(shortText, Group(main, options), "exact", count, true) { Caption = $"exact, {Count(count)}" };
            }
            int about = (int)Math.Floor((n.BitLength - 1) * 0.30102999566398120) + 1;
            return OfBits(n, about, options) with { Kind = "exact", Caption = $"exact, about {Count(about)}" };
        }

        var r = (RatioValue)value;
        int digits = Protocol.Digits.For(target.Precision);
        bool complete = digits <= MainLimit;
        int shown = complete ? digits : PreviewDigits;
        var (parts, exactly) = Decimal(r.Numerator, r.Denominator, shown);
        string mainText = options.Notation == Notation.Auto ? General(parts, shown) : Render(parts, options.Notation);
        // n/d as text only when it could be short: a 15,850-bit denominator takes longer to write in decimal than the rest.
        bool small = r.Numerator.BitLength <= 3.33 * FractionWidth && r.Denominator.BitLength <= 3.33 * FractionWidth;
        string chip = small && r.ToString() is { Length: <= FractionWidth } fraction ? "exact = " + fraction : "exact";
        string caption = chip != "exact" ? chip : exactly ? "exact" : $"exact, {Count(shown)} shown";
        return new ResultText(ShortOf(r, digits), Group(mainText, options), chip, digits, complete) { Caption = caption };
    }

    /// <summary>"1 digit", "76 digits", "301,030 digits".</summary>
    private static string Count(int digits) => digits.ToString("N0", Invariant) + (digits == 1 ? " digit" : " digits");

    /// <summary>
    /// n/d to <paramref name="count"/> significant digits, rounded to nearest with ties to even, from the integers
    /// themselves: the digits, padded with zeros to the count, the decimal exponent of the first, and whether that's n/d
    /// exactly. Cheap at any precision: the work is one division of numbers a few thousand digits long.
    /// </summary>
    private static (Parts Parts, bool Exactly) Decimal(ApInt n, ApInt d, int count)
    {
        ApInt magnitude = ApInt.Abs(n);
        ApInt low = ApInt.Pow(10, count - 1), high = low * 10;
        // floor(log10(|n|/d)) from each part's top 64 bits: right but for a value within a hair of a power of ten, which
        // the loop corrects. A size estimate, not a value, so a double is fine (rule 3 is about values).
        long exponent = (long)Math.Floor(Log10(magnitude) - Log10(d));
        for (int tries = 0; tries < 8; tries++)
        {
            long scale = count - 1 - exponent;
            ApInt numerator = scale >= 0 ? magnitude * ApInt.Pow(10, (int)scale) : magnitude;
            ApInt denominator = scale >= 0 ? d : d * ApInt.Pow(10, (int)-scale);
            var (q, remainder) = ApInt.DivRem(numerator, denominator);
            if (q < low) { exponent--; continue; }
            if (q >= high) { exponent++; continue; }
            int half = (remainder << 1).CompareTo(denominator);
            if (half > 0 || (half == 0 && !(q & ApInt.One).IsZero))
            {
                q += ApInt.One;
                if (q == high)
                {
                    q = low;
                    exponent++;
                }
            }
            return (new Parts(n.IsNegative, q.ToString(), exponent), remainder.IsZero);
        }
        throw new InvalidOperationException($"No decimal exponent for a {n.BitLength}-bit by {d.BitLength}-bit fraction in 8 tries.");
    }

    private static double Log10(ApInt x)
    {
        int drop = (int)Math.Max(0, x.BitLength - 64);
        return Math.Log10(ulong.CreateChecked(x >> drop)) + drop * 0.30102999566398120;
    }

    /// <summary>
    /// The G layout, from parts, as Natural's G&lt;k&gt; lays out a float: trailing zeros dropped, plain from 1E-005 up
    /// to 10^k, scientific outside that.
    /// </summary>
    private static string General(Parts parts, int k)
    {
        if (parts.IsZero) return parts.Negative ? "-0" : "0";
        string digits = TrimZeros(parts.Digits);
        string sign = parts.Negative ? "-" : "";
        long x = parts.Exponent;
        if (x < -5 || x >= k) return sign + Point(digits, 1) + ExponentText(x);
        if (x < 0) return sign + "0." + new string('0', (int)(-x - 1)) + digits;
        return sign + Point(digits, (int)x + 1);
    }

    /// <summary>A fraction's short text: G at up to 17 digits, from its exact decimal, fewer if it would reach 40 characters.</summary>
    private static string ShortOf(RatioValue r, int digits)
    {
        for (int k = Math.Min(digits, 17); ; k--)
        {
            string text = General(Decimal(r.Numerator, r.Denominator, k).Parts, k);
            if (text.Length <= ShortWidth || k == 1) return text;
        }
    }

    /// <summary>Integers this long have at most <see cref="MainLimit"/> digits: 1,300 / log10(2), less a bit.</summary>
    private const long ExactDigitBits = 4300;

    private static ResultText OfFloat(ApFloat x, Target target, TextOptions options)
    {
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
