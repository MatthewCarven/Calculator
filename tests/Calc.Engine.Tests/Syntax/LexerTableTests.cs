using Calc.Syntax;

namespace Calc.Engine.Tests.Syntax;

public class LexerTableTests
{
    /// <summary>Tokens as "Kind:text@start", space separated, ending "End@n".</summary>
    internal static string Render(IReadOnlyList<Token> tokens) =>
        string.Join(" ", tokens.Select(t => t.Kind == TokenKind.End ? $"End@{t.Span.Start}" : $"{t.Kind}:{t.Text}@{t.Span.Start}"));

    [Theory]
    // Decimal.
    [InlineData("12", "Number:12@0 End@2")]
    [InlineData("1.5", "Number:1.5@0 End@3")]
    [InlineData(".5", "Number:.5@0 End@2")]
    [InlineData("1.", "Number:1.@0 End@2")]
    [InlineData("1e5", "Number:1e5@0 End@3")]
    [InlineData("1E-5", "Number:1E-5@0 End@4")]
    [InlineData("1e+5", "Number:1e+5@0 End@4")]
    [InlineData("1.e5", "Number:1.e5@0 End@4")]
    [InlineData(".5e-3", "Number:.5e-3@0 End@5")]
    [InlineData("007", "Number:007@0 End@3")]
    [InlineData("1_000", "Number:1_000@0 End@5")]
    [InlineData("1_000.000_1", "Number:1_000.000_1@0 End@11")]
    [InlineData("1e1_0", "Number:1e1_0@0 End@5")]
    // Hex, binary, octal.
    [InlineData("0xFF", "Number:0xFF@0 End@4")]
    [InlineData("0XFF", "Number:0XFF@0 End@4")]
    [InlineData("0xff", "Number:0xff@0 End@4")]
    [InlineData("0xF_F", "Number:0xF_F@0 End@5")]
    [InlineData("0x1e5", "Number:0x1e5@0 End@5")]
    [InlineData("0x1.8p+1", "Number:0x1.8p+1@0 End@8")]
    [InlineData("0X1.8P+1", "Number:0X1.8P+1@0 End@8")]
    [InlineData("0x1p3", "Number:0x1p3@0 End@5")]
    [InlineData("0x1.p3", "Number:0x1.p3@0 End@6")]
    [InlineData("0x.8p1", "Number:0x.8p1@0 End@6")]
    [InlineData("0b101", "Number:0b101@0 End@5")]
    [InlineData("0B101", "Number:0B101@0 End@5")]
    [InlineData("0b101.01p-3", "Number:0b101.01p-3@0 End@11")]
    [InlineData("0b1_0", "Number:0b1_0@0 End@5")]
    [InlineData("0o17", "Number:0o17@0 End@4")]
    [InlineData("0O17", "Number:0O17@0 End@4")]
    // What Natural's "A" format writes, which must paste back.
    [InlineData("-0x1.999999999999ap-4", "Minus:-@0 Number:0x1.999999999999ap-4@1 End@21")]
    [InlineData("0x0p+0", "Number:0x0p+0@0 End@6")]
    [InlineData("0X0.000P+0", "Number:0X0.000P+0@0 End@10")]
    // An exponent without digits isn't one.
    [InlineData("1e", "Number:1@0 Identifier:e@1 End@2")]
    [InlineData("1e+", "Number:1@0 Identifier:e@1 Plus:+@2 End@3")]
    [InlineData("1e+x", "Number:1@0 Identifier:e@1 Plus:+@2 Identifier:x@3 End@4")]
    [InlineData("1ex", "Number:1@0 Identifier:ex@1 End@3")]
    [InlineData("1e5x", "Number:1e5@0 Identifier:x@3 End@4")]
    [InlineData("1.e", "Number:1.@0 Identifier:e@2 End@3")]
    [InlineData("1e_5", "Number:1@0 Identifier:e_5@1 End@4")]
    [InlineData("0x1p", "Number:0x1@0 Identifier:p@3 End@4")]
    [InlineData("0b1e5", "Number:0b1@0 Identifier:e5@3 End@5")]
    [InlineData("1p5", "Number:1@0 Identifier:p5@1 End@3")]
    // Commas and spaces never group digits.
    [InlineData("max(1,000)", "Identifier:max@0 LeftParen:(@3 Number:1@4 Comma:,@5 Number:000@6 RightParen:)@9 End@10")]
    [InlineData("1 000 000", "Number:1@0 Number:000@2 Number:000@6 End@9")]
    [InlineData("1.2 .3", "Number:1.2@0 Number:.3@4 End@6")]
    [InlineData("12abc", "Number:12@0 Identifier:abc@2 End@5")]
    // Aliases.
    [InlineData("2×3·4÷5−6**7", "Number:2@0 Star:×@1 Number:3@2 Star:·@3 Number:4@4 Slash:÷@5 Number:5@6 Minus:−@7 Number:6@8 Caret:**@9 Number:7@11 End@12")]
    [InlineData("***", "Caret:**@0 Star:*@2 End@3")]
    [InlineData("2^3", "Number:2@0 Caret:^@1 Number:3@2 End@3")]
    [InlineData("π τ φ", "Identifier:π@0 Identifier:τ@2 Identifier:φ@4 End@5")]
    [InlineData("2πr", "Number:2@0 Identifier:π@1 Identifier:r@2 End@3")]
    [InlineData("rπ²", "Identifier:r@0 Identifier:π@1 Squared:²@2 End@3")]
    [InlineData("√2", "Root:√@0 Number:2@1 End@2")]
    [InlineData("x²+y³", "Identifier:x@0 Squared:²@1 Plus:+@2 Identifier:y@3 Cubed:³@4 End@5")]
    [InlineData("30°", "Number:30@0 Degree:°@2 End@3")]
    [InlineData("#3", "Hash:#@0 Number:3@1 End@2")]
    // Words.
    [InlineData("7 mod 3", "Number:7@0 Mod:mod@2 Number:3@6 End@7")]
    [InlineData("rem xor to", "Rem:rem@0 Xor:xor@4 To:to@8 End@10")]
    [InlineData("mod3 Mod modulo", "Identifier:mod3@0 Identifier:Mod@5 Identifier:modulo@9 End@15")]
    [InlineData("Ans _x x_1 _", "Identifier:Ans@0 Identifier:_x@4 Identifier:x_1@7 Identifier:_@11 End@12")]
    [InlineData("θ1", "Identifier:θ1@0 End@2")]
    // The rest of the operators.
    [InlineData("1<<2>>3", "Number:1@0 ShiftLeft:<<@1 Number:2@3 ShiftRight:>>@4 Number:3@6 End@7")]
    [InlineData("~a&b|c", "Tilde:~@0 Identifier:a@1 Ampersand:&@2 Identifier:b@3 Pipe:|@4 Identifier:c@5 End@6")]
    [InlineData("5!+50%", "Number:5@0 Bang:!@1 Plus:+@2 Number:50@3 Percent:%@5 End@6")]
    [InlineData("x=1", "Identifier:x@0 Equals:=@1 Number:1@2 End@3")]
    [InlineData("f(a, b)", "Identifier:f@0 LeftParen:(@1 Identifier:a@2 Comma:,@3 Identifier:b@5 RightParen:)@6 End@7")]
    // Whitespace, of every kind, and nothing.
    [InlineData("", "End@0")]
    [InlineData("   ", "End@3")]
    [InlineData("\t1\n+ 2 ", "Number:1@1 Plus:+@3 Number:2@5 End@7")]
    public void Lexes(string source, string expected) => Assert.Equal(expected, Render(Lexer.Lex(source)));

    [Theory]
    // Hex and binary fractions need their p exponent.
    [InlineData("0x1.8", 0, 5, "A hex fraction needs a p exponent")]
    [InlineData("0x1.8e2", 0, 7, "A hex fraction needs a p exponent")]
    [InlineData("0x1.8p", 0, 5, "A hex fraction needs a p exponent")]
    [InlineData("0x1.8p+", 0, 5, "A hex fraction needs a p exponent")]
    [InlineData("0xF.", 0, 4, "A hex fraction needs a p exponent")]
    [InlineData("0b101.01", 0, 8, "A binary fraction needs a p exponent")]
    // Prefixes need digits, of their radix.
    [InlineData("0x", 0, 2, "0x needs hex digits")]
    [InlineData("0Xg", 0, 2, "0X needs hex digits")]
    [InlineData("0x.p1", 0, 3, "0x needs hex digits")]
    [InlineData("0b", 0, 2, "0b needs binary digits")]
    [InlineData("0b2", 2, 3, "'2' isn't a binary digit")]
    [InlineData("0b1012", 5, 6, "'2' isn't a binary digit")]
    [InlineData("0b1.12p0", 5, 6, "'2' isn't a binary digit")]
    [InlineData("0o", 0, 2, "0o needs octal digits")]
    [InlineData("0o8", 2, 3, "'8' isn't an octal digit")]
    [InlineData("0O19", 3, 4, "'9' isn't an octal digit")]
    [InlineData("0o1.5", 0, 4, "Octal numbers are whole")]
    // Underscores go between digits.
    [InlineData("1__000", 1, 2, "An underscore goes between two digits")]
    [InlineData("1_", 1, 2, "An underscore goes between two digits")]
    [InlineData("1_.5", 1, 2, "An underscore goes between two digits")]
    [InlineData("1._5", 2, 3, "An underscore goes between two digits")]
    [InlineData("1_e5", 1, 2, "An underscore goes between two digits")]
    [InlineData("1e5_", 3, 4, "An underscore goes between two digits")]
    [InlineData("1_x", 1, 2, "An underscore goes between two digits")]
    [InlineData("0x_FF", 2, 3, "An underscore goes between two digits")]
    [InlineData("0b1_2", 3, 4, "An underscore goes between two digits")]
    // One point, and none after an exponent.
    [InlineData("1.2.3", 0, 4, "A number has one point at most")]
    [InlineData("1..2", 0, 3, "A number has one point at most")]
    [InlineData("1e5.5", 0, 4, "An exponent can't have a point")]
    [InlineData("0x1p3.5", 0, 6, "An exponent can't have a point")]
    [InlineData(".", 0, 1, "A point needs a digit right after it")]
    [InlineData("1+.", 2, 3, "A point needs a digit right after it")]
    [InlineData("x.y", 1, 2, "A point needs a digit right after it")]
    // Characters that mean nothing yet.
    [InlineData("1 < 2", 2, 3, "Unexpected character '<'")]
    [InlineData(">", 0, 1, "Unexpected character '>'")]
    [InlineData("a;b", 1, 2, "Unexpected character ';'")]
    [InlineData("[1]", 0, 1, "Unexpected character '['")]
    [InlineData("5 – 3", 2, 3, "Unexpected character '–'")]
    [InlineData("x¹", 1, 2, "Unexpected character '¹'")]
    [InlineData("∞", 0, 1, "Unexpected character '∞'")]
    [InlineData("\u0001", 0, 1, "Unexpected character U+0001")]
    [InlineData("1​2", 1, 2, "Unexpected character U+200B")]
    [InlineData("é", 1, 2, "Unexpected character U+0301")]
    [InlineData("𝑥", 0, 2, "Unexpected character '𝑥'")]
    public void Refuses(string source, int start, int end, string message) => CheckRefusal(source, start, end, message);

    // xUnit mangles a lone surrogate in InlineData, so these two are facts.
    [Fact]
    public void RefusesALoneHighSurrogate() => CheckRefusal("\uD800x", 0, 1, "Unexpected character U+D800");

    [Fact]
    public void RefusesALoneLowSurrogate() => CheckRefusal("x\uDC00", 1, 2, "Unexpected character U+DC00");

    private static void CheckRefusal(string source, int start, int end, string message)
    {
        var error = Assert.Throws<CalcError>(() => Lexer.Lex(source));
        Assert.Equal(ErrorKind.Syntax, error.Kind);
        Assert.Equal(new TextSpan(start, end), error.Span);
        Assert.StartsWith(message, error.Message);
    }

    [Fact]
    public void SingleAsciiTokensShareTheirText()
    {
        var tokens = Lexer.Lex("1+1+1");
        Assert.Same(tokens[0].Text, tokens[2].Text);
        Assert.Same(tokens[1].Text, tokens[3].Text);
    }
}
