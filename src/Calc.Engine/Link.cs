using System.Globalization;
using Natural;

namespace Calc.Engine;

/// <summary>B1's proof that Natural is linked in. B4's evaluator replaces it.</summary>
public static class Link
{
    /// <summary>1/3 at 256 bits, to the 76 digits that precision guarantees (TODO rule 9).</summary>
    public static string OneThird() => ApFloat.Divide(1, 3, 256).ToString("G76", CultureInfo.InvariantCulture);
}
