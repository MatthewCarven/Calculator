using System.Text;
using Calc.Syntax;

namespace Calc.Engine.Tests.Syntax;

public class ParserPropertyTests
{
    [Fact]
    public void RandomTreesPrintAndParseBack()
    {
        var generator = new TreeGenerator(20261125);
        int implicitTrees = 0;
        for (int n = 0; n < 20_000; n++)
        {
            Node tree = generator.Next(7);
            string expected = TreeText.Of(tree);
            string text = Printer.Print(tree);
            Node parsed = Parse(text, $"printed from {expected}");
            Assert.True(expected == TreeText.Of(parsed), $"{expected} printed as \"{text}\" read back as {TreeText.Of(parsed)}");
            CheckSpans(text, parsed);

            string? understood = Printer.Understood(tree);
            if (understood is null) continue;
            implicitTrees++;
            Node reread = Parse(understood, $"understood from {expected}");
            Assert.True(TreeText.Of(tree, eraseImplicit: true) == TreeText.Of(reread, eraseImplicit: true),
                $"{expected} shown as \"{understood}\" read back as {TreeText.Of(reread)}");
        }
        Assert.InRange(implicitTrees, 5_000, 19_000);
    }

    // What can come where an operand is wanted, and where an operator is; plus some that are wrong anywhere.
    private static readonly string[] Operands = ["1", "2", "x", "y", "π", "1.5", "0x1F", "2x", "(", "(", "sin(", "max(", "f()", "√", "-", "~", "+"];
    private static readonly string[] Operators =
    [
        "+", "-", "*", "/", "^", "**", "×", "÷", "−", "!", "%", "²", "³", ")", ")", ",", "&", "|", "<<", ">>",
        " mod ", " rem ", " xor ", " to ", " ", "x", "(",
    ];
    private static readonly string[] Wrong = ["=", "°", "#", ".", "1e", "_", " 3", "%3", ",", ")"];

    [Fact]
    public void AnyTextGivesATreeOrACalcError()
    {
        var random = new Random(3);
        int trees = 0;
        for (int n = 0; n < 100_000; n++)
        {
            // Mostly the right kind of piece next, sometimes the wrong kind, now and then one that's wrong anywhere.
            var text = new StringBuilder();
            bool wantOperand = true;
            for (int k = random.Next(1, 14); k > 0; k--)
            {
                int roll = random.Next(20);
                string[] from = roll == 0 ? Wrong : (roll < 17) == wantOperand ? Operands : Operators;
                string piece = from[random.Next(from.Length)];
                text.Append(piece);
                wantOperand = from == Operands ? piece is "(" or "sin(" or "max(" or "√" or "-" or "~" or "+" : from == Operators && piece is not (")" or "!" or "%" or "²" or "³" or "x" or " ");
            }
            string source = text.ToString();
            Node tree;
            try
            {
                tree = Parser.Parse(source);
            }
            catch (CalcError error)
            {
                Assert.NotNull(error.Span);
                var span = error.Span.Value;
                Assert.True(0 <= span.Start && span.Start <= span.End && span.End <= source.Length, $"\"{source}\": error span {span}");
                Assert.False(string.IsNullOrWhiteSpace(error.Message));
                Assert.NotEqual(ErrorKind.Internal, error.Kind);
                continue;
            }
            // Anything else escapes and fails the test.
            trees++;
            CheckSpans(source, tree);
            string printed = Printer.Print(tree);
            Assert.True(TreeText.Of(tree) == TreeText.Of(Parse(printed, source)), $"\"{source}\" printed as \"{printed}\"");
        }
        Assert.InRange(trees, 5_000, 95_000);
    }

    /// <summary>Every node's span is in range, and its text alone parses to the same subtree: what an error highlights is what it's about.</summary>
    private static void CheckSpans(string source, Node tree)
    {
        foreach (var node in TreeText.Nodes(tree))
        {
            Assert.True(0 <= node.Span.Start && node.Span.Start < node.Span.End && node.Span.End <= source.Length,
                $"\"{source}\": {node} out of range");
            string part = source[node.Span.Start..node.Span.End];
            Assert.True(TreeText.Of(node) == TreeText.Of(Parse(part, source)), $"\"{source}\": the span \"{part}\" isn't {TreeText.Of(node)}");
        }
    }

    private static Node Parse(string text, string context)
    {
        try
        {
            return Parser.Parse(text);
        }
        catch (CalcError e)
        {
            Assert.Fail($"\"{text}\" ({context}) didn't parse: {e.Message} at {e.Span}");
            throw;
        }
    }
}
