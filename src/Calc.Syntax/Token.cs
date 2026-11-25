namespace Calc.Syntax;

public enum TokenKind
{
    /// <summary>A number, kept as its raw text, underscores and all: decimal, or 0x, 0b or 0o.</summary>
    Number,
    /// <summary>A name: a letter or underscore, then letters, digits and underscores. π, τ and φ are names on their own.</summary>
    Identifier,

    Plus,
    /// <summary>- or − (U+2212).</summary>
    Minus,
    /// <summary>*, × or ·.</summary>
    Star,
    /// <summary>/ or ÷.</summary>
    Slash,
    /// <summary>^ or **.</summary>
    Caret,
    Bang,
    Percent,
    /// <summary>², postfix.</summary>
    Squared,
    /// <summary>³, postfix.</summary>
    Cubed,
    /// <summary>√, prefix.</summary>
    Root,
    Tilde,
    Ampersand,
    Pipe,
    ShiftLeft,
    ShiftRight,

    /// <summary>The word "mod".</summary>
    Mod,
    /// <summary>The word "rem".</summary>
    Rem,
    /// <summary>The word "xor".</summary>
    Xor,
    /// <summary>The word "to", reserved for conversions (F1, H10).</summary>
    To,

    LeftParen,
    RightParen,
    Comma,
    Equals,

    /// <summary>°, reserved for angles (W3).</summary>
    Degree,
    /// <summary>#, reserved.</summary>
    Hash,

    /// <summary>The end of the text: always the last token, with an empty span.</summary>
    End,
}

/// <summary>One token: its kind, its text exactly as written, and where it was.</summary>
public readonly record struct Token(TokenKind Kind, string Text, TextSpan Span);
