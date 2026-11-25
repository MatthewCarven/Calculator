using System.Runtime.CompilerServices;

namespace Calc.Syntax;

/// <summary>
/// Tokens to a tree, by Pratt parsing. Bad input gives only a <see cref="CalcError"/> with a span.
/// </summary>
/// <remarks>
/// <para>Loosest first: <c>to</c>; <c>|</c>; <c>xor</c>; <c>&amp;</c>; (a gap for comparisons, H1); <c>&lt;&lt;</c> <c>&gt;&gt;</c>;
/// <c>+</c> <c>-</c>; <c>*</c> <c>/</c> <c>mod</c> <c>rem</c> and implicit products; prefix <c>-</c> <c>+</c> <c>~</c> <c>√</c>;
/// <c>^</c>, right associative, whose right side may start with a prefix operator; postfix <c>!</c> <c>%</c> <c>²</c> <c>³</c>.
/// So <c>-2^2</c> is -4, <c>2^-3</c> is 1/8 and <c>-3!</c> is -6.</para>
/// <para>An implicit product ranks with <c>*</c>, left to right, so <c>1/2x</c> is (1/2)·x and <c>6/2(1+2)</c> is 9
/// (Matthew, 2026-11-25). Only a name, <c>(</c> or <c>√</c> can start one: never a number, so <c>1 000</c> is an error,
/// and never right after <c>%</c>, so <c>7 % 3</c> is an error that says to use mod. A name followed by <c>(</c> is a call.</para>
/// <para>Depth: this recurses, so run it on the worker's compute thread (rule 13). It refuses more than
/// <see cref="MaxDepth"/> levels of recursion, counting parentheses, prefix chains, <c>^</c> chains and arguments, before it
/// recurses further; and a tree deeper than that, such as a long flat sum's left spine, which is built by a loop. On any
/// thread whose stack runs low first, it refuses there instead, so it never overflows the stack.</para>
/// <para>The recursive methods are kept lean, because a Debug build gives every local and temporary its own stack slot:
/// errors are built in helpers, tokens are read in place from an array, and the token wanting an operand is passed as its
/// index. Before that, a level of brackets took 2,456 bytes in Debug, and 200,000 of them didn't fit 256 MiB.</para>
/// </remarks>
public sealed class Parser
{
    public const int MaxDepth = 200_000;

    private const int PrefixPower = 90;
    private const int ProductPower = 80;
    private const int PowerPower = 100;
    private const int NoOwner = -1;

    private readonly Token[] _tokens;
    private int _at;
    private int _nesting;

    private Parser(Token[] tokens) => _tokens = tokens;

    public static Node Parse(string source) => Parse(Lexer.Lex(source));

    /// <summary>Parses tokens from <see cref="Lexer.Lex"/>, which end with <see cref="TokenKind.End"/>.</summary>
    public static Node Parse(IReadOnlyList<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0 || tokens[^1].Kind != TokenKind.End)
            throw new ArgumentException("The tokens must end with an End token, as Lexer.Lex's do.", nameof(tokens));
        return new Parser([.. tokens]).ParseAll();
    }

    private Node ParseAll()
    {
        if (_tokens[_at].Kind == TokenKind.End) throw Syntax(_at, "Nothing to calculate.");
        Node node = ParseExpression(0, NoOwner);
        return _tokens[_at].Kind == TokenKind.End ? node : throw Leftover(_at);
    }

    /// <summary>
    /// An operand, then every operator binding tighter than <paramref name="minPower"/>. Stops only at ')', ',' or the end.
    /// <paramref name="owner"/> is the index of the token that wants this operand, for errors, or <see cref="NoOwner"/>.
    /// </summary>
    private Node ParseExpression(int minPower, int owner)
    {
        if (++_nesting > MaxDepth) throw TooDeep(owner == NoOwner ? _at : owner, counted: true);
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw TooDeep(owner == NoOwner ? _at : owner, counted: false);

        Node left = ParseOperand(owner);
        while (true)
        {
            TokenKind kind = _tokens[_at].Kind;
            int power = BinaryPower(kind);
            if (power > 0)
            {
                if (power <= minPower) break;
                int op = _at++;
                Node right = ParseExpression(kind == TokenKind.Caret ? power - 1 : power, op);
                left = Binary(kind, left, right, op);
            }
            else if (IsPostfix(kind))
            {
                // Postfix binds tightest of all, so it always applies here.
                left = Postfix(left);
            }
            else if (StartsProduct(kind))
            {
                if (ProductPower <= minPower) break;
                Node right = ParseExpression(ProductPower, NoOwner);
                left = Implicit(left, right);
            }
            else if (kind is TokenKind.RightParen or TokenKind.Comma or TokenKind.End)
            {
                break;
            }
            else
            {
                throw AfterOperand(_at);
            }
        }
        _nesting--;
        return left;
    }

    private Node ParseOperand(int owner)
    {
        int at = _at;
        switch (_tokens[at].Kind)
        {
            case TokenKind.Number:
                _at++;
                return new NumberNode(_tokens[at].Text, _tokens[at].Span);

            case TokenKind.Identifier:
                _at++;
                return _tokens[_at].Kind == TokenKind.LeftParen ? ParseCall(at) : new NameNode(_tokens[at].Text, _tokens[at].Span);

            case TokenKind.LeftParen:
                _at++;
                if (_tokens[_at].Kind == TokenKind.RightParen) throw EmptyBrackets(at);
                Node inner = ParseExpression(0, at);
                if (_tokens[_at].Kind != TokenKind.RightParen) throw NotClosed(at);
                inner.Span = new TextSpan(_tokens[at].Span.Start, _tokens[_at].Span.End);
                _at++;
                return inner;

            case TokenKind.Minus or TokenKind.Plus or TokenKind.Tilde or TokenKind.Root:
                _at++;
                return Prefix(at, ParseExpression(PrefixPower, at));

            default:
                throw NoOperand(at, owner);
        }
    }

    private Node ParseCall(int name)
    {
        int open = _at++;
        var arguments = new List<Node>();
        if (_tokens[_at].Kind != TokenKind.RightParen)
        {
            while (true)
            {
                if (_tokens[_at].Kind is not (TokenKind.Comma or TokenKind.RightParen or TokenKind.End))
                    arguments.Add(ParseExpression(0, NoOwner));
                else
                    throw MissingArgument(_at, open);
                if (_tokens[_at].Kind != TokenKind.Comma) break;
                _at++;
            }
            if (_tokens[_at].Kind != TokenKind.RightParen) throw Unclosed(open);
        }
        return Call(name, arguments, _at++);
    }

    // Building nodes, and everything else that isn't the recursion itself, happens out here, to keep its frames small.

    private Node Binary(TokenKind kind, Node left, Node right, int op) =>
        Checked(new BinaryNode(BinaryOpOf(kind), left, right, Between(left, right), _tokens[op].Span));

    private Node Implicit(Node left, Node right)
    {
        var span = Between(left, right);
        return Checked(new BinaryNode(BinaryOp.Multiply, left, right, span, span, isImplicit: true));
    }

    private Node Prefix(int op, Node operand)
    {
        var prefix = _tokens[op].Kind switch
        {
            TokenKind.Minus => PrefixOp.Negate,
            TokenKind.Plus => PrefixOp.Plus,
            TokenKind.Tilde => PrefixOp.Not,
            _ => PrefixOp.Sqrt,
        };
        return Checked(new PrefixNode(prefix, operand, new TextSpan(_tokens[op].Span.Start, operand.Span.End), _tokens[op].Span));
    }

    private Node Postfix(Node operand)
    {
        int op = _at++;
        var postfix = _tokens[op].Kind switch
        {
            TokenKind.Bang => PostfixOp.Factorial,
            TokenKind.Percent => PostfixOp.Percent,
            TokenKind.Squared => PostfixOp.Square,
            _ => PostfixOp.Cube,
        };
        if (postfix == PostfixOp.Percent && (StartsProduct(_tokens[_at].Kind) || _tokens[_at].Kind is TokenKind.Number or TokenKind.Tilde))
            throw Syntax(op, "% means percent here. For a remainder, use mod.");
        return Checked(new PostfixNode(postfix, operand, new TextSpan(operand.Span.Start, _tokens[op].Span.End), _tokens[op].Span));
    }

    private Node Call(int name, List<Node> arguments, int close) =>
        Checked(new CallNode(_tokens[name].Text, arguments, _tokens[name].Span, new TextSpan(_tokens[name].Span.Start, _tokens[close].Span.End)));

    /// <summary>A binary operator's binding power, or 0 when the token isn't one. Only ^ is right associative.</summary>
    private static int BinaryPower(TokenKind kind) => kind switch
    {
        TokenKind.To => 10,
        TokenKind.Pipe => 20,
        TokenKind.Xor => 30,
        TokenKind.Ampersand => 40,
        // 50 is free for comparisons (H1).
        TokenKind.ShiftLeft or TokenKind.ShiftRight => 60,
        TokenKind.Plus or TokenKind.Minus => 70,
        TokenKind.Star or TokenKind.Slash or TokenKind.Mod or TokenKind.Rem => ProductPower,
        TokenKind.Caret => PowerPower,
        _ => 0,
    };

    private static BinaryOp BinaryOpOf(TokenKind kind) => kind switch
    {
        TokenKind.To => BinaryOp.To,
        TokenKind.Pipe => BinaryOp.Or,
        TokenKind.Xor => BinaryOp.Xor,
        TokenKind.Ampersand => BinaryOp.And,
        TokenKind.ShiftLeft => BinaryOp.ShiftLeft,
        TokenKind.ShiftRight => BinaryOp.ShiftRight,
        TokenKind.Plus => BinaryOp.Add,
        TokenKind.Minus => BinaryOp.Subtract,
        TokenKind.Star => BinaryOp.Multiply,
        TokenKind.Slash => BinaryOp.Divide,
        TokenKind.Mod => BinaryOp.Mod,
        TokenKind.Rem => BinaryOp.Rem,
        _ => BinaryOp.Power,
    };

    private static bool IsPostfix(TokenKind kind) => kind is TokenKind.Bang or TokenKind.Percent or TokenKind.Squared or TokenKind.Cubed;

    /// <summary>What can start the right side of an implicit product. Never a number, nor a prefix - + or ~.</summary>
    private static bool StartsProduct(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.LeftParen or TokenKind.Root;

    private static Node Checked(Node node) =>
        node.Depth <= MaxDepth ? node : throw TooDeep(node is BinaryNode b ? b.OpSpan : node.Span, counted: true);

    private static TextSpan Between(Node left, Node right) => new(left.Span.Start, right.Span.End);

    // The errors. Each is built here, never inline in the recursion, because a Debug build reserves stack for every
    // string it might format in a method, on every call.

    [MethodImpl(MethodImplOptions.NoInlining)]
    private CalcError NoOperand(int at, int owner)
    {
        Token t = _tokens[at];
        if (Reserved(t) is CalcError reserved) return reserved;
        if (owner != NoOwner)
        {
            Token o = _tokens[owner];
            if (o.Kind == TokenKind.LeftParen && t.Kind == TokenKind.End) return Unclosed(owner);
            if (t.Kind is TokenKind.End or TokenKind.RightParen or TokenKind.Comma) return Syntax(owner, $"'{o.Text}' needs something after it.");
        }
        return t.Kind switch
        {
            TokenKind.RightParen or TokenKind.Comma => Leftover(at),
            _ => Syntax(at, $"'{t.Text}' needs something before it."),
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private CalcError AfterOperand(int at)
    {
        Token t = _tokens[at];
        return Reserved(t) ?? (t.Kind == TokenKind.Number
            ? Syntax(at, "Put an operator before this number.")
            : Syntax(at, $"Put an operator before '{t.Text}'."));
    }

    /// <summary>What's left where the end should be: a ')' with no '(' or a ',' outside a call.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private CalcError Leftover(int at) => _tokens[at].Kind switch
    {
        TokenKind.RightParen => Syntax(at, "This ')' has no '(' to close."),
        TokenKind.Comma => Syntax(at, "A comma only separates a function's arguments."),
        _ => new CalcError(ErrorKind.Internal, $"The parser stopped early, at '{_tokens[at].Text}'.", _tokens[at].Span),
    };

    /// <summary>Brackets around an expression ended without their ')': at a comma, which belongs only in a call, or at the end.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private CalcError NotClosed(int open) => _tokens[_at].Kind == TokenKind.Comma ? Leftover(_at) : Unclosed(open);

    private CalcError Unclosed(int open) => Syntax(open, "This '(' isn't closed.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private CalcError EmptyBrackets(int open) =>
        new(ErrorKind.Syntax, "Empty brackets: put something between them.", new TextSpan(_tokens[open].Span.Start, _tokens[open + 1].Span.End));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private CalcError MissingArgument(int at, int open) => _tokens[at].Kind == TokenKind.End
        ? Unclosed(open)
        : Syntax(at, $"Expected an argument before this '{_tokens[at].Text}'.");

    private static CalcError? Reserved(Token t) => t.Kind switch
    {
        TokenKind.Degree => new CalcError(ErrorKind.NotAvailable, "Degrees (°) aren't available yet.", t.Span),
        TokenKind.Hash => new CalcError(ErrorKind.NotAvailable, "'#' is reserved.", t.Span),
        TokenKind.Equals => new CalcError(ErrorKind.NotAvailable, "Definitions (=) aren't available yet.", t.Span),
        _ => null,
    };

    private CalcError Syntax(int at, string message) => new(ErrorKind.Syntax, message, _tokens[at].Span);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private CalcError TooDeep(int at, bool counted) => TooDeep(_tokens[at].Span, counted);

    private static CalcError TooDeep(TextSpan span, bool counted) => new(ErrorKind.TooBig, counted
        ? $"This is nested too deeply: more than {MaxDepth:N0} levels."
        : "This is nested too deeply to work through.", span);
}
