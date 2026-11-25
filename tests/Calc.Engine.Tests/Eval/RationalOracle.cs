using System.Globalization;
using System.Numerics;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Tests.Eval;

/// <summary>An exact fraction, by BigInteger, always in lowest terms with a positive denominator. Tests only (rule 3).</summary>
internal readonly record struct Rational(BigInteger Num, BigInteger Den)
{
    public static readonly Rational Zero = new(0, 1);

    public static Rational Of(BigInteger num, BigInteger den)
    {
        if (den.IsZero) throw new DivideByZeroException();
        if (den.Sign < 0) (num, den) = (-num, -den);
        var g = BigInteger.GreatestCommonDivisor(num, den);
        return g.IsOne || g.IsZero ? new(num, den) : new(num / g, den / g);
    }

    public bool IsZero => Num.IsZero;
    public static Rational operator +(Rational a, Rational b) => Of(a.Num * b.Den + b.Num * a.Den, a.Den * b.Den);
    public static Rational operator -(Rational a, Rational b) => Of(a.Num * b.Den - b.Num * a.Den, a.Den * b.Den);
    public static Rational operator *(Rational a, Rational b) => Of(a.Num * b.Num, a.Den * b.Den);
    public static Rational operator /(Rational a, Rational b) => Of(a.Num * b.Den, a.Den * b.Num);
    public static Rational operator -(Rational a) => new(-a.Num, a.Den);
    public static Rational Pow2(long e) => e >= 0 ? new(BigInteger.One << (int)e, 1) : new(1, BigInteger.One << (int)-e);
    public override string ToString() => Den.IsOne ? Num.ToString() : $"{Num}/{Den}";
}

internal sealed class OracleDivideByZero : Exception;

/// <summary>
/// Evaluates <see cref="ArithmeticTreeGenerator"/>'s trees exactly, rounding each literal and each result to p bits in
/// the mode, by its own code, sharing nothing with Calc or Natural. The rounding is IEEE's at an unbounded exponent.
/// </summary>
internal static class RationalOracle
{
    public static Rational Evaluate(Node node, int precision, RoundingMode mode, ref bool inexact)
    {
        switch (node)
        {
            case NumberNode n:
                return Round(Exact(n.Text), precision, mode, ref inexact);
            case PrefixNode { Op: PrefixOp.Negate } p:
                return -Evaluate(p.Operand, precision, mode, ref inexact);
            case PrefixNode { Op: PrefixOp.Plus } p:
                return Evaluate(p.Operand, precision, mode, ref inexact);
            case BinaryNode b:
                Rational left = Evaluate(b.Left, precision, mode, ref inexact);
                Rational right = Evaluate(b.Right, precision, mode, ref inexact);
                Rational exact = b.Op switch
                {
                    BinaryOp.Add => left + right,
                    BinaryOp.Subtract => left - right,
                    BinaryOp.Multiply => left * right,
                    BinaryOp.Divide => right.IsZero ? throw new OracleDivideByZero() : left / right,
                    _ => throw new ArgumentException(b.Op.ToString()),
                };
                return Round(exact, precision, mode, ref inexact);
            default:
                throw new ArgumentException(node.GetType().Name);
        }
    }

    /// <summary>q rounded to <paramref name="precision"/> significant bits in the mode; sets <paramref name="inexact"/> when that changed it.</summary>
    public static Rational Round(Rational q, int precision, RoundingMode mode, ref bool inexact)
    {
        if (q.IsZero) return q;
        int sign = q.Num.Sign;
        BigInteger a = BigInteger.Abs(q.Num), b = q.Den;
        long e = (long)a.GetBitLength() - (long)b.GetBitLength() - precision;
        BigInteger low = BigInteger.One << (precision - 1), high = BigInteger.One << precision;
        BigInteger m, r, d;
        while (true)
        {
            (BigInteger n, d) = e >= 0 ? (a, b << (int)e) : (a << (int)-e, b);
            m = BigInteger.DivRem(n, d, out r);
            if (m < low) e--;
            else if (m >= high) e++;
            else break;
        }
        if (r.IsZero) return q;
        inexact = true;
        bool up = mode switch
        {
            RoundingMode.TowardZero => false,
            RoundingMode.TowardPositive => sign > 0,
            RoundingMode.TowardNegative => sign < 0,
            _ => (2 * r).CompareTo(d) is var c && (c > 0 || (c == 0 && !m.IsEven)),
        };
        if (up) m += 1;
        return Rational.Of(sign * m, 1) * Rational.Pow2(e);
    }

    /// <summary>A literal's exact value, from its text: decimal, hex float, or 0x, 0b, 0o whole numbers, underscores allowed.</summary>
    public static Rational Exact(string text)
    {
        string s = text.Replace("_", "");
        if (s.Length > 1 && s[0] == '0' && char.ToLowerInvariant(s[1]) is 'x' or 'b' or 'o')
        {
            int radix = char.ToLowerInvariant(s[1]) switch { 'x' => 16, 'b' => 2, _ => 8 };
            string body = s[2..];
            long exponent = 0;
            int p = body.IndexOfAny(['p', 'P']);
            if (p >= 0)
            {
                exponent = long.Parse(body[(p + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                body = body[..p];
            }
            int point = body.IndexOf('.');
            string digits = point < 0 ? body : body.Remove(point, 1);
            int fractionDigits = point < 0 ? 0 : body.Length - point - 1;
            BigInteger value = 0;
            foreach (char c in digits) value = value * radix + Convert.ToInt32(c.ToString(), 16);
            Rational r = Rational.Of(value, BigInteger.Pow(radix, fractionDigits));
            return r * Rational.Pow2(exponent);
        }
        else
        {
            long exponent = 0;
            int e = s.IndexOfAny(['e', 'E']);
            if (e >= 0)
            {
                exponent = long.Parse(s[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                s = s[..e];
            }
            int point = s.IndexOf('.');
            string digits = point < 0 ? s : s.Remove(point, 1);
            long fractionDigits = point < 0 ? 0 : s.Length - point - 1;
            BigInteger value = digits.Length == 0 ? 0 : BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            long scale = exponent - fractionDigits;
            return scale >= 0
                ? Rational.Of(value * BigInteger.Pow(10, (int)scale), 1)
                : Rational.Of(value, BigInteger.Pow(10, (int)-scale));
        }
    }

    /// <summary>A finite ApFloat's exact value.</summary>
    public static Rational ToRational(ApFloat x)
    {
        if (x.IsZero) return Rational.Zero;
        BigInteger significand = BigInteger.Abs(BigInteger.Parse(x.Significand.ToString(), CultureInfo.InvariantCulture));
        Rational r = Rational.Of(significand, 1) * Rational.Pow2(x.Exponent);
        return x.IsNegative ? -r : r;
    }
}
