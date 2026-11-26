using System.Globalization;
using Calc.Engine.Eval;
using Calc.Engine.Text;
using Calc.Engine.Values;
using Natural;

namespace Calc.Engine.Tests.Text;

public class LosslessTests
{
    [Theory]
    [InlineData("255", "i:FF")]
    [InlineData("-255", "i:-FF")]
    [InlineData("0", "i:0")]
    [InlineData("1/3", "r:1/3")]
    [InlineData("-255/256", "r:-FF/100")]
    public void ExactTags(string source, string expected) =>
        Assert.Equal(expected, Lossless.Write(Evaluator.Run(source, Target.Bits(256), RoundingMode.ToNearestEven, exact: true).Value, Target.Bits(256)));

    [Theory]
    [InlineData("1.5", 256, null, "f:256:0X1.8P+0")]
    [InlineData("-0.125", 53, null, "f:53:-0X1P-3")]
    [InlineData("1/0", 0, 64, "f:11/53:Infinity")]
    [InlineData("0/0", 0, 64, "f:11/53:NaN")]
    [InlineData("0*-1", 0, 64, "f:11/53:-0X0P+0")]
    public void FloatTags(string source, int bits, int? format, string expected)
    {
        Target target = format is null ? Target.Bits(bits) : Target.Of(IeeeFormat.Binary64);
        Assert.Equal(expected, Lossless.Write(Evaluator.Run(source, target, RoundingMode.ToNearestEven).Value, target));
    }

    [Fact]
    public void EveryKindRoundTrips()
    {
        var random = new Random(18);
        Target[] targets = [Target.Bits(7), Target.Bits(53), Target.Bits(256), Target.Bits(3000), Target.Of(IeeeFormat.Binary16),
                            Target.Of(IeeeFormat.Binary32), Target.Of(IeeeFormat.Binary64), Target.Of(IeeeFormat.Binary128)];
        string[] sources = ["0", "-7", "1/3", "-22/7", "2^4000", "-3^500/7^300", "0.1", "1/0", "-1/0", "0/0", "0*-1", "2^-1074", "1e300*1e300", "sqrt(2)"];
        int checkedCount = 0;
        foreach (var target in targets)
            foreach (string source in sources.Concat(Enumerable.Range(0, 40).Select(_ => $"{random.Next(-1000, 1000)}/{random.Next(1, 1000)}*2^{random.Next(-60, 60)}")))
            {
                foreach (bool exact in new[] { true, false })
                {
                    Value value;
                    try { value = Evaluator.Run(source, target, RoundingMode.ToNearestEven, exact: exact).Value; }
                    catch (global::Calc.Syntax.CalcError) { continue; } // 1/0 at bits, say
                    string text = Lossless.Write(value, target);
                    var (back, backTarget) = Lossless.Read(text);
                    Assert.Equal(text, Lossless.Write(back, backTarget ?? target));
                    switch (value)
                    {
                        case FloatValue f:
                            Assert.Equal(target, backTarget);
                            var g = Assert.IsType<FloatValue>(back);
                            Assert.Equal(f.Number.Precision, g.Number.Precision);
                            Assert.True(f.Number.IsNaN ? g.Number.IsNaN : f.Number.CompareTo(g.Number) == 0 && f.Number.IsNegative == g.Number.IsNegative, $"{text}");
                            break;
                        default:
                            Assert.Null(backTarget);
                            Assert.Equal(value, back);
                            break;
                    }
                    checkedCount++;
                }
            }
        Assert.InRange(checkedCount, 500, 1000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x:1")]
    [InlineData("i:")]
    [InlineData("i:-")]
    [InlineData("i:ff")]
    [InlineData("i:0x1")]
    [InlineData("r:1")]
    [InlineData("r:2/4")]
    [InlineData("r:1/1")]
    [InlineData("r:1/-3")]
    [InlineData("r:1/0")]
    [InlineData("f:1.5")]
    [InlineData("f:0:1")]
    [InlineData("f:11/53")]
    [InlineData("f:11/53:banana")]
    [InlineData("f:99/2:1")]
    public void MalformedTextIsRefused(string text) =>
        Assert.ThrowsAny<FormatException>(() => Lossless.Read(text));

    [Fact]
    public void ATagCantBeCrossed()
    {
        // An integer's text read as a ratio's, or a ratio's as an integer's, is refused, never misread.
        Assert.Throws<FormatException>(() => Lossless.Read("r:FF"));
        Assert.Throws<FormatException>(() => Lossless.Read("i:1/3"));
        Assert.Equal(new IntValue(ApInt.Parse("255", CultureInfo.InvariantCulture)), Lossless.Read("i:FF").Value);
    }
}
