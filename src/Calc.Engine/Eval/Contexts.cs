using Calc.Engine.Text;
using Calc.Engine.Values;
using Calc.Protocol;
using Natural;

namespace Calc.Engine.Eval;

/// <summary>
/// Protocol's records, which know no Natural, to the Engine's types (B9's brief): a <see cref="TargetSpec"/> to a
/// <see cref="Target"/>, a <see cref="Rounding"/> to a <see cref="RoundingMode"/>, and so on.
/// </summary>
public static class Contexts
{
    /// <exception cref="ArgumentException">Not a precision or format Natural has: a precision of 0, say.</exception>
    public static Target TargetOf(TargetSpec target) =>
        target.ExponentBits is int exponentBits ? Target.Of(new IeeeFormat(exponentBits, target.Precision)) : Target.Bits(target.Precision);

    public static TargetSpec SpecOf(Target target) =>
        target.Format is IeeeFormat f ? TargetSpec.Format(f.ExponentBits, f.Precision) : TargetSpec.Bits(target.Precision);

    public static RoundingMode ModeOf(Rounding mode) => mode switch
    {
        Rounding.Nearest => RoundingMode.ToNearestEven,
        Rounding.TowardZero => RoundingMode.TowardZero,
        Rounding.Up => RoundingMode.TowardPositive,
        Rounding.Down => RoundingMode.TowardNegative,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "No such rounding mode."),
    };

    public static ExactBudgets BudgetsOf(BudgetSpec? budgets) =>
        budgets is null ? ExactBudgets.Default : new ExactBudgets(budgets.IntegerBits, budgets.RatioBits, budgets.MultiplyWork);

    public static TextOptions OptionsOf(TextStyle? style) => style is null
        ? TextOptions.Default
        : new TextOptions(style.Notation switch
        {
            NotationStyle.Auto => Notation.Auto,
            NotationStyle.Scientific => Notation.Scientific,
            NotationStyle.Engineering => Notation.Engineering,
            NotationStyle.Fixed => Notation.Fixed,
            NotationStyle.Aligned => Notation.Aligned,
            _ => throw new ArgumentOutOfRangeException(nameof(style), style.Notation, "No such notation."),
        }, style.Separator, style.GroupFraction);

    /// <summary>The flags' names, as the wire carries them: ["Inexact", "Overflow"].</summary>
    public static IReadOnlyList<string> FlagsOf(IeeeFlags flags) =>
        Enum.GetValues<IeeeFlags>().Where(f => f != IeeeFlags.None && (flags & f) != 0).Select(f => f.ToString()).ToArray();
}
