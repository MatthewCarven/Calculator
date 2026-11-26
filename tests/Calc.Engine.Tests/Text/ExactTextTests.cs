using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Calc.Engine.Eval;
using Calc.Engine.Tests.Eval;
using Calc.Engine.Text;
using Calc.Engine.Values;
using Natural;
using static Calc.Engine.Tests.Eval.ExactHelpers;

namespace Calc.Engine.Tests.Text;

/// <summary>B8's display of exact values: the chips, the captions, and a fraction's digits worked out exactly.</summary>
public class ExactTextTests
{
    private static ResultText Text(string source, int bits = 256, Notation notation = Notation.Auto) =>
        ResultText.Of(Evaluator.Run(source, Target.Bits(bits), RoundingMode.ToNearestEven, exact: true).Value, Target.Bits(bits), new TextOptions(notation));

    [Theory]
    [InlineData("0.1+0.2", 53, "exact = 3/10", "exact = 3/10", "0.3")]
    [InlineData("-1/3", 53, "exact = -1/3", "exact = -1/3", "-0.333333333333333")]
    [InlineData("2/3", 53, "exact = 2/3", "exact = 2/3", "0.666666666666667")]
    [InlineData("22/7", 53, "exact = 22/7", "exact = 22/7", "3.14285714285714")]
    [InlineData("7", 53, "exact", "exact, 1 digit", "7")]
    [InlineData("2^64", 53, "exact", "exact, 20 digits", "18446744073709551616")]
    [InlineData("123456789012345/987654321098765", 53, "exact", "exact, 15 digits shown", "0.124999998860937")]
    [InlineData("1/2^60", 256, "exact", "exact", "8.67361737988403547205962240695953369140625E-019")]
    [InlineData("1e-300/3", 53, "exact", "exact, 15 digits shown", "3.33333333333333E-301")]
    [InlineData("sqrt(2)", 53, "float", "float, 15 digits", "1.4142135623731")]
    [InlineData("3^100000", 256, "float (exact too large)", "float (exact too large), 76 digits", null)]
    public void ChipsAndCaptions(string source, int bits, string kind, string caption, string? main)
    {
        var text = Text(source, bits);
        Assert.Equal(kind, text.Kind);
        Assert.Equal(caption, text.Caption);
        if (main is not null) Assert.Equal(main, text.Main);
    }

    [Fact]
    public void AnIntegerTooLongToShowSaysAboutHowManyDigits()
    {
        var text = Text("2^100000");
        Assert.Equal("exact", text.Kind);
        Assert.Equal("exact, about 30,103 digits", text.Caption);
        Assert.Equal(30_103, text.Digits);
        Assert.False(text.Complete);
        Assert.StartsWith("9.99002093014384", text.Main);
        var whole = Text("2^1000");
        Assert.Equal("exact, 302 digits", whole.Caption);
        Assert.True(whole.Complete);
    }

    [Fact]
    public void AFormatTargetsCaptionCountsItsDigits()
    {
        var value = Evaluator.Run("0.1+0.2", Target.Of(IeeeFormat.Binary64), RoundingMode.ToNearestEven, exact: true).Value;
        Assert.Equal("float, 15 digits", ResultText.Of(value, Target.Of(IeeeFormat.Binary64)).Caption);
        var nan = Evaluator.Run("0/0", Target.Of(IeeeFormat.Binary64), RoundingMode.ToNearestEven).Value;
        Assert.Equal("float", ResultText.Of(nan, Target.Of(IeeeFormat.Binary64)).Caption);
    }

    [Fact]
    public void AFractionsDigitsAreItsDecimalRoundedOnce()
    {
        // Every digit shown is n/d rounded to nearest, ties to even, by BigInteger, at precisions on both sides of the
        // 1,300-digit limit. From a float of the fraction instead, about 2% of last digits would be off.
        var random = new Random(91);
        int[] precisions = [2, 24, 53, 64, 113, 256, 1000, 4096, 4400, 20000];
        for (int i = 0; i < 3_000; i++)
        {
            int p = precisions[random.Next(precisions.Length)];
            Rational x = RandomFraction(random, random.Next(1, 200));
            if (x.Den.IsOne) continue;
            var text = ResultText.Of(ExactHelpers.Exact(x), Target.Bits(p), new TextOptions(Notation.Aligned));
            int digits = Protocol.Digits.For(p);
            int shown = digits <= ResultText.MainLimit ? digits : ResultText.PreviewDigits;
            Assert.True(text.Main == Scientific(x, shown), $"{x} at {p} bits: {text.Main}, expected {Scientific(x, shown)}");
            Assert.Equal(digits <= ResultText.MainLimit, text.Complete);
        }
    }

    /// <summary>|x| to <paramref name="count"/> digits in E layout, rounded half to even, all by BigInteger.</summary>
    private static string Scientific(Rational x, int count)
    {
        BigInteger a = BigInteger.Abs(x.Num), b = x.Den;
        int e = (int)Math.Floor(BigInteger.Log10(a) - BigInteger.Log10(b));
        while (Compare(new Rational(a, b), Pow10(e)) < 0) e--;
        while (Compare(new Rational(a, b), Pow10(e + 1)) >= 0) e++;
        Rational scaled = new Rational(a, b) * Pow10(count - 1 - e);
        BigInteger q = BigInteger.DivRem(scaled.Num, scaled.Den, out var r);
        int half = (2 * r).CompareTo(scaled.Den);
        if (half > 0 || (half == 0 && !q.IsEven)) q++;
        if (q == BigInteger.Pow(10, count))
        {
            q /= 10;
            e++;
        }
        string digits = q.ToString(CultureInfo.InvariantCulture);
        string body = digits.Length > 1 ? digits[..1] + "." + digits[1..] : digits;
        return (x.Num.Sign < 0 ? "-" : "") + body + "E" + (e < 0 ? "-" : "+") + Math.Abs(e).ToString("000", CultureInfo.InvariantCulture);
    }

    private static Rational Pow10(int e) => e >= 0 ? new Rational(BigInteger.Pow(10, e), 1) : new Rational(1, BigInteger.Pow(10, -e));

    [Fact]
    public void AFractionOverAPowerOfTwoReadsAsTheSameFloatWould()
    {
        // m/2^k at a precision that holds it exactly is the same number as the float, so every notation must give the
        // same text: the fraction's layout, done here, against Natural's own G and E.
        var random = new Random(92);
        for (int i = 0; i < 2_000; i++)
        {
            int p = random.Next(2, 1024);
            BigInteger m = RandomBig(random, random.Next(1, p + 1)) | 1;
            if (random.Next(2) == 0) m = -m;
            int k = random.Next(1, 400);
            Value ratio = RatioValue.Of(Ap(m), ApInt.One << k);
            var number = new FloatValue(new ApFloat(Ap(m), -k, p));
            foreach (Notation notation in Enum.GetValues<Notation>())
            {
                var options = new TextOptions(notation, random.Next(3) == 0 ? "," : null);
                ResultText a = ResultText.Of(ratio, Target.Bits(p), options), b = ResultText.Of(number, Target.Bits(p), options);
                string what = $"{m}/2^{k} at {p} bits, {notation}";
                Assert.True(a.Main == b.Main, $"{what}: fraction {a.Main}, float {b.Main}");
                Assert.True(a.Short == b.Short, $"{what}: fraction {a.Short}, float {b.Short}");
                Assert.Equal(b.Digits, a.Digits);
            }
        }
    }

    [Fact]
    public void PreviewsAreQuick()
    {
        // A fraction at 2^20 bits shows a 100-digit preview of its 315,652; a 16-million-bit integer, about 4.8 million
        // digits, a preview too. Both are the exact values' digits, and both are quick.
        Value fraction = Evaluator.Run("1/3^10000", Target.Bits(1 << 20), RoundingMode.ToNearestEven, exact: true).Value;
        Value integer = Evaluator.Run("2^16000000 - 1", Target.Bits(256), RoundingMode.ToNearestEven, exact: true).Value;
        Assert.IsType<RatioValue>(fraction);
        Assert.IsType<IntValue>(integer);
        var clock = Stopwatch.StartNew();
        var a = ResultText.Of(fraction, Target.Bits(1 << 20));
        long fractionMs = clock.ElapsedMilliseconds;
        clock.Restart();
        var b = ResultText.Of(integer, Target.Bits(256));
        long integerMs = clock.ElapsedMilliseconds;
        Assert.False(a.Complete);
        Assert.Equal(315_652, a.Digits);
        Assert.Equal("exact, about 4,816,480 digits", b.Caption);
        Assert.True(fractionMs < 200, $"fraction: {fractionMs} ms");
        Assert.True(integerMs < 1000, $"integer: {integerMs} ms");
    }

    [Fact]
    public void AFractionGroupsLikeAnyNumber()
    {
        Assert.Equal("1,234.33333333333", ResultText.Of(Evaluator.Run("3703/3", Target.Bits(53), RoundingMode.ToNearestEven, exact: true).Value,
            Target.Bits(53), new TextOptions(Notation.Auto, ",")).Main);
    }
}
