using Calc.Engine.Values;
using Calc.Syntax;
using Natural;

namespace Calc.Engine.Eval;

/// <summary>
/// The number kinds' half of evaluation. The <see cref="Evaluator"/> walks the tree and knows no number types; this does
/// the arithmetic. Each operation gets the text it's about, for its errors. Floats now; exact values in B7.
/// </summary>
public interface IArithmetic
{
    /// <summary>The flags raised so far, IEEE's five, sticky.</summary>
    IeeeFlags Flags { get; }

    /// <summary>A number token's raw text, as the lexer took it, rounded once to the target.</summary>
    Value Literal(string text, TextSpan at);

    /// <summary>A value from outside the evaluation, such as Ans. A float must already be at this target (rule 14).</summary>
    Value Import(Value value, TextSpan at);

    Value Add(Value a, Value b, TextSpan at);
    Value Subtract(Value a, Value b, TextSpan at);
    Value Multiply(Value a, Value b, TextSpan at);
    Value Divide(Value a, Value b, TextSpan at);
    Value Negate(Value a, TextSpan at);
    Value Power(Value a, Value b, TextSpan at);
    Value Mod(Value a, Value b, TextSpan at);
    Value Rem(Value a, Value b, TextSpan at);
    Value Percent(Value a, TextSpan at);
    Value Factorial(Value a, TextSpan at);
    Value And(Value a, Value b, TextSpan at);
    Value Or(Value a, Value b, TextSpan at);
    Value Xor(Value a, Value b, TextSpan at);
    Value ShiftLeft(Value a, Value b, TextSpan at);
    Value ShiftRight(Value a, Value b, TextSpan at);
    Value Not(Value a, TextSpan at);

    /// <summary>-1, 0 or 1, or null when unordered, as when either is NaN.</summary>
    int? Compare(Value a, Value b);

    Value Call(string name, IReadOnlyList<Value> arguments, TextSpan at);
}
