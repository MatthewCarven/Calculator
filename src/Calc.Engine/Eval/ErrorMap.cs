using Calc.Syntax;

namespace Calc.Engine.Eval;

/// <summary>
/// Every exception from Natural or the runtime, to the <see cref="CalcError"/> the person sees, in one place (rule 12). The
/// table is report 1's: docs/research/reports/1-natural-api-gaps.md, "Exceptions".
/// </summary>
public static class ErrorMap
{
    public static CalcError From(Exception exception, TextSpan? at) => exception switch
    {
        CalcError error => error,
        // Bad text or a bad format. The lexer checks literals first, so this is rare.
        FormatException => new(ErrorKind.Syntax, "That isn't a number Calc can read.", at),
        // An exponent past a long; NaN or ∞ to an integer; printing at the very top of the range.
        OverflowException => new(ErrorKind.Overflow, "The result is too large: its exponent doesn't fit.", at),
        // ApInt division or ModPow by zero. Before ArithmeticException, which it derives from.
        DivideByZeroException => new(ErrorKind.DivideByZero, "Division by zero.", at),
        // ApInt.Sqrt of a negative, ApFloat.Sign of NaN.
        ArithmeticException => new(ErrorKind.Domain, "That's outside what the operation is defined for.", at),
        OutOfMemoryException => new(ErrorKind.TooBig, "That's too big to hold in memory.", at),
        InsufficientExecutionStackException => new(ErrorKind.TooBig, "This is nested too deeply to work through.", at),
        // The soft stop (B9).
        OperationCanceledException => new(ErrorKind.Stopped, "Stopped.", at),
        // ArgumentException and its kin, InvalidOperationException, NotSupportedException, and anything else: Calc
        // should have prevented it.
        _ => new(ErrorKind.Internal, $"Something went wrong inside Calc: {exception.GetType().Name}: {exception.Message}", at),
    };
}
