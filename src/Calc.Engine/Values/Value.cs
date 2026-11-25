using System.Globalization;
using Natural;

namespace Calc.Engine.Values;

/// <summary>A result. Floats now; B7 adds exact integers and fractions as further kinds.</summary>
public abstract record Value;

/// <summary>A float, rounded to the evaluation's target.</summary>
public sealed record FloatValue(ApFloat Number) : Value
{
    /// <summary>
    /// Plain text until B6's ResultText: the shortest that reads back, within rule 10's limit for "R", and
    /// scientific past it.
    /// </summary>
    public override string ToString() =>
        Number.ToString(Number.Precision <= 2048 ? "R" : "E99", CultureInfo.InvariantCulture);
}
