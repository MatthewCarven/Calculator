using System.Numerics;
using Calc.Protocol;

namespace Calc.Engine.Tests.Text;

public class DigitsTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(53, 15)]
    [InlineData(113, 33)]
    [InlineData(256, 76)]
    [InlineData(512, 153)]
    [InlineData(1024, 307)]
    [InlineData(2048, 616)]
    [InlineData(4096, 1232)]
    [InlineData(8192, 2465)]
    [InlineData(16384, 4931)]
    [InlineData(1 << 30, 323228496)]
    public void EveryPreset(int bits, int digits) => Assert.Equal(digits, Digits.For(bits));

    [Fact]
    public void TwoMillionPrecisionsAgreeWithA128BitConstant()
    {
        // floor((p − 1)·log10 2) by the 128-bit fraction, which no p up to 2^30 can fool.
        BigInteger log10Of2 = BigInteger.Parse("04D104D427DE7FBCC47C4ACD605BE48BC", System.Globalization.NumberStyles.HexNumber);
        Assert.InRange((double)log10Of2 / Math.Pow(2, 128), Math.Log10(2) - 1e-15, Math.Log10(2) + 1e-15);
        var random = new Random(9);
        for (int i = 0; i < 2_000_000; i++)
        {
            int p = i < 100_000 ? i + 1 : random.Next(1, Digits.MaxBits + 1);
            int expected = Math.Max(1, (int)((p - 1) * log10Of2 >> 128));
            if (Digits.For(p) != expected) Assert.Fail($"For({p}) is {Digits.For(p)}, not {expected}");
        }
    }

    [Fact]
    public void BitsForIsTheFewestBitsThatHoldThoseDigits()
    {
        var random = new Random(10);
        int top = Digits.For(Digits.MaxBits);
        for (int i = 0; i < 200_000; i++)
        {
            int d = i < 20_000 ? i + 1 : random.Next(1, top + 1);
            int bits = Digits.BitsFor(d);
            Assert.True(Digits.For(bits) >= d, $"BitsFor({d}) = {bits} holds only {Digits.For(bits)}");
            if (d >= 2) Assert.True(Digits.For(bits - 1) < d, $"BitsFor({d}) = {bits}, but {bits - 1} holds {Digits.For(bits - 1)}");
        }
        Assert.Equal(5, Digits.BitsFor(1));
        Assert.Equal(254, Digits.BitsFor(76)); // floor(253·log10 2) = 76, floor(252·log10 2) = 75
        Assert.Equal(51, Digits.BitsFor(15));
    }

    [Fact]
    public void OutsideTheRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Digits.For(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Digits.For(Digits.MaxBits + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Digits.BitsFor(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Digits.BitsFor(Digits.For(Digits.MaxBits) + 1));
    }
}
