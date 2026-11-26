using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Calc.Engine.Eval;
using Calc.Engine.Tests.Eval;
using Calc.Engine.Text;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Tests.Text;

public class ResultTextTests
{
    private static readonly Target Bits53 = Target.Bits(53);
    private static readonly Target Bits256 = Target.Bits(256);
    private static readonly Target Binary64 = Target.Of(IeeeFormat.Binary64);

    private static ResultText Text(string source, Target target, Notation notation = Notation.Auto, string? separator = null, bool groupFraction = false) =>
        ResultText.Of(Evaluator.Run(source, target, RoundingMode.ToNearestEven).Value, target, new TextOptions(notation, separator, groupFraction));

    private static readonly string Third76 = new('3', 76);

    [Theory]
    [InlineData("1/3", Notation.Auto, "0.")]
    [InlineData("1/3", Notation.Fixed, "0.")]
    public void OneThirdAt256BitsShowsAll76Digits(string source, Notation notation, string prefix) =>
        Assert.Equal(prefix + Third76, Text(source, Bits256, notation).Main);

    [Fact]
    public void OneThirdAt256BitsInTheOtherNotations()
    {
        Assert.Equal("3." + new string('3', 75) + "E-001", Text("1/3", Bits256, Notation.Scientific).Main);
        Assert.Equal("333." + new string('3', 73) + "E-003", Text("1/3", Bits256, Notation.Engineering).Main);
        Assert.Equal("3." + new string('3', 75) + "E-001", Text("1/3", Bits256, Notation.Aligned).Main);
    }

    [Theory]
    // Auto is Natural's G: positional for exponents from -5 up to the digit count.
    [InlineData("123456.789", Notation.Auto, "123456.789")]
    [InlineData("1e100", Notation.Auto, "1E+100")]
    [InlineData("1e-7", Notation.Auto, "1E-007")]
    [InlineData("-2/3", Notation.Auto, "-0.666666666666667")]
    [InlineData("0.1+0.2", Notation.Auto, "0.3")]
    // Scientific drops trailing zeros; Aligned keeps all 15 guaranteed digits.
    [InlineData("1.5", Notation.Scientific, "1.5E+000")]
    [InlineData("1.5", Notation.Aligned, "1.50000000000000E+000")]
    [InlineData("-123456.789", Notation.Scientific, "-1.23456789E+005")]
    [InlineData("0", Notation.Scientific, "0E+000")]
    // Engineering: the exponent a multiple of 3, below too.
    [InlineData("123456.789", Notation.Engineering, "123.456789E+003")]
    [InlineData("12345", Notation.Engineering, "12.345E+003")]
    [InlineData("1e100", Notation.Engineering, "10E+099")]
    [InlineData("0.012", Notation.Engineering, "12E-003")]
    [InlineData("-2/3", Notation.Engineering, "-666.666666666667E-003")]
    [InlineData("1e-7", Notation.Engineering, "100E-009")]
    // Fixed: positional, padding a long whole part with zeros.
    [InlineData("1e-7", Notation.Fixed, "0.0000001")]
    [InlineData("-0.00012345", Notation.Fixed, "-0.00012345")]
    [InlineData("123456.789", Notation.Fixed, "123456.789")]
    [InlineData("2^70", Notation.Fixed, "1180591620717410000000")]
    public void EachNotation(string source, Notation notation, string expected) =>
        Assert.Equal(expected, Text(source, Bits53, notation).Main);

    [Fact]
    public void FixedFallsBackToScientificPastItsLimit()
    {
        Assert.Equal("1" + new string('0', 100), Text("1e100", Bits53, Notation.Fixed).Main);
        Assert.Equal("1E+2000", Text("1e2000", Bits53, Notation.Fixed).Main);
        Assert.Equal("1E-2000", Text("1e-2000", Bits53, Notation.Fixed).Main);
    }

    [Fact]
    public void MinusZeroFoldsAtBitsAndStaysAtAFormat()
    {
        Assert.Equal("0", Text("0 * -1", Bits53).Main);
        Assert.Equal("0", Text("0 * -1", Bits53).Short);
        Assert.Equal("0E+000", Text("0 * -1", Bits53, Notation.Scientific).Main);
        Assert.Equal("-0", Text("0 * -1", Binary64).Main);
        Assert.Equal("-0E+000", Text("0 * -1", Binary64, Notation.Scientific).Main);
    }

    [Theory]
    [InlineData("1/0", "Infinity")]
    [InlineData("-1/0", "-Infinity")]
    [InlineData("0/0", "NaN")]
    public void InfinityAndNaNAreWordsAtAFormat(string source, string word)
    {
        var text = Text(source, Binary64, Notation.Engineering, ",");
        Assert.Equal(word, text.Main);
        Assert.Equal(word, text.Short);
    }

    [Theory]
    [InlineData("1234567.891", ",", false, "1,234,567.891")]
    [InlineData("-1234567.891", " ", false, "-1 234 567.891")]
    [InlineData("1/3", " ", true, "0.333 333 333 333 333")]
    [InlineData("1/3", ",", false, "0.333333333333333")]
    [InlineData("123", ",", false, "123")]
    [InlineData("1234", ",", false, "1,234")]
    public void Grouping(string source, string separator, bool groupFraction, string expected) =>
        Assert.Equal(expected, Text(source, Bits53, Notation.Auto, separator, groupFraction).Main);

    [Fact]
    public void GroupingOnlyAddsSeparators()
    {
        var random = new Random(11);
        string[] separators = [",", " ", "_", " ", "'"];
        foreach (Notation notation in Enum.GetValues<Notation>())
            for (int i = 0; i < 400; i++)
            {
                int p = random.Next(2, 600);
                string source = RandomSource(random);
                var plain = Text(source, Target.Bits(p), notation);
                string separator = separators[random.Next(separators.Length)];
                var grouped = Text(source, Target.Bits(p), notation, separator, random.Next(2) == 0);
                Assert.Equal(plain.Main, grouped.Main.Replace(separator, ""));
            }
    }

    [Fact]
    public void EngineeringExponentsAreMultiplesOfThree()
    {
        var random = new Random(12);
        for (int i = 0; i < 3_000; i++)
        {
            string main = Text(RandomSource(random), Target.Bits(random.Next(2, 300)), Notation.Engineering).Main;
            int e = main.IndexOf('E');
            long exponent = long.Parse(main[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            Assert.True(exponent % 3 == 0, $"{main}: exponent {exponent}");
            string whole = main[..e].TrimStart('-').Split('.')[0];
            Assert.True(whole.Length is >= 1 and <= 3 && (whole[0] != '0' || whole == "0"), $"{main}: whole part {whole}");
        }
    }

    private static string RandomSource(Random random) => random.Next(6) switch
    {
        0 => $"{random.Next(1, 1000)}/{random.Next(1, 1000)}",
        1 => $"-{random.Next(1, 1000)}/{random.Next(1, 1000)}",
        2 => $"{random.Next(1, 100)}e{random.Next(-300, 300)}",
        3 => $"{random.NextInt64(1, long.MaxValue)}",
        4 => $"2^{random.Next(-3000, 3000)}",
        _ => (random.NextDouble() * 1e6).ToString("R", CultureInfo.InvariantCulture),
    };

    [Fact]
    public void MainAtBitsReadsBackWithinHalfAUnitOfItsLastDigit()
    {
        var random = new Random(13);
        foreach (Notation notation in new[] { Notation.Auto, Notation.Scientific, Notation.Fixed })
            for (int i = 0; i < 500; i++)
            {
                int p = random.Next(2, 4100);
                var value = Evaluator.Run(RandomSource(random), Target.Bits(p), RoundingMode.ToNearestEven).Value;
                var text = ResultText.Of(value, Target.Bits(p), new TextOptions(notation));
                Assert.True(text.Complete);
                CheckWithinHalfAUnit(text.Main, ((FloatValue)value).Number, slack: false);
            }
    }

    [Fact]
    public void APreviewPastThe1300DigitLimitReadsBackWithinHalfAUnit()
    {
        var random = new Random(14);
        var target = Target.Bits(16384);
        foreach (Notation notation in new[] { Notation.Auto, Notation.Scientific, Notation.Engineering })
            for (int i = 0; i < 40; i++)
            {
                var value = Evaluator.Run($"{random.Next(1, 10_000)}/{random.Next(1, 10_000)}*2^{random.Next(-50, 50)}", target, RoundingMode.ToNearestEven).Value;
                var text = ResultText.Of(value, target, new TextOptions(notation));
                Assert.False(text.Complete);
                Assert.Equal(4931, text.Digits);
                CheckWithinHalfAUnit(text.Main, ((FloatValue)value).Number, slack: true);
            }
    }

    /// <summary>
    /// The text's exact value is within half a unit of its last significant digit of x. With slack, a hair more: a preview
    /// rounds to 1,024 bits first, so it can be off by 2^-1000 of x besides.
    /// </summary>
    private static void CheckWithinHalfAUnit(string text, ApFloat x, bool slack)
    {
        bool negative = text.StartsWith('-');
        string body = negative ? text[1..] : text;
        Rational shown = RationalOracle.Exact(body), exact = RationalOracle.ToRational(x);
        if (negative) shown = -shown;
        // Trailing zeros don't count: fixed notation pads a long whole part with them, and they claim nothing.
        string mantissa = body.Split('E')[0].Replace(".", "").Trim('0');
        int significant = Math.Max(1, mantissa.Length);
        if (exact.IsZero)
        {
            Assert.True(shown.IsZero, $"{text} for zero");
            return;
        }
        Rational magnitude = exact.Num.Sign < 0 ? -exact : exact;
        long e = (long)Math.Floor(BigInteger.Log10(magnitude.Num) - BigInteger.Log10(magnitude.Den));
        while (Compare(Pow10(e), magnitude) > 0) e--;
        while (Compare(Pow10(e + 1), magnitude) <= 0) e++;
        // Where the last significant digit is: the first sits at 10^e, unless rounding carried it to 10^(e+1).
        long first = Compare(Pow10(e + 1) - shown, Rational.Zero) <= 0 && shown.Num.Sign > 0 ? e + 1 : e;
        Rational half = Pow10(first - significant + 1) * new Rational(1, 2);
        if (slack) half = half + magnitude * Rational.Pow2(-1000);
        Rational diff = shown - exact;
        if (diff.Num.Sign < 0) diff = -diff;
        Assert.True(Compare(diff, half) <= 0, $"{text[..Math.Min(60, text.Length)]}…: off by more than half a unit of its last digit");
    }

    private static Rational Pow10(long e) => e >= 0 ? new Rational(BigInteger.Pow(10, (int)e), 1) : new Rational(1, BigInteger.Pow(10, (int)-e));
    private static int Compare(Rational a, Rational b) => (a.Num * b.Den).CompareTo(b.Num * a.Den);

    [Fact]
    public void ShortStaysUnderFortyCharactersAndReadsBack()
    {
        string[] sources = ["3^(2^62)", "-3^(2^62)", "2^100000", "2^-100000", "-1/3", "1/7*10^200", "123456789012345678901234567890", "-0.000000123456789"];
        foreach (string source in sources)
            foreach (int p in new[] { 7, 53, 256, 4096 })
            {
                var value = Evaluator.Run(source, Target.Bits(p), RoundingMode.ToNearestEven).Value;
                string text = ResultText.Of(value, Target.Bits(p)).Short;
                Assert.True(text.Length < 40, $"{source} at {p} bits: \"{text}\" is {text.Length} characters");
                // 3^(2^62)'s exact check would need 10^(2.2·10^18) as a BigInteger: only its length is checked.
                if (!source.Contains("2^62")) CheckWithinHalfAUnit(text, ((FloatValue)value).Number, slack: true);
            }
        string huge = Text("3^(2^62)", Bits256).Short;
        Assert.StartsWith("5.77788054846547", huge);
        Assert.EndsWith("E+2200333419485199624", huge);
    }

    [Fact]
    public void AtAFormatMainIsTheShortestTextThatReadsBack()
    {
        var random = new Random(15);
        for (int i = 0; i < 5_000; i++)
        {
            double d = BitConverter.Int64BitsToDouble(random.NextInt64());
            if (!double.IsFinite(d)) continue;
            if (i % 4 == 0) d = BitConverter.Int64BitsToDouble(random.NextInt64(1, 1L << 52)) * (random.Next(2) == 0 ? 1 : -1); // a subnormal
            string main = ResultText.Of(new FloatValue(d), Binary64).Main;
            double back = double.Parse(main, CultureInfo.InvariantCulture);
            Assert.True(BitConverter.DoubleToInt64Bits(back) == BitConverter.DoubleToInt64Bits(d), $"{d:R}: \"{main}\" reads back as {back:R}");
            Assert.True(Significant(main) <= Significant(d.ToString("R", CultureInfo.InvariantCulture)), $"{d:R}: \"{main}\" has more digits than .NET's shortest");
        }
        Assert.Equal("5E-324", ResultText.Of(new FloatValue(double.Epsilon), Binary64).Main);

        // Significant digits, whatever the shape: 0.00001, 1E-005 and 1E-05 all have one.
        static int Significant(string text) => text.TrimStart('-').Split('E')[0].Replace(".", "").Trim('0').Length;
    }

    [Fact]
    public void Binary32TooReadsBackShortest()
    {
        var random = new Random(16);
        var target = Target.Of(IeeeFormat.Binary32);
        for (int i = 0; i < 20_000; i++)
        {
            float f = BitConverter.Int32BitsToSingle(random.Next() ^ (random.Next(2) << 31));
            if (!float.IsFinite(f)) continue;
            string main = ResultText.Of(new FloatValue(f), target).Main;
            float back = float.Parse(main, CultureInfo.InvariantCulture);
            Assert.True(BitConverter.SingleToInt32Bits(back) == BitConverter.SingleToInt32Bits(f), $"{f:R}: \"{main}\" reads back as {back:R}");
        }
    }

    [Fact]
    public void ShortAndMainAreQuickOnFullSignificandsAt2To20Bits()
    {
        var target = Target.Bits(1 << 20);
        var floats = new FloatArithmetic(target, RoundingMode.ToNearestEven);
        var random = new Random(17);
        ResultText.Of(floats.Divide(floats.Literal("1", default), floats.Literal("7", default), default), target); // warm up
        for (int i = 0; i < 5; i++)
        {
            Value value = floats.Divide(floats.Literal(random.NextInt64(1L << 40, long.MaxValue).ToString(CultureInfo.InvariantCulture), default),
                                        floats.Literal(random.NextInt64(1L << 40, long.MaxValue).ToString(CultureInfo.InvariantCulture), default), default);
            foreach (Notation notation in Enum.GetValues<Notation>())
            {
                var clock = Stopwatch.StartNew();
                var text = ResultText.Of(value, target, new TextOptions(notation));
                Assert.True(clock.ElapsedMilliseconds < 200, $"{notation}: {clock.ElapsedMilliseconds} ms");
                Assert.False(text.Complete);
                Assert.True(text.Short.Length < 40);
            }
        }
    }

    [Fact]
    public void ThePreviewStaysQuickAt2To22Bits()
    {
        // Straight E99 grows with the precision, about 100 ms at 2^20 bits in Release; rounding to 1,024 bits first keeps a
        // preview under a millisecond at any size. At 2^22 bits the difference can't hide inside the 200 ms budget.
        var target = Target.Bits(1 << 22);
        var floats = new FloatArithmetic(target, RoundingMode.ToNearestEven);
        Value value = floats.Divide(floats.Literal("8761345987234598765", default), floats.Literal("1234567890123456789", default), default);
        ResultText.Of(value, target);
        foreach (Notation notation in Enum.GetValues<Notation>())
        {
            var clock = Stopwatch.StartNew();
            ResultText.Of(value, target, new TextOptions(notation));
            Assert.True(clock.ElapsedMilliseconds < 200, $"{notation}: {clock.ElapsedMilliseconds} ms");
        }
    }

    [Fact]
    public void AtTheVeryTopOfTheRangeItsAnOverflowError()
    {
        // Values whose top bit is at 2^63 - 2 or above parse, but can't be printed in decimal (A2).
        var value = new FloatValue(new ApFloat(1, long.MaxValue - 1, 1));
        var error = Assert.Throws<CalcError>(() => ResultText.Of(value, Bits256));
        Assert.Equal(ErrorKind.Overflow, error.Kind);
    }

    [Fact]
    public void KindDigitsAndCompleteness()
    {
        var text = Text("1/3", Bits256);
        Assert.Equal("float", text.Kind);
        Assert.Equal(76, text.Digits);
        Assert.True(text.Complete);
        Assert.Equal(15, Text("1/3", Binary64).Digits);
        Assert.False(Text("1/3", Target.Bits(8192)).Complete);
        Assert.True(Text("1/3", Target.Bits(4096)).Complete);
    }
}
