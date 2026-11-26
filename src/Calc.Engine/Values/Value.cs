using System.Globalization;
using Natural;

namespace Calc.Engine.Values;

/// <summary>A result. Floats now; B7 adds exact integers and fractions as further kinds.</summary>
public abstract record Value;

/// <summary>A float, rounded to the evaluation's target.</summary>
public sealed record FloatValue(ApFloat Number) : Value
{
    /// <summary>
    /// Raw text, for tests, logs and <c>--raw</c>: every digit that reads back ("R", within rule 10's limit), and scientific
    /// past it. What people see is <see cref="Text.ResultText"/>, which shows only the digits the precision guarantees.
    /// </summary>
    public override string ToString() =>
        Number.ToString(Number.Precision <= 2048 ? "R" : "E99", CultureInfo.InvariantCulture);
}
