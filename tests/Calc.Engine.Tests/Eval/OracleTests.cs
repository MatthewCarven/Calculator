using System.Globalization;
using Calc.Engine.Eval;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Tests.Eval;

public class OracleTests
{
    private static Value Evaluate(Node tree, Target target, RoundingMode mode, out IeeeFlags flags)
    {
        var floats = new FloatArithmetic(target, mode);
        Value value = new Evaluator(floats, new Names()).Evaluate(tree);
        flags = floats.Flags;
        return value;
    }

    [Fact]
    public void Binary64MatchesDoubleBitForBit()
    {
        var generator = new ArithmeticTreeGenerator(64, extremes: true);
        for (int n = 0; n < 20_000; n++)
        {
            Node tree = generator.Next(6);
            double expected = AsDouble(tree, generator);
            double actual = (double)((FloatValue)Evaluate(tree, Target.Of(IeeeFormat.Binary64), RoundingMode.ToNearestEven, out _)).Number;
            if (!(double.IsNaN(expected) && double.IsNaN(actual)) && BitConverter.DoubleToInt64Bits(expected) != BitConverter.DoubleToInt64Bits(actual))
                Assert.Fail($"{Printer.Print(tree)}: double gives {expected:R}, Calc {actual:R}");
        }
    }

    [Fact]
    public void Binary32MatchesFloatBitForBit()
    {
        var generator = new ArithmeticTreeGenerator(32, extremes: true);
        for (int n = 0; n < 20_000; n++)
        {
            Node tree = generator.Next(6);
            float expected = AsFloat(tree, generator);
            float actual = (float)((FloatValue)Evaluate(tree, Target.Of(IeeeFormat.Binary32), RoundingMode.ToNearestEven, out _)).Number;
            if (!(float.IsNaN(expected) && float.IsNaN(actual)) && BitConverter.SingleToInt32Bits(expected) != BitConverter.SingleToInt32Bits(actual))
                Assert.Fail($"{Printer.Print(tree)}: float gives {expected:R}, Calc {actual:R}");
        }
    }

    [Fact]
    public void RandomPrecisionsAndModesMatchTheRationalOracle()
    {
        var generator = new ArithmeticTreeGenerator(7);
        var random = new Random(99);
        RoundingMode[] modes = [RoundingMode.ToNearestEven, RoundingMode.TowardZero, RoundingMode.TowardPositive, RoundingMode.TowardNegative];
        int divisionsByZero = 0;
        for (int n = 0; n < 5_000; n++)
        {
            Node tree = generator.Next(6);
            int precision = random.Next(2, 301);
            RoundingMode mode = modes[random.Next(4)];
            string what = $"{Printer.Print(tree)} at {precision} bits, {mode}";

            bool inexact = false;
            Rational? expected;
            try { expected = RationalOracle.Evaluate(tree, precision, mode, ref inexact); }
            catch (OracleDivideByZero) { expected = null; }

            Value? actual = null;
            IeeeFlags flags = IeeeFlags.None;
            CalcError? error = null;
            try { actual = Evaluate(tree, Target.Bits(precision), mode, out flags); }
            catch (CalcError e) { error = e; }

            if (expected is null)
            {
                divisionsByZero++;
                if (error?.Kind != ErrorKind.DivideByZero) Assert.Fail($"{what}: expected DivideByZero, got {error?.Kind.ToString() ?? actual!.ToString()}");
                continue;
            }
            if (error is not null) Assert.Fail($"{what}: expected {expected}, got {error.Kind}: {error.Message}");
            var number = ((FloatValue)actual!).Number;
            Assert.True(number.Precision == precision, $"{what}: came back at {number.Precision} bits");
            Rational got = RationalOracle.ToRational(number);
            if (got != expected) Assert.Fail($"{what}: expected {expected}, got {got}");
            if (inexact != ((flags & IeeeFlags.Inexact) != 0)) Assert.Fail($"{what}: Inexact should be {inexact}, flags {flags}");
            if ((flags & ~IeeeFlags.Inexact) != 0) Assert.Fail($"{what}: unexpected flags {flags}");
        }
        Assert.InRange(divisionsByZero, 1, 500);
    }

    private static double AsDouble(Node node, ArithmeticTreeGenerator generator) => node switch
    {
        NumberNode n => generator.Leaves[n] switch
        {
            { FromDouble: double d } => d,
            { Whole: long w } => w,
            var leaf => double.Parse(leaf.Text.Replace("_", ""), CultureInfo.InvariantCulture),
        },
        PrefixNode { Op: PrefixOp.Negate } p => -AsDouble(p.Operand, generator),
        PrefixNode p => AsDouble(p.Operand, generator),
        BinaryNode b => b.Op switch
        {
            BinaryOp.Add => AsDouble(b.Left, generator) + AsDouble(b.Right, generator),
            BinaryOp.Subtract => AsDouble(b.Left, generator) - AsDouble(b.Right, generator),
            BinaryOp.Multiply => AsDouble(b.Left, generator) * AsDouble(b.Right, generator),
            _ => AsDouble(b.Left, generator) / AsDouble(b.Right, generator),
        },
        _ => throw new ArgumentException(node.GetType().Name),
    };

    private static float AsFloat(Node node, ArithmeticTreeGenerator generator) => node switch
    {
        NumberNode n => generator.Leaves[n] switch
        {
            { FromDouble: double d } => (float)d,
            { Whole: long w } => w,
            var leaf => float.Parse(leaf.Text.Replace("_", ""), CultureInfo.InvariantCulture),
        },
        PrefixNode { Op: PrefixOp.Negate } p => -AsFloat(p.Operand, generator),
        PrefixNode p => AsFloat(p.Operand, generator),
        BinaryNode b => b.Op switch
        {
            BinaryOp.Add => AsFloat(b.Left, generator) + AsFloat(b.Right, generator),
            BinaryOp.Subtract => AsFloat(b.Left, generator) - AsFloat(b.Right, generator),
            BinaryOp.Multiply => AsFloat(b.Left, generator) * AsFloat(b.Right, generator),
            _ => AsFloat(b.Left, generator) / AsFloat(b.Right, generator),
        },
        _ => throw new ArgumentException(node.GetType().Name),
    };
}
