using System.Text;
using Calc.Syntax;

namespace Calc.Engine.Tests.Syntax;

/// <summary>A tree as an S-expression, ignoring spans: (+ 1 (* 2 x)). An implicit product is (· a b), unless erased to (* a b).</summary>
internal static class TreeText
{
    public static string Of(Node node, bool eraseImplicit = false)
    {
        var text = new StringBuilder();
        Write(node, text, eraseImplicit);
        return text.ToString();
    }

    private static void Write(Node node, StringBuilder text, bool erase)
    {
        switch (node)
        {
            case NumberNode n: text.Append(n.Text); return;
            case NameNode n: text.Append(n.Name); return;
            case PrefixNode p:
                text.Append('(').Append(p.Op switch { PrefixOp.Negate => "neg", PrefixOp.Plus => "pos", PrefixOp.Not => "not", _ => "sqrt" }).Append(' ');
                Write(p.Operand, text, erase);
                text.Append(')');
                return;
            case PostfixNode p:
                text.Append('(').Append(p.Op switch { PostfixOp.Factorial => "!", PostfixOp.Percent => "%", PostfixOp.Square => "sq", _ => "cube" }).Append(' ');
                Write(p.Operand, text, erase);
                text.Append(')');
                return;
            case BinaryNode b:
                text.Append('(').Append(b.IsImplicit && !erase ? "·" : Symbol(b.Op)).Append(' ');
                Write(b.Left, text, erase);
                text.Append(' ');
                Write(b.Right, text, erase);
                text.Append(')');
                return;
            case CallNode c:
                text.Append("(call ").Append(c.Name);
                foreach (var argument in c.Arguments)
                {
                    text.Append(' ');
                    Write(argument, text, erase);
                }
                text.Append(')');
                return;
            default:
                throw new ArgumentException(node.GetType().Name);
        }
    }

    public static string Symbol(BinaryOp op) => op switch
    {
        BinaryOp.To => "to",
        BinaryOp.Or => "|",
        BinaryOp.Xor => "xor",
        BinaryOp.And => "&",
        BinaryOp.ShiftLeft => "<<",
        BinaryOp.ShiftRight => ">>",
        BinaryOp.Add => "+",
        BinaryOp.Subtract => "-",
        BinaryOp.Multiply => "*",
        BinaryOp.Divide => "/",
        BinaryOp.Mod => "mod",
        BinaryOp.Rem => "rem",
        BinaryOp.Power => "^",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>Every node of a small tree, parents first.</summary>
    public static IEnumerable<Node> Nodes(Node node)
    {
        yield return node;
        IEnumerable<Node> children = node switch
        {
            PrefixNode p => [p.Operand],
            PostfixNode p => [p.Operand],
            BinaryNode b => [b.Left, b.Right],
            CallNode c => c.Arguments,
            _ => [],
        };
        foreach (var child in children)
            foreach (var n in Nodes(child))
                yield return n;
    }
}
