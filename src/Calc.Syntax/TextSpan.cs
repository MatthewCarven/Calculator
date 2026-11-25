namespace Calc.Syntax;

/// <summary>A stretch of the source text, in UTF-16 code units: <see cref="Start"/> inclusive, <see cref="End"/> exclusive.</summary>
public readonly record struct TextSpan(int Start, int End)
{
    public int Length => End - Start;

    public override string ToString() => $"{Start}..{End}";
}
