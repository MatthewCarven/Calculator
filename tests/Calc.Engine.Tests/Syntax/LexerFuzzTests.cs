using System.Diagnostics;
using System.Text;
using Calc.Syntax;

namespace Calc.Engine.Tests.Syntax;

public class LexerFuzzTests
{
    // Weighted towards what makes numbers hard: digits, points, underscores, exponent and radix letters,
    // digits no radix but 10 has, aliases, and a few characters that must be refused.
    private static readonly string[] Pieces =
    [
        "0", "0", "0", "1", "1", "1", "2", "5", "7", "8", "9", ".", ".", "_", "_", "e", "E", "x", "X", "b", "B", "o", "O",
        "p", "P", "a", "f", "F", "g", "+", "-", "*", "/", "^", "(", ")", ",", "=", " ", " ", "π", "×", "−", "<", ">", "!",
        "%", "²", "√", "°", "#", "m", "d", "r", "t", " ", ";", "𝑥", "\uD800", "θ", "\t", "mod", "0x", "0b", "0o",
    ];

    [Fact]
    public void AgreesWithTheReferenceLexer()
    {
        var random = new Random(20261125);
        int errors = 0;
        for (int n = 0; n < 100_000; n++)
        {
            var text = new StringBuilder();
            int length = random.Next(17);
            for (int k = 0; k < length; k++) text.Append(Pieces[random.Next(Pieces.Length)]);
            string source = text.ToString();

            var expected = ReferenceLexer.Lex(source);
            IReadOnlyList<Token>? actual = null;
            try
            {
                actual = Lexer.Lex(source);
            }
            catch (CalcError error)
            {
                CheckError(source, error);
            }

            if (expected is null)
            {
                if (actual is not null)
                    Assert.Fail($"{Show(source)}: the reference refuses it, but the lexer gave {LexerTableTests.Render(actual)}");
                errors++;
            }
            else
            {
                if (actual is null)
                    Assert.Fail($"{Show(source)}: the reference accepts it, but the lexer refused it");
                CheckTokens(source, actual);
                Assert.Equal(expected, actual.Select(t => (t.Kind, t.Span.Start, t.Span.End)).ToList());
            }
        }
        // Both outcomes must be well represented, or the comparison means little.
        Assert.InRange(errors, 20_000, 80_000);
    }

    [Fact]
    public void AnyTextGivesTokensOrACalcError()
    {
        var random = new Random(11);
        for (int n = 0; n < 100_000; n++)
        {
            int length = random.Next(25);
            var chars = new char[length];
            for (int k = 0; k < length; k++)
                chars[k] = random.Next(4) == 0 ? (char)random.Next(128) : (char)random.Next(0x10000);
            string source = new(chars);
            try
            {
                CheckTokens(source, Lexer.Lex(source));
            }
            catch (CalcError error)
            {
                CheckError(source, error);
            }
            // Anything else escapes and fails the test.
        }
    }

    [Fact]
    public void LexesAMillionTermsOnASmallStack()
    {
        // The Client lexes on any thread, so the lexer must loop, never recurse (rule 13).
        string source = string.Join("+", Enumerable.Repeat("1", 1_000_000));
        int count = 0;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { count = Lexer.Lex(source).Count; }
            catch (Exception e) { failure = e; }
        }, 256 * 1024);
        var clock = Stopwatch.StartNew();
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the lexer ran for 30 s");
        Assert.Null(failure);
        Assert.Equal(2_000_000, count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    /// <summary>Spans in range and in order, each token's text exactly its span, only whitespace between, and one End at the end.</summary>
    private static void CheckTokens(string source, IReadOnlyList<Token> tokens)
    {
        int at = 0;
        for (int k = 0; k < tokens.Count; k++)
        {
            var token = tokens[k];
            for (int c = at; c < token.Span.Start; c++)
                Assert.True(char.IsWhiteSpace(source[c]), $"{Show(source)}: a gap holds {Show(source[c].ToString())}");
            if (k == tokens.Count - 1)
            {
                Assert.Equal(TokenKind.End, token.Kind);
                Assert.Equal(new TextSpan(source.Length, source.Length), token.Span);
                Assert.Equal("", token.Text);
            }
            else
            {
                Assert.NotEqual(TokenKind.End, token.Kind);
                Assert.True(token.Span.Start < token.Span.End && token.Span.End <= source.Length, $"{Show(source)}: span {token.Span}");
                Assert.Equal(source[token.Span.Start..token.Span.End], token.Text);
            }
            at = token.Span.End;
        }
    }

    private static void CheckError(string source, CalcError error)
    {
        Assert.Equal(ErrorKind.Syntax, error.Kind);
        Assert.NotNull(error.Span);
        var span = error.Span.Value;
        Assert.True(span.Start >= 0 && span.Start < span.End && span.End <= source.Length, $"{Show(source)}: error span {span}");
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static string Show(string s) =>
        "\"" + string.Concat(s.Select(c => c is < ' ' or > '~' ? $"\\u{(int)c:X4}" : c.ToString())) + "\"";
}
