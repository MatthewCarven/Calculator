using System.Globalization;
using Natural;

namespace Calc.Engine.Values;

/// <summary>A result: a <see cref="FloatValue"/>, or an exact <see cref="IntValue"/> or <see cref="RatioValue"/>.</summary>
public abstract record Value;

/// <summary>A float, rounded to the evaluation's target.</summary>
public sealed record FloatValue(ApFloat Number) : Value
{
    /// <summary>
    /// The exact answer was past a budget, so this float stands in for it: the chip says "float, exact too large".
    /// Nothing about it is a refusal.
    /// </summary>
    public bool ExactTooLarge { get; init; }

    /// <summary>
    /// Raw text, for tests, logs and <c>--raw</c>: every digit that reads back ("R", within rule 10's limit), and scientific
    /// past it. What people see is <see cref="Text.ResultText"/>, which shows only the digits the precision guarantees.
    /// </summary>
    public override string ToString() =>
        Number.ToString(Number.Precision <= 2048 ? "R" : "E99", CultureInfo.InvariantCulture);
}
