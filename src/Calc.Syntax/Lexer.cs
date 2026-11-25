using System.Globalization;
using System.Text;

namespace Calc.Syntax;

/// <summary>
/// Text to tokens. Bad input gives a <see cref="CalcError"/> of kind Syntax with a span, and nothing else.
/// It loops and never recurses, so the Client can use it on any thread, whatever the text's length.
/// </summary>
/// <remarks>
/// <para>Numbers: <c>12</c>, <c>1.5</c>, <c>.5</c>, <c>1.</c>, <c>1e5</c>, <c>1E-5</c>, <c>1_000</c>; <c>0xFF</c>, <c>0b101</c>, <c>0o17</c>
/// (and <c>0X</c>, <c>0B</c>, <c>0O</c>); hex and binary fractions with a p exponent, <c>0x1.8p+1</c> and <c>0b101.01p-3</c>
/// (and <c>P</c>), as Natural's "A" format writes them. An exponent with no digits isn't one: <c>1e</c> is 1 then the name
/// <c>e</c>, and <c>1e+</c> is 1, <c>e</c>, <c>+</c>. Hex digits include e, so <c>0x1e5</c> is 485. Underscores go only
/// between digits. A comma never groups digits.</para>
/// <para>A hex or binary fraction must have its p exponent: <c>0x1.8</c> is an error, because <c>0x1.8e2</c> would
/// otherwise be a hex fraction to someone who meant 180. Natural would read it; Calc doesn't.</para>
/// <para>Words: <c>mod</c>, <c>rem</c>, <c>xor</c> and <c>to</c> are operators. π, τ and φ are names on their own, so
/// <c>2πr</c> is 2, π, r.</para>
/// </remarks>
public static class Lexer
{
    public static IReadOnlyList<Token> Lex(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var tokens = new List<Token>();
        int i = 0;
        while (true)
        {
            while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
            if (i == source.Length)
            {
                tokens.Add(new Token(TokenKind.End, "", new TextSpan(i, i)));
                return tokens;
            }

            int start = i;
            char c = source[i];
            TokenKind kind;
            if (IsDigit(c) || (c == '.' && i + 1 < source.Length && IsDigit(source[i + 1])))
            {
                i = ScanNumber(source, start);
                kind = TokenKind.Number;
            }
            else if (IsConstantLetter(c))
            {
                i++;
                kind = TokenKind.Identifier;
            }
            else if (IsNameStart(c))
            {
                i++;
                while (i < source.Length && IsNamePart(source[i])) i++;
                kind = Keyword(source.AsSpan(start, i - start));
            }
            else
            {
                (kind, int length) = Symbol(source, i);
                i += length;
            }
            tokens.Add(new Token(kind, Slice(source, start, i), new TextSpan(start, i)));
        }
    }

    private static (TokenKind Kind, int Length) Symbol(string s, int i)
    {
        char c = s[i];
        char next = i + 1 < s.Length ? s[i + 1] : '\0';
        switch (c)
        {
            case '+': return (TokenKind.Plus, 1);
            case '-' or '−': return (TokenKind.Minus, 1);
            case '*' when next == '*': return (TokenKind.Caret, 2);
            case '*' or '×' or '·': return (TokenKind.Star, 1);
            case '/' or '÷': return (TokenKind.Slash, 1);
            case '^': return (TokenKind.Caret, 1);
            case '!': return (TokenKind.Bang, 1);
            case '%': return (TokenKind.Percent, 1);
            case '²': return (TokenKind.Squared, 1);
            case '³': return (TokenKind.Cubed, 1);
            case '√': return (TokenKind.Root, 1);
            case '~': return (TokenKind.Tilde, 1);
            case '&': return (TokenKind.Ampersand, 1);
            case '|': return (TokenKind.Pipe, 1);
            case '<' when next == '<': return (TokenKind.ShiftLeft, 2);
            case '>' when next == '>': return (TokenKind.ShiftRight, 2);
            case '(': return (TokenKind.LeftParen, 1);
            case ')': return (TokenKind.RightParen, 1);
            case ',': return (TokenKind.Comma, 1);
            case '=': return (TokenKind.Equals, 1);
            case '°': return (TokenKind.Degree, 1);
            case '#': return (TokenKind.Hash, 1);
            case '.': throw Error(i, i + 1, "A point needs a digit right after it.");
        }

        int width = Rune.TryGetRuneAt(s, i, out Rune rune) ? rune.Utf16SequenceLength : 1;
        throw Error(i, i + width, $"Unexpected character {Describe(s, i)}.");
    }

    /// <summary>Scans the number starting at <paramref name="start"/>, which is a digit, or a point before a digit, and returns where it ends.</summary>
    private static int ScanNumber(string s, int start)
    {
        (int end, bool exponent) = s[start] == '0' && start + 1 < s.Length && Radix(s[start + 1]) is int radix
            ? ScanPrefixed(s, start, radix)
            : ScanDecimal(s, start);

        // Nothing above stops at a point it could have taken, so one here is a second point, or one after the exponent.
        if (end < s.Length && s[end] == '.')
            throw Error(start, end + 1, exponent ? "An exponent can't have a point." : "A number has one point at most.");
        return end;
    }

    private static (int End, bool Exponent) ScanDecimal(string s, int start)
    {
        int i = ScanDigits(s, start, 10);
        if (i < s.Length && s[i] == '.') i = ScanDigits(s, i + 1, 10);
        int end = ScanExponent(s, i, "eE");
        return (end, end > i);
    }

    private static (int End, bool Exponent) ScanPrefixed(string s, int start, int radix)
    {
        string prefix = s.Substring(start, 2);
        int i = ScanDigits(s, start + 2, radix);
        bool whole = i > start + 2;
        if (radix == 8)
        {
            if (!whole) throw Error(start, start + 2, $"{prefix} needs octal digits after it.");
            if (i < s.Length && s[i] == '.') throw Error(start, i + 1, "Octal numbers are whole.");
            return (i, false);
        }

        bool point = i < s.Length && s[i] == '.';
        bool fraction = false;
        if (point)
        {
            int f = i + 1;
            i = ScanDigits(s, f, radix);
            fraction = i > f;
        }
        string name = radix == 16 ? "hex" : "binary";
        if (!whole && !fraction) throw Error(start, i, $"{prefix} needs {name} digits after it.");

        int end = ScanExponent(s, i, "pP");
        if (point && end == i)
        {
            string example = radix == 16 ? "0x1.8p0" : "0b1.1p0";
            throw Error(start, i, $"A {name} fraction needs a p exponent, such as {example}.");
        }
        return (end, end > i);
    }

    /// <summary>An exponent, if one is there: a marker, an optional sign, then decimal digits. Without digits it isn't one.</summary>
    private static int ScanExponent(string s, int i, string markers)
    {
        if (i >= s.Length || markers.IndexOf(s[i]) < 0) return i;
        int j = i + 1;
        if (j < s.Length && s[j] is '+' or '-') j++;
        return j < s.Length && IsDigit(s[j]) ? ScanDigits(s, j, 10) : i;
    }

    /// <summary>
    /// Digits of the radix, with underscores between them, from <paramref name="i"/>; returns where they stop, which is
    /// <paramref name="i"/> when there are none. An underscore not between two digits, or a decimal digit the radix
    /// doesn't have, is an error.
    /// </summary>
    private static int ScanDigits(string s, int i, int radix)
    {
        int first = i;
        while (i < s.Length)
        {
            char c = s[i];
            if (DigitValue(c) < radix)
            {
                i++;
            }
            else if (c == '_')
            {
                if (i == first || i + 1 >= s.Length || DigitValue(s[i + 1]) >= radix)
                    throw Error(i, i + 1, "An underscore goes between two digits.");
                i++;
            }
            else if (IsDigit(c))
            {
                throw Error(i, i + 1, $"'{c}' isn't {(radix == 2 ? "a binary" : "an octal")} digit.");
            }
            else
            {
                break;
            }
        }
        return i;
    }

    private static TokenKind Keyword(ReadOnlySpan<char> word) => word switch
    {
        "mod" => TokenKind.Mod,
        "rem" => TokenKind.Rem,
        "xor" => TokenKind.Xor,
        "to" => TokenKind.To,
        _ => TokenKind.Identifier,
    };

    private static int? Radix(char c) => c switch
    {
        'x' or 'X' => 16,
        'b' or 'B' => 2,
        'o' or 'O' => 8,
        _ => null,
    };

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    /// <summary>A character's value as a digit in any radix up to 16, or 99 when it isn't one.</summary>
    private static int DigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 99,
    };

    private static bool IsConstantLetter(char c) => c is 'π' or 'τ' or 'φ';

    private static bool IsNameStart(char c) => c == '_' || (char.IsLetter(c) && !IsConstantLetter(c));

    private static bool IsNamePart(char c) => IsNameStart(c) || IsDigit(c);

    /// <summary>The character at <paramref name="i"/> in quotes, or as U+XXXX when quoting it would show nothing useful.</summary>
    private static string Describe(string s, int i)
    {
        if (!Rune.TryGetRuneAt(s, i, out Rune rune)) return $"U+{(int)s[i]:X4}"; // a lone surrogate
        bool visible = Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark);
        return visible ? $"'{rune}'" : $"U+{rune.Value:X4}";
    }

    private static readonly string[] Ascii = Enumerable.Range(0, 128).Select(c => ((char)c).ToString()).ToArray();

    /// <summary>A token's text. Single ASCII characters share one string each, so a long flat sum doesn't allocate per token.</summary>
    private static string Slice(string s, int start, int end) =>
        end - start == 1 && s[start] < 128 ? Ascii[s[start]] : s.Substring(start, end - start);

    private static CalcError Error(int start, int end, string message) =>
        new(ErrorKind.Syntax, message, new TextSpan(start, end));
}
