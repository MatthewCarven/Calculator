#:project C:\Users\Admin\AppData\Local\Temp\claude\64370ccacde86111\2148620f-8ea3-424c-8a83-cb11a25a9795\scratchpad\natural-api\src\Natural\Natural.csproj
#:property PublishAot=false
// Traps a calculator author would hit. Everything here is cheap: no big numbers.
using System.Globalization;
using System.Numerics;
using System.Text;
using Natural;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"current culture at start: '{CultureInfo.CurrentCulture.Name}'");
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
var inv = CultureInfo.InvariantCulture;

static void H(string s) => Console.WriteLine($"\n#### {s}");
static void S(string label, Func<object?> f)
{
    try
    {
        object? v = f();
        string t = v?.ToString() ?? "null";
        if (t.Length > 110) t = t[..70] + $"...[{t.Length} chars]..." + t[^20..];
        Console.WriteLine($"{label,-58} = {t}");
    }
    catch (Exception e)
    {
        string m = e.Message.Replace("\r", " ").Replace("\n", " ");
        if (m.Length > 80) m = m[..80] + "...";
        Console.WriteLine($"{label,-58} ! {e.GetType().Name}: {m}");
    }
}
static string P(ApFloat x) => $"p={x.Precision} {x:R}";

// ------------------------------------------------------------------
H("A. per-value precision and contamination");
ApFloat x64 = ApFloat.Parse("0.1", 64);
ApFloat x256 = ApFloat.Parse("0.1", 256);
ApFloat x1024 = ApFloat.Parse("0.1", 1024);
ApFloat d53 = 0.1;   // the implicit double conversion
S("default(ApFloat).Precision", () => default(ApFloat).Precision);
S("ApFloat.One.Precision / Zero", () => $"{ApFloat.One.Precision} / {ApFloat.Zero.Precision}");
S("(ApFloat)0.1 precision (double literal)", () => P(d53));
S("(ApFloat)0.1f precision", () => P(0.1f));
S("(ApFloat)3 precision (int)", () => ((ApFloat)3).Precision);
S("(ApFloat)(ApInt)(1<<300) precision", () => ((ApFloat)(ApInt.One << 300)).Precision);
S("ApFloat.CreateChecked(0.1m) precision", () => P(ApFloat.CreateChecked(0.1m)));
S("x64 * 3            [int widens result to 256]", () => (x64 * 3).Precision);
S("x64 * 3.0          [double stays 64]", () => (x64 * 3.0).Precision);
S("x64 + ApFloat.One  [widens to 256]", () => (x64 + ApFloat.One).Precision);
S("x64 + ApFloat.Zero [widens to 256]", () => (x64 + ApFloat.Zero).Precision);
S("x1024 + 0.1        [double literal into 1024-bit sum]", () => P(x1024 + 0.1));
S("x1024 + x64 precision", () => (x1024 + x64).Precision);
S("Sqrt(x64) / Floor(x64) / ++x64 precision", () => { var y = x64; y++; return $"{ApFloat.Sqrt(x64).Precision} {ApFloat.Floor(x64).Precision} {y.Precision}"; });
S("x64 % 3 precision (larger of the two)", () => (x64 % 3).Precision);
S("ApFloat.Add(x256, x256, 64).Precision", () => ApFloat.Add(x256, x256, 64).Precision);
S("new IeeeContext(64).Multiply(x256, 3).Precision", () => new IeeeContext(64).Multiply(x256, 3).Precision);
S("new IeeeContext(1024).Add(x64, x64).Precision", () => new IeeeContext(1024).Add(x64, x64).Precision);
// How many decimal digits of the widened value are right.
static int RightDigits(ApFloat v, string exact, int p)
{
    string a = v.WithPrecision(p).ToString("F" + 300, CultureInfo.InvariantCulture), b = ApFloat.Parse(exact, p).ToString("F" + 300, CultureInfo.InvariantCulture);
    int i = 0; while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
    return i - 2;
}
S("stale x64.WithPrecision(1024): digits of 0.1 still right", () => RightDigits(x64.WithPrecision(1024), "0.1", 1024));
S("stale (0.1 double) into 1024-bit sum: digits right of 0.2", () => RightDigits(x1024 + 0.1, "0.2", 1024));
S("default ToString() of that contaminated value", () => (x1024 + 0.1).ToString());
S("DecimalDigitsFor 53/64/256/1024", () => string.Join(",", new[] { 53, 64, 256, 1024 }.Select(ApFloat.DecimalDigitsFor)));
S("0.1+0.2-0.3 at 256 (parse each)", () => (ApFloat.Parse("0.1") + ApFloat.Parse("0.2") - ApFloat.Parse("0.3")).ToString("R"));
S("0.1+0.2 at 256 R", () => (ApFloat.Parse("0.1") + ApFloat.Parse("0.2")).ToString("R"));
S("1/3*3 at 256 R", () => ((ApFloat)1 / 3 * 3).ToString("R"));
S("sqrt(2)^2-2 at 256 R", () => { var s = ApFloat.Sqrt(2); return (s * s - 2).ToString("R"); });
S("Equals ignores precision: (1@53).Equals(1@256)", () => ((ApFloat)1.0).Equals(ApFloat.Parse("1")));
S("hash equal across precisions", () => ((ApFloat)1.0).GetHashCode() == ApFloat.Parse("1").GetHashCode());
S("0.1@64 == 0.1@256", () => x64 == x256);

// ------------------------------------------------------------------
H("B. hex and binary text");
foreach (long v in new long[] { 0, 1, 7, 8, 15, 16, 255, 256, -1, -2, -8, -9, -255, -256 })
    S($"{v}: X / x / B", () => { ApInt a = v; return $"{a:X} / {a:x} / {a:B}"; });
S("-5 X8 / 5 X8 / -5 B8", () => $"{(ApInt)(-5):X8} / {(ApInt)5:X8} / {(ApInt)(-5):B8}");
S("(2^200+1) X length", () => ((ApInt.One << 200) + 1).ToString("X").Length);
S("ApInt.Parse(\"0xFF\")", () => ApInt.Parse("0xFF"));
S("ApInt.Parse(\"-0xFF\")", () => ApInt.Parse("-0xFF"));
S("ApInt.Parse(\"FF\", HexNumber)", () => ApInt.Parse("FF", NumberStyles.HexNumber, null));
S("ApInt.Parse(\"0FF\", HexNumber)", () => ApInt.Parse("0FF", NumberStyles.HexNumber, null));
S("ApInt.Parse(\"-FF\", HexNumber)", () => ApInt.Parse("-FF", NumberStyles.HexNumber, null));
S("round trip: Parse(\"0x\" + (-255 X))", () => ApInt.Parse("0x" + ((ApInt)(-255)).ToString("X")));
S("round trip: Parse(-255 X, HexNumber)", () => ApInt.Parse(((ApInt)(-255)).ToString("X"), NumberStyles.HexNumber, null));
S("Parse(\"11111111\", BinaryNumber)", () => ApInt.Parse("11111111", NumberStyles.BinaryNumber, null));
S("ApInt.Parse(\"0o17\")", () => ApInt.Parse("0o17"));
S("ApInt ToString(\"O\")", () => ((ApInt)8).ToString("O"));
S("ApInt ToString(\"N0\")", () => ((ApInt)1234567).ToString("N0"));
S("ApInt ToString(\"G5\")", () => ((ApInt)1234567).ToString("G5"));
S("ApInt ToString(\"D10\"), -5", () => ((ApInt)(-5)).ToString("D10"));
S("ApInt ToString(\"#,##0\")", () => ((ApInt)1234567).ToString("#,##0"));
// the plain-digit routes on the public API
static string PlainHex(ApInt x) { string h = ApInt.Abs(x).ToString("X").TrimStart('0'); return (x.IsNegative ? "-" : "") + (h.Length == 0 ? "0" : h); }
static string PlainBin(ApInt x) { string h = ApInt.Abs(x).ToString("B").TrimStart('0'); return (x.IsNegative ? "-" : "") + (h.Length == 0 ? "0" : h); }
S("PlainHex 0,255,-255,2^64,-(2^64)", () => string.Join(" ", new ApInt[] { 0, 255, -255, ApInt.One << 64, -(ApInt.One << 64) }.Select(PlainHex)));
S("PlainBin 0,255,-255", () => string.Join(" ", new ApInt[] { 0, 255, -255 }.Select(PlainBin)));
S("((ApFloat)ApInt 255).ToString(\"B\")   [public, plain]", () => ((ApFloat)(ApInt)255).ToString("B"));
S("((ApFloat)ApInt -255).ToString(\"B\")  [public, plain]", () => ((ApFloat)(ApInt)(-255)).ToString("B"));
S("((ApFloat)ApInt 0).ToString(\"B\")", () => ((ApFloat)(ApInt)0).ToString("B"));
S("((ApFloat)ApInt 255).ToString(\"X\")   [hexfloat, not hex]", () => ((ApFloat)(ApInt)255).ToString("X"));
S("((ApFloat)ApInt 255).ToString(\"A\")", () => ((ApFloat)(ApInt)255).ToString("A"));
S("((ApFloat)5.25).ToString(\"B\")", () => ((ApFloat)5.25).ToString("B"));
S("((ApFloat)0.1).ToString(\"B8\")", () => ((ApFloat)0.1).ToString("B8"));
S("((ApFloat)-0.0).ToString(\"B\") / \"A\"", () => $"{(ApFloat)(-0.0):B} / {(ApFloat)(-0.0):A}");

// ------------------------------------------------------------------
H("C. BigInteger word quirks against a fixed-width programmer mode");
S("PopCount(-1)              [64-bit mode wants 64]", () => ApInt.PopCount(-1));
S("PopCount(-1L<<40)", () => ApInt.PopCount((ApInt)(-1) << 40));
S("PopCount(-(2^100))", () => ApInt.PopCount(-(ApInt.One << 100)));
S("PopCount(255 & -1)", () => ApInt.PopCount((ApInt)255 & -1));
S("RotateLeft(1, 40)         [64-bit mode wants 2^40]", () => ApInt.RotateLeft(1, 40));
S("RotateLeft(0x81, 1)       [8-bit mode wants 3]", () => ApInt.RotateLeft(0x81, 1));
S("RotateRight(1, 1)         [8-bit mode wants 128]", () => ApInt.RotateRight(1, 1));
S("RotateRight(1, 1) as uint", () => BitOperations.RotateRight(1u, 1));
S("RotateLeft(2^31, 1)       [width: value-dependent]", () => ApInt.RotateLeft(ApInt.One << 31, 1));
S("RotateLeft(2^32, 1)", () => ApInt.RotateLeft(ApInt.One << 32, 1));
S("RotateLeft(2^63, 1)", () => ApInt.RotateLeft(ApInt.One << 63, 1));
S("RotateLeft(-1, 5)", () => ApInt.RotateLeft(-1, 5));
S("RotateLeft(-2, 1)", () => ApInt.RotateLeft(-2, 1));
S("RotateLeft(0, 5)", () => ApInt.RotateLeft(0, 5));
S("LeadingZeroCount(1)       [64-bit mode wants 63]", () => ApInt.LeadingZeroCount(1));
S("LeadingZeroCount(2^40)", () => ApInt.LeadingZeroCount(ApInt.One << 40));
S("LeadingZeroCount(0) / (-1)", () => $"{ApInt.LeadingZeroCount(0)} / {ApInt.LeadingZeroCount(-1)}");
S("TrailingZeroCount(0)      [64-bit mode wants 64]", () => ApInt.TrailingZeroCount(0));
S("Log2(0)", () => ApInt.Log2(0));
S("Log2(-8)", () => ApInt.Log2(-8));
S("-16 >>> 2", () => (ApInt)(-16) >>> 2);
S("-16 >> 2", () => (ApInt)(-16) >> 2);
S("-(2^40) >>> 32", () => -(ApInt.One << 40) >>> 32);
S("-1 >>> 64", () => (ApInt)(-1) >>> 64);
// The masked recipes, checked against the hardware at 8, 16, 32, 64 bits.
static ApInt Mask(int w) => (ApInt.One << w) - 1;
static ApInt Rotl(ApInt x, int r, int w) { r = ((r % w) + w) % w; x &= Mask(w); return r == 0 ? x : ((x << r) | (x >> (w - r))) & Mask(w); }
static ApInt ToSigned(ApInt x, int w) { x &= Mask(w); return x >= (ApInt.One << (w - 1)) ? x - (ApInt.One << w) : x; }
{
    var rng = new Random(12345);
    int bad = 0, checks = 0;
    for (int i = 0; i < 20000; i++)
    {
        ulong u = (ulong)rng.NextInt64() ^ ((ulong)rng.NextInt64() << 1);
        if (i % 4 == 0) u = ~0UL << rng.Next(64);
        int r = rng.Next(-70, 70);
        ApInt a = u;
        checks += 7;
        if ((ulong)Rotl(a, r, 64) != BitOperations.RotateLeft(u, r)) bad++;
        if ((uint)(ulong)Rotl((uint)u, r, 32) != BitOperations.RotateLeft((uint)u, r)) bad++;
        if ((byte)(ulong)Rotl((uint)(byte)u, r, 8) != byte.RotateLeft((byte)u, r)) bad++;
        if ((ushort)(ulong)Rotl((uint)(ushort)u, r, 16) != ushort.RotateLeft((ushort)u, r)) bad++;
        if ((long)ToSigned(a, 64) != (long)u) bad++;
        if ((int)(long)ToSigned(a, 8) != (sbyte)(byte)u) bad++;
        if ((long)ApInt.PopCount(ToSigned(a, 64) & Mask(64)) != BitOperations.PopCount(u)) bad++;
    }
    Console.WriteLine($"masked rotate/signed/popcount recipes vs hardware: {bad} wrong of {checks}");
}
// How many of 8 widths' rotations the built-in gets right.
{
    int wrong = 0, total = 0;
    for (int r = 1; r < 64; r++) { total++; if (ApInt.RotateLeft(1, r) != (ApInt)BitOperations.RotateLeft(1UL, r)) wrong++; }
    Console.WriteLine($"ApInt.RotateLeft(1, r) vs 64-bit rotate, r=1..63: {wrong} wrong of {total}");
}

// ------------------------------------------------------------------
H("D. conversions");
S("(int)(ApInt)5000000000    [explicit long, then C# cast]", () => (int)(ApInt)5000000000L);
S("int.CreateChecked((ApInt)5000000000)", () => int.CreateChecked((ApInt)5000000000L));
S("ApInt.Pow(2, (int)(ApInt)(2^32 + 3))", () => ApInt.Pow(2, (int)((ApInt.One << 32) + 3)));
S("(long)(2^63)", () => (long)(ApInt.One << 63));
S("(ulong)(-1)", () => (ulong)(ApInt)(-1));
S("(int)(ApFloat)3e10", () => (int)(ApFloat)3e10);
S("(ApInt)(ApFloat)-2.7", () => (ApInt)(ApFloat)(-2.7));
S("(ApInt)NaN", () => (ApInt)ApFloat.NaN);
S("(ApInt)∞", () => (ApInt)ApFloat.PositiveInfinity);
S("(long)NaN", () => (long)ApFloat.NaN);
S("(long)1e30", () => (long)(ApFloat)1e30);
S("long.CreateSaturating(ApFloat 1e30)", () => long.CreateSaturating((ApFloat)1e30));
S("ApInt.CreateChecked(1e300) BitLength", () => ApInt.CreateChecked(1e300).BitLength);
S("ApInt.CreateChecked(double.NaN)", () => ApInt.CreateChecked(double.NaN));
S("ApInt.CreateSaturating(double.NaN)", () => ApInt.CreateSaturating(double.NaN));
S("ApInt.CreateSaturating(double.PositiveInfinity)", () => ApInt.CreateSaturating(double.PositiveInfinity));
S("(double)(ApFloat)(2^2000)", () => (double)new ApFloat(1, 2000));
S("(double)ApFloat 1e-400 parse", () => (double)ApFloat.Parse("1e-400"));
S("NaN.Significand", () => ApFloat.NaN.Significand);
S("∞.Exponent", () => ApFloat.PositiveInfinity.Exponent);
S("0.Significand / Exponent", () => $"{ApFloat.Zero.Significand} / {ApFloat.Zero.Exponent}");
S("5.25: Significand / Exponent", () => $"{((ApFloat)5.25).Significand} / {((ApFloat)5.25).Exponent}");

// ------------------------------------------------------------------
H("E. arithmetic edge cases and every exception seen");
S("ApInt 1/0", () => (ApInt)1 / 0);
S("ApInt 1%0", () => (ApInt)1 % 0);
S("ApInt.DivRem(1,0)", () => ApInt.DivRem(1, 0));
S("ApInt.ModPow(2,3,0)", () => ApInt.ModPow(2, 3, 0));
S("ApInt.ModPow(2,-1,5)", () => ApInt.ModPow(2, -1, 5));
S("ApInt.ModPow(-2,3,5)", () => ApInt.ModPow(-2, 3, 5));
S("ApInt.ModPow(2,3,-5)", () => ApInt.ModPow(2, 3, -5));
S("ApInt.Pow(2,-1)", () => ApInt.Pow(2, -1));
S("ApInt.Pow(0,0)", () => ApInt.Pow(0, 0));
S("ApInt.Sqrt(-1)", () => ApInt.Sqrt(-1));
S("ApInt.GreatestCommonDivisor(0,0)", () => ApInt.GreatestCommonDivisor(0, 0));
S("ApInt.Clamp(5, 9, 1)", () => ApInt.Clamp(5, 9, 1));
S("ApInt -7 / 2, -7 % 2, -7 >> 1", () => $"{(ApInt)(-7) / 2}, {(ApInt)(-7) % 2}, {(ApInt)(-7) >> 1}");
S("ApInt.Parse(null!)", () => ApInt.Parse((string)null!));
S("ApInt.Parse(null!, style, null)", () => ApInt.Parse((string)null!, NumberStyles.Integer, null));
S("ApInt.CompareTo(\"x\")", () => ((ApInt)1).CompareTo((object)"x"));
S("ApFloat 1/0, -1/0, 0/0", () => $"{(ApFloat)1 / 0:R}, {(ApFloat)(-1) / 0:R}, {(ApFloat)0 / 0:R}");
S("ApFloat 1%0", () => ((ApFloat)1 % 0).ToString("R"));
S("ApFloat ∞%2", () => (ApFloat.PositiveInfinity % 2).ToString("R"));
S("ApFloat Ieee754Remainder(1,0)", () => ApFloat.Ieee754Remainder(1, 0).ToString("R"));
S("ApFloat.Sqrt(-1)", () => ApFloat.Sqrt(-1).ToString("R"));
S("ApFloat ∞-∞, 0*∞", () => $"{ApFloat.PositiveInfinity - ApFloat.PositiveInfinity:R}, {ApFloat.Zero * ApFloat.PositiveInfinity:R}");
S("ApFloat.Sign(NaN)", () => ApFloat.Sign(ApFloat.NaN));
S("ApFloat.Sign(-0)", () => ApFloat.Sign(ApFloat.NegativeZero));
S("ApFloat.Clamp(5, 9, 1)", () => ApFloat.Clamp(5, 9, 1));
S("ApFloat.CompareTo(\"x\")", () => ((ApFloat)1).CompareTo((object)"x"));
S("NaN == NaN, NaN.Equals(NaN), NaN<1, NaN.CompareTo(1)", () => $"{ApFloat.NaN == ApFloat.NaN} {ApFloat.NaN.Equals(ApFloat.NaN)} {ApFloat.NaN < 1} {ApFloat.NaN.CompareTo(1)}");
S("+0 == -0, Equals", () => $"{ApFloat.Zero == ApFloat.NegativeZero} {ApFloat.Zero.Equals(ApFloat.NegativeZero)}");
S("0 * -1 prints", () => ((ApFloat)0 * -1).ToString("R"));
S("Ceiling(-0.5) prints", () => ApFloat.Ceiling(-0.5).ToString("R"));
S("-0 F2 / E2 / G / G3", () => { var z = ApFloat.NegativeZero; return $"{z:F2} / {z:E2} / {z:G} / {z:G3}"; });
S("new ApFloat(1, 0, 0)", () => new ApFloat(1, 0, 0));
S("Parse(\"1\", 0)", () => ApFloat.Parse("1", 0));
S("Parse(\"1\", MaxPrecision + 1)", () => ApFloat.Parse("1", ApFloat.MaxPrecision + 1));
S("TryParse(\"1\", -1, ...)  [a Try that throws]", () => ApFloat.TryParse("1", -1, RoundingMode.ToNearestEven, null, out _));
S("WithPrecision(0)", () => ((ApFloat)1).WithPrecision(0));
S("DecimalDigitsFor(0)", () => ApFloat.DecimalDigitsFor(0));
S("Add(1, 1, 0)", () => ApFloat.Add(1, 1, 0));
S("Add with bad RoundingMode, inexact", () => ApFloat.Divide(1, 3, 53, (RoundingMode)99).ToString("R"));
S("Add with bad RoundingMode, exact", () => ApFloat.Add(1, 1, 53, (RoundingMode)99).ToString("R"));
S("new IeeeContext(53).Mode = 99", () => { var c = new IeeeContext(53); c.Mode = (RoundingMode)99; return c; });
S("new IeeeFormat(1, 53)", () => new IeeeFormat(1, 53));
S("new IeeeFormat(11, 1)", () => new IeeeFormat(11, 1));
S("IeeeFormat.Binary(100)", () => IeeeFormat.Binary(100));
S("IeeeFormat.Binary(480768) / Binary(480800)", () => { var a = IeeeFormat.Binary(480768); return $"{a.ExponentBits},{a.Precision}: then " + IeeeFormat.Binary(480800); });
S("Add(1,1, default(IeeeFormat))", () => ApFloat.Add(1, 1, default(IeeeFormat)));
S("ToIeeeBytes(new IeeeFormat(8,8+1)) (not whole bytes)", () => ((ApFloat)1).ToIeeeBytes(new IeeeFormat(8, 9)));
S("FromIeeeBytes wrong length", () => ApFloat.FromIeeeBytes(new byte[3], IeeeFormat.Binary32));
S("ToIeeeBytes 1.5 binary32", () => Convert.ToHexString(((ApFloat)1.5).ToIeeeBytes(IeeeFormat.Binary32)));
S("ToIeeeBytes 1.5 binary32 big-endian", () => Convert.ToHexString(((ApFloat)1.5).ToIeeeBytes(IeeeFormat.Binary32, RoundingMode.ToNearestEven, true)));
S("ToIeeeBytes π binary128 round trip", () => ApFloat.FromIeeeBytes(ApFloat.Parse("3.14159").ToIeeeBytes(IeeeFormat.Binary128), IeeeFormat.Binary128).ToString("R"));
S("ApInt ToMagnitudeBytes(-258)", () => Convert.ToHexString(((ApInt)(-258)).ToMagnitudeBytes()));
S("ApInt FromMagnitude(0102, negative)", () => ApInt.FromMagnitude(new byte[] { 2, 1 }, true));

// ------------------------------------------------------------------
H("F. formats");
ApFloat q = ApFloat.Parse("-1234567.891");
foreach (string f in new[] { "", "E", "E3", "e3", "F", "F0", "F2", "G", "G3", "G10", "g3", "R", "B4", "A", "a3", "X", "x", "#,##0.00", "0000000000.000", "#", "'x = '0.###", "#,##0.###'m'" })
    S($"-1234567.891 \"{f}\"", () => q.ToString(f));
foreach (string f in new[] { "N", "N2", "C", "P", "D", "D5", "Z", "0.0%", "0;0", "0.00E+00", "0.0.0", "'open", "E1234567890", "F12345678901" })
    S($"bad? \"{f}\"", () => ((ApFloat)1.5).ToString(f));
S("0.125 F2 (ties to even; .NET says 0.13)", () => $"{(ApFloat)0.125:F2} vs {0.125.ToString("F2", inv)}");
S("2.5 F0 / 3.5 F0", () => $"{(ApFloat)2.5:F0} / {(ApFloat)3.5:F0}");
S("2.5 F0 mode TowardPositive", () => ((ApFloat)2.5).ToString("F0", inv, RoundingMode.TowardPositive));
S("1e21 G / 1e-6 G (at 256)", () => $"{ApFloat.Parse("1e21"):G} / {ApFloat.Parse("1e-6"):G}");
S("1e79 G / 1e78 G", () => $"{ApFloat.Parse("1e79"):G} / {ApFloat.Parse("1e78"):G}");
S("1/3 G at 256 length", () => ((ApFloat)1 / 3).ToString("G").Length);
S("1/3 default at 256 length", () => ((ApFloat)1 / 3).ToString().Length);
S("NaN \"F2\", ∞ \"E2\", -∞ \"#,##0\", NaN \"Z\"", () => $"{ApFloat.NaN:F2}, {ApFloat.PositiveInfinity:E2}, {ApFloat.NegativeInfinity:#,##0}, {ApFloat.NaN:Z}");
S("TryFormat small buffer", () => ((ApFloat)1.5).TryFormat(new char[1], out _, "R", null));
foreach (string c in new[] { "en-US", "en-GB", "de-DE", "fr-FR", "sv-SE", "en-IN", "ar-SA", "hi-IN" })
{
    var ci = CultureInfo.GetCultureInfo(c);
    S($"{c}: 1234567.5 \"#,##0.0\"", () => ((ApFloat)1234567.5).ToString("#,##0.0", ci));
    S($"{c}: .NET's own 1234567.5 \"#,##0.0\"", () => 1234567.5.ToString("#,##0.0", ci));
    S($"{c}: -1.5 R / ∞ / NaN", () => $"{((ApFloat)(-1.5)).ToString("R", ci)} / {ApFloat.PositiveInfinity.ToString("R", ci)} / {ApFloat.NaN.ToString("R", ci)}");
}

// ------------------------------------------------------------------
H("G. parsing");
foreach (string t in new[] { "", " ", ".", "-", "+", "e5", "1e", "1e+", "--1", "+-1", "1__0", "_1", "1_", "1_000.25", "1.2.3", "1 2", "  1.5  ", "\t1.5\n", ".5", "5.", "1E5", "0x", "0x1p", "0x1.8", "0x1.8p1", "0X1P3", "0b", "0b101", "0b102", "0b1.1p-1", "0x_1", "1,000", "1,5", "١٢٣", "½", "∞", "-∞", "+∞", "inf", "INF", "Infinity", "-Infinity", "NaN", "nan", "-NaN", "+NaN", "\u22125", "1e99999999999999999999", "1e-99999999999999999999", "0e99999999999999999999", "1e2776511644261678566", "1e2776511644261678567", "1e-2776511644261678566", "1e-2776511644261678567", "1f", "1m", "0.1d" })
{
    string shown = t.Replace("\t", "\\t").Replace("\n", "\\n");
    S($"ApFloat.Parse(\"{shown}\", 64)", () => ApFloat.Parse(t, 64).ToString("E4"));
}
foreach (string t in new[] { "", " 12 ", "+12", "-12", "1_000", "1,000", "1e5", "1.0", "0x", "0xFF", "0XfF", "0b101", "0B1", "-0x10", "0x-10", "\u221212", "١٢٣" })
    S($"ApInt.Parse(\"{t}\")", () => ApInt.Parse(t));
S("ApInt.Parse(\"1,000\", Number, en-US)", () => ApInt.Parse("1,000", NumberStyles.Number, CultureInfo.GetCultureInfo("en-US")));
S("ApInt.Parse(\"1.000\", Number, de-DE)", () => ApInt.Parse("1.000", NumberStyles.Number, CultureInfo.GetCultureInfo("de-DE")));
S("ApInt.Parse(\"1.5\", Number, inv)", () => ApInt.Parse("1.5", NumberStyles.Number, inv));
S("ApInt.Parse(\"1e3\", Float, inv)", () => ApInt.Parse("1e3", NumberStyles.Float, inv));
S("ApInt.Parse(\"1e1000000000\", Float, inv)", () => ApInt.Parse("1e1000000000", NumberStyles.Float, inv));
S("ApInt.Parse(\"5\", AllowHexSpecifier|AllowLeadingSign)", () => ApInt.Parse("5", NumberStyles.AllowHexSpecifier | NumberStyles.AllowLeadingSign, inv));
S("ApFloat.Parse(\"1\", HexNumber, inv)", () => ApFloat.Parse("1", NumberStyles.HexNumber, inv));
S("ApFloat.TryParse(\"1\", HexNumber, inv, out _)", () => ApFloat.TryParse("1", NumberStyles.HexNumber, inv, out _));
S("ApFloat.Parse(\"1,234.5\", Number, en-US) precision", () => P(ApFloat.Parse("1,234.5", NumberStyles.Number, CultureInfo.GetCultureInfo("en-US"))));
S("ApFloat.Parse(\"1,5\", de-DE) (own syntax)", () => ApFloat.Parse("1,5", CultureInfo.GetCultureInfo("de-DE")).ToString("R", inv));
S("ApFloat.Parse(\"1.5\", de-DE) (own syntax)", () => ApFloat.Parse("1.5", CultureInfo.GetCultureInfo("de-DE")).ToString("R", inv));
S("ApFloat.Parse(\"1.234,5\", Number, de-DE)", () => ApFloat.Parse("1.234,5", NumberStyles.Number, CultureInfo.GetCultureInfo("de-DE")).ToString("R", inv));
S("ApFloat.Parse(\"1 234,5\", Number, fr-FR) (plain space)", () => ApFloat.Parse("1 234,5", NumberStyles.Number, CultureInfo.GetCultureInfo("fr-FR")).ToString("R", inv));
S("ApFloat.Parse(\"−5\", en-US own syntax, U+2212)", () => ApFloat.Parse("\u22125", CultureInfo.GetCultureInfo("en-US")).ToString("R", inv));
S("ApFloat.Parse(\"−5\", sv-SE own syntax, U+2212)", () => ApFloat.Parse("\u22125", CultureInfo.GetCultureInfo("sv-SE")).ToString("R", inv));
S("ApFloat.Parse(\"-5\", sv-SE own syntax, hyphen)", () => ApFloat.Parse("-5", CultureInfo.GetCultureInfo("sv-SE")).ToString("R", inv));
S("with thread culture de-DE: ApFloat.Parse(\"1.5\")", () =>
{
    var old = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    try { return ApFloat.Parse("1.5").ToString("R", inv); } finally { CultureInfo.CurrentCulture = old; }
});
S("with thread culture de-DE: (1.5).ToString(\"R\") round trip", () =>
{
    var old = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    try { string t = ((ApFloat)1.5).ToString("R"); return t + " then en-style parse: " + ApFloat.Parse(t, inv).ToString("R", inv); } finally { CultureInfo.CurrentCulture = old; }
});
S("IeeeContext(64).Parse(\"1e400\")", () => new IeeeContext(64).Parse("1e400").ToString("E2"));
S("IeeeContext(Binary64).Parse(\"1e400\")", () => { var c = new IeeeContext(IeeeFormat.Binary64); var v = c.Parse("1e400"); return $"{v:R} {c.Flags}"; });
S("ctx.Parse failed text leaves flags", () => { var c = new IeeeContext(64); try { c.Parse("x"); } catch { } return c.Flags; });

// ------------------------------------------------------------------
H("H. flags and rounding");
{
    var c = new IeeeContext(256);
    S("256: 1/3 flags", () => { c.ClearFlags(); c.Divide(1, 3); return c.Flags; });
    S("256: 1/0 flags", () => { c.ClearFlags(); return $"{c.Divide(1, 0):R} {c.Flags}"; });
    S("256: 0/0 flags", () => { c.ClearFlags(); return $"{c.Divide(0, 0):R} {c.Flags}"; });
    S("256: sqrt(-1) flags", () => { c.ClearFlags(); return $"{c.Sqrt(-1):R} {c.Flags}"; });
    S("256: 2^(10^12) squared: no overflow flag", () => { c.ClearFlags(); var v = new ApFloat(1, 1000000000000); return $"{c.Multiply(v, v):E2} {c.Flags}"; });
    S("operators raise nothing: c untouched by 1/(ApFloat)3", () => { c.ClearFlags(); var _ = (ApFloat)1 / 3; return c.Flags; });
    var f = new IeeeContext(IeeeFormat.Binary64);
    S("b64: 1e300*1e300", () => { f.ClearFlags(); return $"{f.Multiply(1e300, 1e300):R} {f.Flags}"; });
    S("b64: 1e-300*1e-300", () => { f.ClearFlags(); return $"{f.Multiply(1e-300, 1e-300):R} {f.Flags}"; });
    S("b64 TowardZero: 1e300*1e300", () => { f.ClearFlags(); f.Mode = RoundingMode.TowardZero; var r = f.Multiply(1e300, 1e300); f.Mode = RoundingMode.ToNearestEven; return $"{r:R} {f.Flags}"; });
    S("mode through context: 1/3 up vs down at 8 bits", () => $"{new IeeeContext(8, RoundingMode.TowardPositive).Divide(1, 3):R} {new IeeeContext(8, RoundingMode.TowardNegative).Divide(1, 3):R}");
    S("Round midpoints 2.5: ToEven AwayFromZero ToZero ToNegInf ToPosInf", () => string.Join(" ", new[] { MidpointRounding.ToEven, MidpointRounding.AwayFromZero, MidpointRounding.ToZero, MidpointRounding.ToNegativeInfinity, MidpointRounding.ToPositiveInfinity }.Select(m => ApFloat.Round(2.5, m).ToString("R"))));
    S("Round(-0.4), Floor(-0.0), Round(2^-1000)", () => $"{ApFloat.Round(-0.4):R}, {ApFloat.Floor(-0.0):R}, {ApFloat.Round(new ApFloat(1, -1000)):R}");
    S("Truncate(1e30)", () => ApFloat.Truncate(1e30).ToString("R"));
    S("x % 1 fraction of -7.25", () => ((ApFloat)(-7.25) % 1).ToString("R"));
}
