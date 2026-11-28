namespace Calc.Protocol;

/// <summary>The rounding modes, Protocol's own, since Protocol has no Natural. The Engine maps them to Natural's.</summary>
public enum Rounding { Nearest, TowardZero, Up, Down }

/// <summary>The angle unit for trigonometry (W3). Degrees, as the window starts.</summary>
public enum AngleUnit { Degrees, Radians, Gradians }

/// <summary>
/// What every result rounds to: a precision in bits, or an IEEE format's exponent bits and precision when
/// <see cref="ExponentBits"/> is set. Written as Lossless writes a float's target: "256", or "11/53".
/// </summary>
public sealed record TargetSpec(int Precision, int? ExponentBits = null)
{
    public static TargetSpec Bits(int bits) => new(bits);

    public static TargetSpec Format(int exponentBits, int precision) => new(precision, exponentBits);

    public bool IsFormat => ExponentBits is not null;

    public override string ToString() => ExponentBits is int e ? $"{e}/{Precision}" : $"{Precision}";
}

/// <summary>How far exact values go before they're floats: B7's budgets, as settings. The defaults are B7's.</summary>
public sealed record BudgetSpec(long IntegerBits = 1L << 24, long RatioBits = 1L << 14, double MultiplyWork = 4294967296.0);

/// <summary>
/// Everything an evaluation depends on besides its text, carried on every request and stored on every history line:
/// the target, the rounding mode, whether integers and fractions stay exact, the angle unit and the budgets. It keys
/// every worker cache, so later sessions add fields here: G4 an interval flag, F2 a word size, H2 a definitions hash.
/// </summary>
public sealed record EvalContext(TargetSpec Target, Rounding Mode = Rounding.Nearest, bool Exact = true,
                                 AngleUnit Angle = AngleUnit.Degrees, BudgetSpec? Budgets = null)
{
    /// <summary>256 bits, to nearest, exact, degrees: how the window starts.</summary>
    public static EvalContext Default { get; } = new(TargetSpec.Bits(256));
}
