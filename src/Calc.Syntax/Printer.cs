using System.Text;

namespace Calc.Syntax;

/// <summary>
/// A tree back to text, with as few parentheses as reading it back needs. This recurses: run it on the worker's compute
/// thread for anything deep (rule 13).
/// </summary>
public static class Printer
{
    /// <summary>
    /// Text that parses back to exactly this tree, implicit products and all. An implicit product is two operands with
    /// a space between, which is always safe: <c>2</c> beside the name <c>e5x</c> would otherwise read back as <c>2e5</c>, <c>x</c>.
    /// </summary>
    public static string Print(Node node)
    {
        var text = new StringBuilder();
        new Writer(text, understood: false).Write(node);
        return text.ToString();
    }

    /// <summary>
    /// How Calc understood the text, for the screen: each implicit product written as <c>·</c>, and a division, mod or rem
    /// on its left put in parentheses, so <c>1/2x</c> shows as <c>(1/2)·x</c>. Null when there's no implicit product, since then
    /// it would say nothing new. It parses back to the same tree, but with the products explicit.
    /// </summary>
    public static string? Understood(Node node)
    {
        var text = new StringBuilder();
        var writer = new Writer(text, understood: true);
        writer.Write(node);
        return writer.SawImplicit ? text.ToString() : null;
    }

    // Each node's rank: an operand of lower rank than its place needs parentheses.
    private const int Atom = 200, PostfixRank = 110, PowerRank = 100, PrefixRank = 90, ProductRank = 80;

    private static int Rank(Node node) => node switch
    {
        BinaryNode b => Rank(b.Op),
        PrefixNode => PrefixRank,
        PostfixNode => PostfixRank,
        _ => Atom,
    };

    private static int Rank(BinaryOp op) => op switch
    {
        BinaryOp.To => 10,
        BinaryOp.Or => 20,
        BinaryOp.Xor => 30,
        BinaryOp.And => 40,
        BinaryOp.ShiftLeft or BinaryOp.ShiftRight => 60,
        BinaryOp.Add or BinaryOp.Subtract => 70,
        BinaryOp.Multiply or BinaryOp.Divide or BinaryOp.Mod or BinaryOp.Rem => ProductRank,
        BinaryOp.Power => PowerRank,
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static string Symbol(BinaryOp op) => op switch
    {
        BinaryOp.To => " to ",
        BinaryOp.Or => " | ",
        BinaryOp.Xor => " xor ",
        BinaryOp.And => " & ",
        BinaryOp.ShiftLeft => " << ",
        BinaryOp.ShiftRight => " >> ",
        BinaryOp.Add => " + ",
        BinaryOp.Subtract => " - ",
        BinaryOp.Multiply => "*",
        BinaryOp.Divide => "/",
        BinaryOp.Mod => " mod ",
        BinaryOp.Rem => " rem ",
        BinaryOp.Power => "^",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static string Symbol(PrefixOp op) => op switch
    {
        PrefixOp.Negate => "-",
        PrefixOp.Plus => "+",
        PrefixOp.Not => "~",
        PrefixOp.Sqrt => "√",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static string Symbol(PostfixOp op) => op switch
    {
        PostfixOp.Factorial => "!",
        PostfixOp.Percent => "%",
        PostfixOp.Square => "²",
        PostfixOp.Cube => "³",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private sealed class Writer(StringBuilder text, bool understood)
    {
        public bool SawImplicit { get; private set; }

        public void Write(Node node)
        {
            switch (node)
            {
                case NumberNode n:
                    text.Append(n.Text);
                    break;
                case NameNode n:
                    text.Append(n.Name);
                    break;
                case CallNode c:
                    text.Append(c.Name).Append('(');
                    for (int i = 0; i < c.Arguments.Count; i++)
                    {
                        if (i > 0) text.Append(", ");
                        Write(c.Arguments[i]);
                    }
                    text.Append(')');
                    break;
                case PrefixNode p:
                    text.Append(Symbol(p.Op));
                    // A prefix operand is parsed at prefix rank, so ^ and postfix bind inside it: -2^2 is -(2^2).
                    WriteOperand(p.Operand, Rank(p.Operand) < PrefixRank);
                    break;
                case PostfixNode p:
                    WriteOperand(p.Operand, Rank(p.Operand) < PostfixRank);
                    text.Append(Symbol(p.Op));
                    break;
                case BinaryNode { IsImplicit: true } b:
                    WriteImplicit(b);
                    break;
                case BinaryNode b:
                    WriteBinary(b);
                    break;
                default:
                    throw new ArgumentException($"Unknown node {node.GetType().Name}.", nameof(node));
            }
        }

        private void WriteBinary(BinaryNode b)
        {
            int rank = Rank(b.Op);
            if (b.Op == BinaryOp.Power)
            {
                // Right associative, and its right side may start with a prefix operator: 2^-3, 2^3^2.
                WriteOperand(b.Left, Rank(b.Left) <= rank);
                text.Append(Symbol(b.Op));
                WriteOperand(b.Right, Rank(b.Right) < PrefixRank);
                return;
            }
            WriteOperand(b.Left, Rank(b.Left) < rank);
            text.Append(Symbol(b.Op));
            WriteOperand(b.Right, Rank(b.Right) <= rank);
        }

        private void WriteImplicit(BinaryNode b)
        {
            SawImplicit = true;
            // Read back, the right side must start with a name, ( or √. On screen the · is explicit, so only rank matters.
            bool rightInParens = understood ? Rank(b.Right) <= ProductRank : RightInParens(b);
            bool rightStartsWithParen = rightInParens || First(b.Right) == Start.Paren;
            bool leftInParens = Rank(b.Left) < ProductRank
                // Read back, a name before ( would be a call, and % before an operand is an error.
                || (!understood && rightStartsWithParen && EndsWithName(b.Left))
                || (!understood && EndsWithPercent(b.Left))
                // On screen, show how a division grouped: (1/2)·x.
                || (understood && b.Left is BinaryNode { IsImplicit: false, Op: BinaryOp.Divide or BinaryOp.Mod or BinaryOp.Rem });
            WriteOperand(b.Left, leftInParens);
            text.Append(understood ? "·" : " ");
            WriteOperand(b.Right, rightInParens);
        }

        private void WriteOperand(Node node, bool parens)
        {
            if (parens) text.Append('(');
            Write(node);
            if (parens) text.Append(')');
        }

        /// <summary>Whether, in a read-back print, an implicit product's right side goes in parentheses: when it would start with anything but a name, ( or √.</summary>
        private static bool RightInParens(BinaryNode b) =>
            Rank(b.Right) <= ProductRank || First(b.Right) is Start.Number or Start.OtherPrefix;

        private enum Start { Name, Number, Paren, Sqrt, OtherPrefix }

        /// <summary>
        /// The first token of a bare print, for a node ranked above products. Above products, only a power's left side and
        /// a postfix's operand can begin its text, unless they're in parentheses.
        /// </summary>
        private static Start First(Node node)
        {
            while (true)
            {
                switch (node)
                {
                    case NameNode or CallNode:
                        return Start.Name;
                    case NumberNode:
                        return Start.Number;
                    case PrefixNode p:
                        return p.Op == PrefixOp.Sqrt ? Start.Sqrt : Start.OtherPrefix;
                    case PostfixNode p:
                        if (Rank(p.Operand) < PostfixRank) return Start.Paren;
                        node = p.Operand;
                        break;
                    case BinaryNode { IsImplicit: false, Op: BinaryOp.Power } b:
                        if (Rank(b.Left) <= PowerRank) return Start.Paren;
                        node = b.Left;
                        break;
                    default:
                        throw new ArgumentException("Only nodes ranked above products are asked.", nameof(node));
                }
            }
        }

        /// <summary>Whether the node, printed bare, ends with a name.</summary>
        private static bool EndsWithName(Node node) => Last(node) is NameNode;

        /// <summary>Whether the node, printed bare, ends with %.</summary>
        private static bool EndsWithPercent(Node node) => Last(node) is PostfixNode { Op: PostfixOp.Percent };

        /// <summary>The node whose text ends the bare print: a leaf, a call, a postfix, or a node printed in parentheses.</summary>
        private static Node Last(Node node)
        {
            while (true)
            {
                switch (node)
                {
                    case PrefixNode p when Rank(p.Operand) >= PrefixRank:
                        node = p.Operand;
                        break;
                    case BinaryNode { IsImplicit: false, Op: BinaryOp.Power } b when Rank(b.Right) >= PrefixRank:
                        node = b.Right;
                        break;
                    case BinaryNode { IsImplicit: false } b when b.Op != BinaryOp.Power && Rank(b.Right) > Rank(b.Op):
                        node = b.Right;
                        break;
                    case BinaryNode { IsImplicit: true } b when !RightInParens(b):
                        node = b.Right;
                        break;
                    default:
                        return node;
                }
            }
        }
    }
}
