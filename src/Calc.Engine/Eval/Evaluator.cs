using System.Runtime.CompilerServices;
using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Eval;

/// <summary>A result and the flags its evaluation raised.</summary>
public readonly record struct Outcome(Value Value, IeeeFlags Flags);

/// <summary>
/// Walks a tree, left before right, and hands every operation to an <see cref="IArithmetic"/>: it knows no number types.
/// Every exception becomes a <see cref="CalcError"/> through <see cref="ErrorMap"/>, with the span of the operator, name,
/// number or call it came from.
/// </summary>
/// <remarks>
/// Trees can be 200,000 deep, so this runs on the worker's compute thread (rule 13). As in <see cref="Parser"/>, the
/// recursive method is kept lean, with everything else in helpers, and a low stack is a TooBig error, never an overflow.
/// </remarks>
public sealed class Evaluator(IArithmetic arithmetic, Names names)
{
    /// <summary>Parses and evaluates on the calling thread, which should have a big stack (rule 13).</summary>
    public static Outcome Run(string source, Target target, RoundingMode mode, Value? ans = null)
    {
        Node tree = Parser.Parse(source);
        var floats = new FloatArithmetic(target, mode);
        Value value = new Evaluator(floats, new Names(ans)).Evaluate(tree);
        return new Outcome(value, floats.Flags);
    }

    public Value Evaluate(Node node)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw TooDeep(node);
        switch (node)
        {
            case BinaryNode { Op: BinaryOp.To } b:
                // Its right side names a format or unit (F1, H10), so it's refused before either side is evaluated.
                throw NotYet(b);
            case BinaryNode b:
                Value left = Evaluate(b.Left);
                return Binary(b, left, Evaluate(b.Right));
            case PrefixNode p:
                return Prefix(p, Evaluate(p.Operand));
            case PostfixNode p:
                return Postfix(p, Evaluate(p.Operand));
            case CallNode c:
                return Call(c);
            case NumberNode n:
                return Literal(n);
            case NameNode n:
                return Name(n);
            default:
                throw Unknown(node);
        }
    }

    // Everything but the recursion lives out here, so the recursive frame stays small (see Parser's remarks).

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Value Binary(BinaryNode b, Value left, Value right)
    {
        try
        {
            return b.Op switch
            {
                BinaryOp.Add => arithmetic.Add(left, right, b.OpSpan),
                BinaryOp.Subtract => arithmetic.Subtract(left, right, b.OpSpan),
                BinaryOp.Multiply => arithmetic.Multiply(left, right, b.OpSpan),
                BinaryOp.Divide => arithmetic.Divide(left, right, b.OpSpan),
                BinaryOp.Power => arithmetic.Power(left, right, b.OpSpan),
                BinaryOp.Mod => arithmetic.Mod(left, right, b.OpSpan),
                BinaryOp.Rem => arithmetic.Rem(left, right, b.OpSpan),
                BinaryOp.And => arithmetic.And(left, right, b.OpSpan),
                BinaryOp.Or => arithmetic.Or(left, right, b.OpSpan),
                BinaryOp.Xor => arithmetic.Xor(left, right, b.OpSpan),
                BinaryOp.ShiftLeft => arithmetic.ShiftLeft(left, right, b.OpSpan),
                BinaryOp.ShiftRight => arithmetic.ShiftRight(left, right, b.OpSpan),
                _ => throw new CalcError(ErrorKind.Internal, $"Unknown operator {b.Op}.", b.OpSpan),
            };
        }
        catch (Exception e) when (e is not CalcError)
        {
            throw ErrorMap.From(e, b.OpSpan);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Value Prefix(PrefixNode p, Value operand)
    {
        try
        {
            return p.Op switch
            {
                PrefixOp.Negate => arithmetic.Negate(operand, p.OpSpan),
                PrefixOp.Plus => operand,
                PrefixOp.Not => arithmetic.Not(operand, p.OpSpan),
                PrefixOp.Sqrt => arithmetic.Call("sqrt", [operand], p.OpSpan),
                _ => throw new CalcError(ErrorKind.Internal, $"Unknown prefix {p.Op}.", p.OpSpan),
            };
        }
        catch (Exception e) when (e is not CalcError)
        {
            throw ErrorMap.From(e, p.OpSpan);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Value Postfix(PostfixNode p, Value operand)
    {
        try
        {
            return p.Op switch
            {
                PostfixOp.Factorial => arithmetic.Factorial(operand, p.OpSpan),
                PostfixOp.Percent => arithmetic.Percent(operand, p.OpSpan),
                PostfixOp.Square => arithmetic.Power(operand, arithmetic.Literal("2", p.OpSpan), p.OpSpan),
                PostfixOp.Cube => arithmetic.Power(operand, arithmetic.Literal("3", p.OpSpan), p.OpSpan),
                _ => throw new CalcError(ErrorKind.Internal, $"Unknown postfix {p.Op}.", p.OpSpan),
            };
        }
        catch (Exception e) when (e is not CalcError)
        {
            throw ErrorMap.From(e, p.OpSpan);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Value Call(CallNode c)
    {
        var arguments = new Value[c.Arguments.Count];
        for (int i = 0; i < arguments.Length; i++) arguments[i] = Evaluate(c.Arguments[i]);
        try
        {
            return arithmetic.Call(c.Name, arguments, c.Span);
        }
        catch (Exception e) when (e is not CalcError)
        {
            throw ErrorMap.From(e, c.Span);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Value Literal(NumberNode n)
    {
        try
        {
            return arithmetic.Literal(n.Text, n.Span);
        }
        catch (Exception e) when (e is not CalcError)
        {
            throw ErrorMap.From(e, n.Span);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Value Name(NameNode n)
    {
        try
        {
            return names.Resolve(n.Name, n.Span, arithmetic);
        }
        catch (Exception e) when (e is not CalcError)
        {
            throw ErrorMap.From(e, n.Span);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CalcError NotYet(BinaryNode b) => new(ErrorKind.NotAvailable, "'to' isn't available yet.", b.OpSpan);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CalcError TooDeep(Node node) =>
        new(ErrorKind.TooBig, "This is nested too deeply to work through.", node.Span);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CalcError Unknown(Node node) =>
        new(ErrorKind.Internal, $"The evaluator met a {node.GetType().Name}.", node.Span);
}
