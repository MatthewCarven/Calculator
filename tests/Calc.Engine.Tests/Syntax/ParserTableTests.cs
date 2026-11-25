using Calc.Syntax;

namespace Calc.Engine.Tests.Syntax;

public class ParserTableTests
{
    [Theory]
    // The brief's "So:".
    [InlineData("-2^2", "(neg (^ 2 2))")]
    [InlineData("2^3^2", "(^ 2 (^ 3 2))")]
    [InlineData("2^-3", "(^ 2 (neg 3))")]
    [InlineData("2^3!", "(^ 2 (! 3))")]
    [InlineData("-3!", "(neg (! 3))")]
    [InlineData("√2^2", "(sqrt (^ 2 2))")]
    [InlineData("2^-3^2", "(^ 2 (neg (^ 3 2)))")]
    [InlineData("2^-3*4", "(* (^ 2 (neg 3)) 4)")]
    // Implicit products: ranked with *, left to right (Matthew, 2026-11-25).
    [InlineData("2π", "(· 2 π)")]
    [InlineData("2(3)", "(· 2 3)")]
    [InlineData("(2)(3)", "(· 2 3)")]
    [InlineData("2x", "(· 2 x)")]
    [InlineData("2√3", "(· 2 (sqrt 3))")]
    [InlineData("1/2x", "(· (/ 1 2) x)")]
    [InlineData("6/2(1+2)", "(· (/ 6 2) (+ 1 2))")]
    [InlineData("2x^2", "(· 2 (^ x 2))")]
    [InlineData("2πr", "(· (· 2 π) r)")]
    [InlineData("x y", "(· x y)")]
    [InlineData("-2x", "(· (neg 2) x)")]
    [InlineData("√2x", "(· (sqrt 2) x)")]
    [InlineData("2^3x", "(· (^ 2 3) x)")]
    [InlineData("2x!", "(· 2 (! x))")]
    [InlineData("2x²", "(· 2 (sq x))")]
    [InlineData("(1)(2)(3)", "(· (· 1 2) 3)")]
    [InlineData("3*2x", "(· (* 3 2) x)")]
    [InlineData("2x*3", "(* (· 2 x) 3)")]
    [InlineData("1+2x", "(+ 1 (· 2 x))")]
    // A name before ( is a call, even after a number.
    [InlineData("x(2)", "(call x 2)")]
    [InlineData("(x)(2)", "(· x 2)")]
    [InlineData("2x(3)", "(· 2 (call x 3))")]
    [InlineData("f(2)x", "(· (call f 2) x)")]
    [InlineData("f(2)(3)", "(· (call f 2) 3)")]
    [InlineData("sin (30)", "(call sin 30)")]
    // Percent is ÷100, never a remainder.
    [InlineData("50%", "(% 50)")]
    [InlineData("200 + 10%", "(+ 200 (% 10))")]
    [InlineData("50%%", "(% (% 50))")]
    [InlineData("50% - 3", "(- (% 50) 3)")]
    [InlineData("50%*2", "(* (% 50) 2)")]
    [InlineData("(50%)(3)", "(· (% 50) 3)")]
    [InlineData("-7 mod 3", "(mod (neg 7) 3)")]
    [InlineData("7 rem -3", "(rem 7 (neg 3))")]
    // Calls.
    [InlineData("f()", "(call f)")]
    [InlineData("max(1, 2, 3)", "(call max 1 2 3)")]
    [InlineData("f(g(x), 2+3)", "(call f (call g x) (+ 2 3))")]
    [InlineData("sin(30)^2", "(^ (call sin 30) 2)")]
    [InlineData("π(2)", "(call π 2)")]
    // The rest.
    [InlineData("255 to hex", "(to 255 hex)")]
    [InlineData("1 | 2 xor 3 & 4", "(| 1 (xor 2 (& 3 4)))")]
    [InlineData("1 << 2 + 3", "(<< 1 (+ 2 3))")]
    [InlineData("~x & y", "(& (not x) y)")]
    [InlineData("+x", "(pos x)")]
    [InlineData("--x", "(neg (neg x))")]
    [InlineData("x²³", "(cube (sq x))")]
    [InlineData("2**3", "(^ 2 3)")]
    [InlineData("2×3÷4", "(/ (* 2 3) 4)")]
    [InlineData("1-2-3", "(- (- 1 2) 3)")]
    [InlineData("((1))", "1")]
    [InlineData("0x1.8p+1 + 1_000", "(+ 0x1.8p+1 1_000)")]
    public void Parses(string source, string expected) => Assert.Equal(expected, TreeText.Of(Parser.Parse(source)));

    // The binary operators, as the brief ranks them, written out again here: text, S-expression, rank, right associative.
    private static readonly (string Text, string Symbol, int Rank, bool Right)[] Binaries =
    [
        ("to", "to", 1, false), ("|", "|", 2, false), ("xor", "xor", 3, false), ("&", "&", 4, false),
        ("<<", "<<", 5, false), (">>", ">>", 5, false), ("+", "+", 6, false), ("-", "-", 6, false),
        ("*", "*", 7, false), ("/", "/", 7, false), ("mod", "mod", 7, false), ("rem", "rem", 7, false), ("", "·", 7, false),
        ("^", "^", 9, true),
    ];

    // Prefix operators rank 8, between products and ^; postfix ones above everything.
    private static readonly (string Text, string Symbol)[] Prefixes = [("-", "neg"), ("+", "pos"), ("~", "not"), ("√", "sqrt")];
    private static readonly (string Text, string Symbol)[] Postfixes = [("!", "!"), ("%", "%"), ("²", "sq"), ("³", "cube")];

    [Fact]
    public void EveryPairOfBinaryOperators()
    {
        foreach (var a in Binaries)
            foreach (var b in Binaries)
            {
                string source = $"x {a.Text} y {b.Text} z";
                bool leftFirst = a.Rank > b.Rank || (a.Rank == b.Rank && !b.Right);
                string expected = leftFirst ? $"({b.Symbol} ({a.Symbol} x y) z)" : $"({a.Symbol} x ({b.Symbol} y z))";
                Assert.True(expected == TreeText.Of(Parser.Parse(source)), $"{source}: expected {expected}, got {TreeText.Of(Parser.Parse(source))}");
            }
    }

    [Fact]
    public void EveryPrefixWithEveryBinary()
    {
        foreach (var p in Prefixes)
            foreach (var b in Binaries)
            {
                string before = $"{p.Text}x {b.Text} y";
                string expected = b.Rank > 8 ? $"({p.Symbol} ({b.Symbol} x y))" : $"({b.Symbol} ({p.Symbol} x) y)";
                Assert.True(expected == TreeText.Of(Parser.Parse(before)), $"{before}: expected {expected}");

                // After an implicit product only √ can start the right side: x -y is a subtraction.
                if (b.Text == "" && p.Text != "√") continue;
                string after = $"x {b.Text} {p.Text}y";
                Assert.True($"({b.Symbol} x ({p.Symbol} y))" == TreeText.Of(Parser.Parse(after)), after);
            }
    }

    [Fact]
    public void EveryPostfixWithEveryBinaryAndPrefix()
    {
        foreach (var q in Postfixes)
        {
            foreach (var b in Binaries)
            {
                string onRight = $"x {b.Text} y{q.Text}";
                Assert.True($"({b.Symbol} x ({q.Symbol} y))" == TreeText.Of(Parser.Parse(onRight)), onRight);
                if (b.Text == "" && q.Text == "%") continue; // x% y is an error, tested below
                string onLeft = $"x{q.Text} {b.Text} y";
                Assert.True($"({b.Symbol} ({q.Symbol} x) y)" == TreeText.Of(Parser.Parse(onLeft)), onLeft);
            }
            foreach (var p in Prefixes)
                Assert.Equal($"({p.Symbol} ({q.Symbol} x))", TreeText.Of(Parser.Parse($"{p.Text}x{q.Text}")));
        }
    }

    [Theory]
    // The brief's five.
    [InlineData("1+", ErrorKind.Syntax, 1, 2, "'+' needs something after it.")]
    [InlineData("(1", ErrorKind.Syntax, 0, 1, "This '(' isn't closed.")]
    [InlineData("1)", ErrorKind.Syntax, 1, 2, "This ')' has no '(' to close.")]
    [InlineData("f(", ErrorKind.Syntax, 1, 2, "This '(' isn't closed.")]
    [InlineData(",", ErrorKind.Syntax, 0, 1, "A comma only separates a function's arguments.")]
    // Nothing at all.
    [InlineData("", ErrorKind.Syntax, 0, 0, "Nothing to calculate.")]
    [InlineData("   ", ErrorKind.Syntax, 3, 3, "Nothing to calculate.")]
    // % before an operand.
    [InlineData("7 % 3", ErrorKind.Syntax, 2, 3, "% means percent here. For a remainder, use mod.")]
    [InlineData("7 % x", ErrorKind.Syntax, 2, 3, "% means percent here. For a remainder, use mod.")]
    [InlineData("50%(2)", ErrorKind.Syntax, 2, 3, "% means percent here. For a remainder, use mod.")]
    [InlineData("50% √2", ErrorKind.Syntax, 2, 3, "% means percent here. For a remainder, use mod.")]
    [InlineData("50%~1", ErrorKind.Syntax, 2, 3, "% means percent here. For a remainder, use mod.")]
    [InlineData("2*50% x", ErrorKind.Syntax, 4, 5, "% means percent here. For a remainder, use mod.")]
    // A number never starts an implicit product.
    [InlineData("1 000 000", ErrorKind.Syntax, 2, 5, "Put an operator before this number.")]
    [InlineData("2 3", ErrorKind.Syntax, 2, 3, "Put an operator before this number.")]
    [InlineData("x 2", ErrorKind.Syntax, 2, 3, "Put an operator before this number.")]
    [InlineData("(2)3", ErrorKind.Syntax, 3, 4, "Put an operator before this number.")]
    [InlineData("3! 2", ErrorKind.Syntax, 3, 4, "Put an operator before this number.")]
    [InlineData("2~3", ErrorKind.Syntax, 1, 2, "Put an operator before '~'.")]
    // Missing operands and brackets.
    [InlineData("()", ErrorKind.Syntax, 0, 2, "Empty brackets: put something between them.")]
    [InlineData("(+)", ErrorKind.Syntax, 1, 2, "'+' needs something after it.")]
    [InlineData("1 + )", ErrorKind.Syntax, 2, 3, "'+' needs something after it.")]
    [InlineData("-", ErrorKind.Syntax, 0, 1, "'-' needs something after it.")]
    [InlineData("2^", ErrorKind.Syntax, 1, 2, "'^' needs something after it.")]
    [InlineData("7 mod", ErrorKind.Syntax, 2, 5, "'mod' needs something after it.")]
    [InlineData("*2", ErrorKind.Syntax, 0, 1, "'*' needs something before it.")]
    [InlineData("!3", ErrorKind.Syntax, 0, 1, "'!' needs something before it.")]
    [InlineData("mod 3", ErrorKind.Syntax, 0, 3, "'mod' needs something before it.")]
    [InlineData(")", ErrorKind.Syntax, 0, 1, "This ')' has no '(' to close.")]
    [InlineData("(", ErrorKind.Syntax, 0, 1, "This '(' isn't closed.")]
    [InlineData("((1)", ErrorKind.Syntax, 0, 1, "This '(' isn't closed.")]
    [InlineData("(1,2)", ErrorKind.Syntax, 2, 3, "A comma only separates a function's arguments.")]
    [InlineData("1, 2", ErrorKind.Syntax, 1, 2, "A comma only separates a function's arguments.")]
    // Calls.
    [InlineData("f(1,)", ErrorKind.Syntax, 4, 5, "Expected an argument before this ')'.")]
    [InlineData("f(,1)", ErrorKind.Syntax, 2, 3, "Expected an argument before this ','.")]
    [InlineData("f(1,,2)", ErrorKind.Syntax, 4, 5, "Expected an argument before this ','.")]
    [InlineData("f(1 2)", ErrorKind.Syntax, 4, 5, "Put an operator before this number.")]
    [InlineData("f(1", ErrorKind.Syntax, 1, 2, "This '(' isn't closed.")]
    [InlineData("f(1,", ErrorKind.Syntax, 1, 2, "This '(' isn't closed.")]
    // Reserved, by name.
    [InlineData("30°", ErrorKind.NotAvailable, 2, 3, "Degrees (°) aren't available yet.")]
    [InlineData("°", ErrorKind.NotAvailable, 0, 1, "Degrees (°) aren't available yet.")]
    [InlineData("#3", ErrorKind.NotAvailable, 0, 1, "'#' is reserved.")]
    [InlineData("x = 1", ErrorKind.NotAvailable, 2, 3, "Definitions (=) aren't available yet.")]
    // The lexer's errors come through.
    [InlineData("0x1.8", ErrorKind.Syntax, 0, 5, "A hex fraction needs a p exponent")]
    public void Refuses(string source, ErrorKind kind, int start, int end, string message)
    {
        var error = Assert.Throws<CalcError>(() => Parser.Parse(source));
        Assert.Equal(kind, error.Kind);
        Assert.Equal(new TextSpan(start, end), error.Span);
        Assert.StartsWith(message, error.Message);
    }

    [Fact]
    public void SpansCoverWhatWasWritten()
    {
        var tree = (BinaryNode)Parser.Parse("(1 + 2)*3");
        Assert.Equal(new TextSpan(0, 9), tree.Span);
        Assert.Equal(new TextSpan(7, 8), tree.OpSpan);
        var sum = (BinaryNode)tree.Left;
        Assert.Equal(new TextSpan(0, 7), sum.Span); // widened over its parentheses
        Assert.Equal(new TextSpan(3, 4), sum.OpSpan);

        var product = (BinaryNode)Parser.Parse("2 x");
        Assert.True(product.IsImplicit);
        Assert.Equal(new TextSpan(0, 3), product.OpSpan); // an implicit product has no operator: its whole text

        var call = (CallNode)Parser.Parse("max(1, 2)");
        Assert.Equal(new TextSpan(0, 3), call.NameSpan);
        Assert.Equal(new TextSpan(0, 9), call.Span);
    }

    [Theory]
    [InlineData("1/2x", "(1/2)·x")]
    [InlineData("6/2(1+2)", "(6/2)·(1 + 2)")]
    [InlineData("2πr", "2·π·r")]
    [InlineData("2x^2", "2·x^2")]
    [InlineData("(2)(3)", "2·3")]
    [InlineData("7 mod 3x", "(7 mod 3)·x")]
    [InlineData("3*2x", "3*2·x")]
    [InlineData("1+2√3", "1 + 2·√3")]
    [InlineData("1+2", null)]
    [InlineData("2*x", null)]
    public void ShowsHowItUnderstoodImplicitProducts(string source, string? expected) =>
        Assert.Equal(expected, Printer.Understood(Parser.Parse(source)));

    [Theory]
    [InlineData("1+2*3", "1 + 2*3")]
    [InlineData("(1+2)*3", "(1 + 2)*3")]
    [InlineData("-2^2", "-2^2")]
    [InlineData("(-2)^2", "(-2)^2")]
    [InlineData("2^(3^2)", "2^3^2")]
    [InlineData("(2^3)^2", "(2^3)^2")]
    [InlineData("2x", "2 x")]
    [InlineData("(2x)(3)", "(2 x) (3)")]
    [InlineData("(50%)(3)", "(50%) (3)")]
    [InlineData("x(y)", "x(y)")]
    [InlineData("(x)(y)", "x y")]
    [InlineData("(x)(2)", "(x) (2)")]
    [InlineData("2(-3)", "2 (-3)")]
    [InlineData("(-3)!", "(-3)!")]
    [InlineData("max( 1 ,2 )", "max(1, 2)")]
    [InlineData("1 - (2 - 3)", "1 - (2 - 3)")]
    [InlineData("1 - -2", "1 - -2")]
    public void PrintsWithFewestParentheses(string source, string expected) =>
        Assert.Equal(expected, Printer.Print(Parser.Parse(source)));
}
