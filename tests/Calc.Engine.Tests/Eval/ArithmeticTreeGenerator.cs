using System.Globalization;
using Calc.Syntax;

namespace Calc.Engine.Tests.Eval;

/// <summary>A literal, with how an oracle gets its value: from the double it was printed from, as a whole number, or by parsing its decimal text.</summary>
internal sealed record Leaf(string Text, double? FromDouble = null, long? Whole = null);

/// <summary>
/// Random trees of + − × ÷, implicit products, unary - and +, over literals in every form the lexer takes: decimal,
/// underscored, hex floats, 0x, 0b and 0o whole numbers, long integers, 1. and .5. Built directly, not parsed. Later
/// backends (B7's exact values) can reuse it with their own oracles.
/// </summary>
internal sealed class ArithmeticTreeGenerator(int seed, bool extremes = false)
{
    private static readonly TextSpan None = default;
    private readonly Random _random = new(seed);

    /// <summary>Each literal's source, by node.</summary>
    public Dictionary<NumberNode, Leaf> Leaves { get; } = new(ReferenceEqualityComparer.Instance);

    public Node Next(int depth)
    {
        if (depth <= 1 || _random.Next(4) == 0) return Literal();
        return _random.Next(12) switch
        {
            0 => new PrefixNode(PrefixOp.Negate, Next(depth - 1), None, None),
            1 => new PrefixNode(PrefixOp.Plus, Next(depth - 1), None, None),
            2 or 3 => Binary(BinaryOp.Add, depth),
            4 or 5 => Binary(BinaryOp.Subtract, depth),
            6 or 7 => Binary(BinaryOp.Multiply, depth),
            8 => new BinaryNode(BinaryOp.Multiply, Next(depth - 1), Next(depth - 1), None, None, isImplicit: true),
            _ => Binary(BinaryOp.Divide, depth),
        };
    }

    private BinaryNode Binary(BinaryOp op, int depth) => new(op, Next(depth - 1), Next(depth - 1), None, None);

    private NumberNode Literal()
    {
        Leaf leaf = MakeLeaf();
        var node = new NumberNode(leaf.Text, None);
        Leaves[node] = leaf;
        return node;
    }

    private Leaf MakeLeaf()
    {
        switch (_random.Next(12))
        {
            case 0 or 1 or 2:
                return new Leaf(RandomDouble().ToString("R", CultureInfo.InvariantCulture));
            case 3:
            {
                double d = RandomDouble();
                return d is >= 1 and < 1e15 ? new Leaf(Grouped(d)) : new Leaf(d.ToString("R", CultureInfo.InvariantCulture));
            }
            case 4:
            {
                double d = RandomDouble();
                return double.IsNormal(d) ? new Leaf(Hex(d, _random.Next(2) == 0), FromDouble: d) : new Leaf("0");
            }
            case 5:
            {
                long n = _random.Next(0, 1_000_000);
                return new Leaf(n.ToString(CultureInfo.InvariantCulture), Whole: n);
            }
            case 6:
            {
                long n = _random.Next(0, 1 << 20);
                return _random.Next(3) switch
                {
                    0 => new Leaf((_random.Next(2) == 0 ? "0b" : "0B") + Convert.ToString(n, 2), Whole: n),
                    1 => new Leaf((_random.Next(2) == 0 ? "0o" : "0O") + Convert.ToString(n, 8), Whole: n),
                    _ => new Leaf((_random.Next(2) == 0 ? "0x" : "0X") + n.ToString(_random.Next(2) == 0 ? "x" : "X", CultureInfo.InvariantCulture), Whole: n),
                };
            }
            case 7:
            {
                // A whole number too long for a double to hold exactly.
                var digits = new char[_random.Next(17, 40)];
                digits[0] = (char)('1' + _random.Next(9));
                for (int i = 1; i < digits.Length; i++) digits[i] = (char)('0' + _random.Next(10));
                return new Leaf(new string(digits));
            }
            case 8:
                return new Leaf(_random.Next(3) switch { 0 => $"{_random.Next(1000)}.", 1 => $".{_random.Next(1, 1000)}", _ => $"{_random.Next(1, 10)}e{_random.Next(-20, 21)}" });
            case 9 when extremes:
                return new Leaf(Extremes[_random.Next(Extremes.Length)]);
            default:
                return new Leaf(_random.Next(1, 100).ToString(CultureInfo.InvariantCulture), Whole: null);
        }
    }

    private static readonly string[] Extremes =
    [
        "1e300", "1e-300", "4.9e-324", "5e-324", "1.7976931348623157e308", "2.2250738585072014e-308", "2.225073858507201e-308",
        "3.4028234663852886e38", "1.401298464324817e-45", "1.1754943508222875e-38", "1e39", "1e-46", "65504", "6e-8",
    ];

    private double RandomDouble()
    {
        double mantissa = 1 + _random.NextDouble();
        int exponent = extremes && _random.Next(5) == 0 ? _random.Next(-1074, 1024) : _random.Next(-40, 41);
        return Math.ScaleB(mantissa, exponent);
    }

    private static string Grouped(double d)
    {
        string text = d.ToString("R", CultureInfo.InvariantCulture);
        int point = text.IndexOf('.');
        string whole = point < 0 ? text : text[..point];
        var grouped = new System.Text.StringBuilder();
        for (int i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0) grouped.Append('_');
            grouped.Append(whole[i]);
        }
        return point < 0 ? grouped.ToString() : grouped + text[point..];
    }

    /// <summary>A normal double exactly, as C's %a writes it: 0x1.8p+1.</summary>
    private static string Hex(double d, bool upper)
    {
        long bits = BitConverter.DoubleToInt64Bits(d);
        long fraction = bits & ((1L << 52) - 1);
        int exponent = (int)((bits >> 52) & 0x7FF) - 1023;
        string text = $"0x1.{fraction:x13}p{exponent.ToString("+0;-0", CultureInfo.InvariantCulture)}";
        return upper ? text.ToUpperInvariant() : text;
    }
}
