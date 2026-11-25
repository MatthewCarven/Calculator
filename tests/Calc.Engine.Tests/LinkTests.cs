using System.Numerics;

namespace Calc.Engine.Tests;

public class LinkTests
{
    [Fact]
    public void OneThirdComesFromNaturalToSeventySixDigits()
    {
        // The oracle: 10^76 / 3, by BigInteger, is the first 76 digits of 1/3.
        string expected = "0." + (BigInteger.Pow(10, 76) / 3).ToString();
        Assert.Equal(78, expected.Length);
        Assert.Equal(expected, Link.OneThird());
    }
}
