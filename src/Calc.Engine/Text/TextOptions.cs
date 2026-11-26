namespace Calc.Engine.Text;

public enum Notation
{
    /// <summary>Positional or scientific, whichever G picks: 0.333…, 1.23E+021.</summary>
    Auto,
    /// <summary>Always d.ddd…E±xxx, trailing zeros dropped.</summary>
    Scientific,
    /// <summary>Scientific with the exponent a multiple of 3: 333.333…E-003.</summary>
    Engineering,
    /// <summary>Positional, with no exponent, unless that would run past a sane length; then scientific.</summary>
    Fixed,
    /// <summary>
    /// Scientific with every guaranteed digit, trailing zeros kept: fixed width and no noise, so a right-aligned column
    /// reads like a bar graph, the way Matthew likes them.
    /// </summary>
    Aligned,
}

/// <summary>How a result is written.</summary>
/// <param name="Separator">Put between groups of three digits, or null for none.</param>
/// <param name="GroupFraction">Group the digits after the point too, from the point outward.</param>
public sealed record TextOptions(Notation Notation = Notation.Auto, string? Separator = null, bool GroupFraction = false)
{
    public static readonly TextOptions Default = new();
}
