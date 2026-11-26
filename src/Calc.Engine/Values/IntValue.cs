using Natural;

namespace Calc.Engine.Values;

/// <summary>An exact integer.</summary>
public sealed record IntValue(ApInt Number) : Value
{
    /// <summary>Raw decimal, for tests, logs and <c>--raw</c>.</summary>
    public override string ToString() => Number.ToString();
}
