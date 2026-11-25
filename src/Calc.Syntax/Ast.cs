namespace Calc.Syntax;

public enum PrefixOp
{
    /// <summary>-</summary>
    Negate,
    /// <summary>+, which does nothing but is kept, so the tree prints as written.</summary>
    Plus,
    /// <summary>~, bitwise not.</summary>
    Not,
    /// <summary>√</summary>
    Sqrt,
}

public enum PostfixOp
{
    /// <summary>!</summary>
    Factorial,
    /// <summary>%, which divides by 100 (Matthew, 2026-11-25). It's never a remainder: that's mod.</summary>
    Percent,
    /// <summary>²</summary>
    Square,
    /// <summary>³</summary>
    Cube,
}

/// <summary>Binary operators, loosest first.</summary>
public enum BinaryOp
{
    /// <summary>to, reserved for conversions (F1, H10).</summary>
    To,
    /// <summary>|, bitwise or.</summary>
    Or,
    Xor,
    /// <summary>&amp;, bitwise and.</summary>
    And,
    ShiftLeft,
    ShiftRight,
    Add,
    Subtract,
    /// <summary>*, ×, ·, or nothing at all: see <see cref="BinaryNode.IsImplicit"/>.</summary>
    Multiply,
    Divide,
    /// <summary>mod, floored.</summary>
    Mod,
    /// <summary>rem, truncated.</summary>
    Rem,
    /// <summary>^ or **, right associative.</summary>
    Power,
}

/// <summary>
/// A node of the tree. Plain classes, not records: a record's generated Equals, GetHashCode and ToString recurse, and
/// trees here can be 200,000 deep, which only the worker's big stack survives (rule 13). Nothing here walks the tree.
/// </summary>
public abstract class Node(TextSpan span, int depth)
{
    /// <summary>The whole expression's text, its parentheses included. The parser widens it over them, before anyone else sees the node.</summary>
    public TextSpan Span { get; internal set; } = span;

    /// <summary>How many nodes the longest path down to a leaf holds, this one included: 1 for a leaf. Worked out once, when built.</summary>
    public int Depth { get; } = depth;

    public override string ToString() => $"{GetType().Name} {Span}";
}

public sealed class NumberNode(string text, TextSpan span) : Node(span, 1)
{
    /// <summary>The number's text exactly as written, underscores and all, as the lexer took it.</summary>
    public string Text { get; } = text;
}

public sealed class NameNode(string name, TextSpan span) : Node(span, 1)
{
    public string Name { get; } = name;
}

public sealed class PrefixNode(PrefixOp op, Node operand, TextSpan span, TextSpan opSpan) : Node(span, checked(operand.Depth + 1))
{
    public PrefixOp Op { get; } = op;
    public Node Operand { get; } = operand;
    public TextSpan OpSpan { get; } = opSpan;
}

public sealed class PostfixNode(PostfixOp op, Node operand, TextSpan span, TextSpan opSpan) : Node(span, checked(operand.Depth + 1))
{
    public PostfixOp Op { get; } = op;
    public Node Operand { get; } = operand;
    public TextSpan OpSpan { get; } = opSpan;
}

public sealed class BinaryNode(BinaryOp op, Node left, Node right, TextSpan span, TextSpan opSpan, bool isImplicit = false)
    : Node(span, checked(Math.Max(left.Depth, right.Depth) + 1))
{
    public BinaryOp Op { get; } = op;
    public Node Left { get; } = left;
    public Node Right { get; } = right;

    /// <summary>The operator's text, for errors. For an implicit product, which has none, it's the whole product.</summary>
    public TextSpan OpSpan { get; } = opSpan;

    /// <summary>A product written with no operator, as in 2x or 2(3). Always <see cref="BinaryOp.Multiply"/>.</summary>
    public bool IsImplicit { get; } = isImplicit;
}

public sealed class CallNode(string name, IReadOnlyList<Node> arguments, TextSpan nameSpan, TextSpan span)
    : Node(span, checked(Deepest(arguments) + 1))
{
    private static int Deepest(IReadOnlyList<Node> nodes)
    {
        int deepest = 0;
        for (int i = 0; i < nodes.Count; i++) deepest = Math.Max(deepest, nodes[i].Depth);
        return deepest;
    }

    public string Name { get; } = name;
    public IReadOnlyList<Node> Arguments { get; } = arguments;
    public TextSpan NameSpan { get; } = nameSpan;
}
