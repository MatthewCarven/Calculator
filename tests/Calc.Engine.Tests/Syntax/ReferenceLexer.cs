using System.Text.RegularExpressions;
using Calc.Syntax;

namespace Calc.Engine.Tests.Syntax;

/// <summary>
/// A second lexer, written from B2's rules with regular expressions and no code shared with <see cref="Lexer"/>, as an oracle.
/// It says only which tokens, or that the text is an error, not which error: messages and error spans are the table's job.
/// </summary>
internal static class ReferenceLexer
{
    private const string Dec = "[0-9](?:_?[0-9])*";
    private const string Hex = "[0-9a-fA-F](?:_?[0-9a-fA-F])*";
    private const string Bin = "[01](?:_?[01])*";
    private const string Oct = "[0-7](?:_?[0-7])*";

    private static readonly Regex Space = new(@"\G\s+");
    private static readonly Regex Prefix = new(@"\G0[xXbBoO]");
    private static readonly Regex HexNumber = new($@"\G0[xX](?:{Hex}(?<point>\.(?:{Hex})?)?|(?<point>\.){Hex})(?<exp>[pP][+-]?{Dec})?");
    private static readonly Regex BinNumber = new($@"\G0[bB](?:{Bin}(?<point>\.(?:{Bin})?)?|(?<point>\.){Bin})(?<exp>[pP][+-]?{Dec})?");
    private static readonly Regex OctNumber = new($@"\G0[oO]{Oct}");
    private static readonly Regex DecNumber = new($@"\G(?:{Dec}(?:\.(?:{Dec})?)?|\.{Dec})(?:[eE][+-]?{Dec})?");
    private static readonly Regex Constant = new(@"\G[πτφ]");
    private static readonly Regex Name = new(@"\G[\p{L}_-[πτφ]][\p{L}_0-9-[πτφ]]*");

    private static readonly (string Text, TokenKind Kind)[] Symbols =
    [
        ("**", TokenKind.Caret), ("<<", TokenKind.ShiftLeft), (">>", TokenKind.ShiftRight),
        ("+", TokenKind.Plus), ("-", TokenKind.Minus), ("−", TokenKind.Minus),
        ("*", TokenKind.Star), ("×", TokenKind.Star), ("·", TokenKind.Star), ("/", TokenKind.Slash), ("÷", TokenKind.Slash),
        ("^", TokenKind.Caret), ("!", TokenKind.Bang), ("%", TokenKind.Percent), ("²", TokenKind.Squared), ("³", TokenKind.Cubed),
        ("√", TokenKind.Root), ("~", TokenKind.Tilde), ("&", TokenKind.Ampersand), ("|", TokenKind.Pipe),
        ("(", TokenKind.LeftParen), (")", TokenKind.RightParen), (",", TokenKind.Comma), ("=", TokenKind.Equals),
        ("°", TokenKind.Degree), ("#", TokenKind.Hash),
    ];

    /// <summary>The tokens as (kind, start, end), End included, or null for an error.</summary>
    public static List<(TokenKind Kind, int Start, int End)>? Lex(string s)
    {
        var tokens = new List<(TokenKind, int, int)>();
        int i = 0;
        while (true)
        {
            i += Space.Match(s, i) is { Success: true } space ? space.Length : 0;
            if (i == s.Length)
            {
                tokens.Add((TokenKind.End, i, i));
                return tokens;
            }

            Match m;
            TokenKind kind = TokenKind.Number;
            if (Prefix.IsMatch(s, i))
            {
                char radix = char.ToLowerInvariant(s[i + 1]);
                m = (radix == 'x' ? HexNumber : radix == 'b' ? BinNumber : OctNumber).Match(s, i);
                if (!m.Success) return null;
                if (m.Groups["point"].Success && !m.Groups["exp"].Success) return null;
                string after = radix == 'b' ? "._23456789" : radix == 'o' ? "._89" : "._";
                if (i + m.Length < s.Length && after.Contains(s[i + m.Length])) return null;
            }
            else if ((m = DecNumber.Match(s, i)).Success)
            {
                if (i + m.Length < s.Length && s[i + m.Length] is '.' or '_') return null;
            }
            else if ((m = Constant.Match(s, i)).Success)
            {
                kind = TokenKind.Identifier;
            }
            else if ((m = Name.Match(s, i)).Success)
            {
                kind = m.Value switch
                {
                    "mod" => TokenKind.Mod,
                    "rem" => TokenKind.Rem,
                    "xor" => TokenKind.Xor,
                    "to" => TokenKind.To,
                    _ => TokenKind.Identifier,
                };
            }
            else
            {
                var symbol = Symbols.FirstOrDefault(x => string.CompareOrdinal(s, i, x.Text, 0, x.Text.Length) == 0);
                if (symbol.Text is null) return null;
                tokens.Add((symbol.Kind, i, i + symbol.Text.Length));
                i += symbol.Text.Length;
                continue;
            }
            tokens.Add((kind, i, i + m.Length));
            i += m.Length;
        }
    }
}
