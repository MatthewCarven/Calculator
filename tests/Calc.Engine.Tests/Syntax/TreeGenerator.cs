using Calc.Syntax;

namespace Calc.Engine.Tests.Syntax;

/// <summary>
/// Random trees of every node kind, built directly rather than parsed, with numbers and names chosen to trip a printer:
/// <c>e5x</c> beside a number, <c>x1</c> after a 0, <c>_2</c> after a 1. B4 can build on it with its own leaves.
/// </summary>
internal sealed class TreeGenerator(int seed)
{
    private static readonly string[] Numbers = ["1", "7", "2.5", ".5", "1.", "0xFF", "0x1.8p+1", "1e5", "1E-5", "1_000", "0b101", "0o17", "0"];
    private static readonly string[] Names = ["x", "y", "π", "e", "Ans", "e5x", "x1", "_2", "θ", "p"];
    private static readonly string[] Functions = ["f", "sin", "max"];
    private static readonly TextSpan None = default;

    private readonly Random _random = new(seed);

    public Node Next(int depth)
    {
        if (depth <= 1 || _random.Next(4) == 0) return Leaf();
        return _random.Next(10) switch
        {
            0 => new PrefixNode((PrefixOp)_random.Next(4), Next(depth - 1), None, None),
            1 => new PostfixNode((PostfixOp)_random.Next(4), Next(depth - 1), None, None),
            2 => Call(depth),
            3 or 4 => new BinaryNode(BinaryOp.Multiply, Next(depth - 1), Next(depth - 1), None, None, isImplicit: true),
            _ => new BinaryNode((BinaryOp)_random.Next(13), Next(depth - 1), Next(depth - 1), None, None),
        };
    }

    private Node Leaf() => _random.Next(2) == 0
        ? new NumberNode(Numbers[_random.Next(Numbers.Length)], None)
        : new NameNode(Names[_random.Next(Names.Length)], None);

    private Node Call(int depth)
    {
        var arguments = new List<Node>();
        for (int i = _random.Next(4); i > 0; i--) arguments.Add(Next(depth - 1));
        return new CallNode(Functions[_random.Next(Functions.Length)], arguments, None, None);
    }
}
