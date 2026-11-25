namespace Calc.Syntax;

/// <summary>What went wrong, as the person sees it. B4's ErrorMap maps Natural's exceptions to these.</summary>
public enum ErrorKind
{
    /// <summary>The text isn't an expression.</summary>
    Syntax,
    /// <summary>Outside a function's domain, such as sqrt(-1) at bits.</summary>
    Domain,
    DivideByZero,
    /// <summary>Past the exponent range.</summary>
    Overflow,
    /// <summary>Too big to hold in memory or to print.</summary>
    TooBig,
    /// <summary>Not built yet.</summary>
    NotAvailable,
    /// <summary>Stopped by the person.</summary>
    Stopped,
    /// <summary>A bug: something Calc should have prevented.</summary>
    Internal,
}

/// <summary>The one exception Calc's own code throws for something the person did, with the stretch of text it's about.</summary>
public sealed class CalcError(ErrorKind kind, string message, TextSpan? span = null) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;

    /// <summary>The text the error is about, when there is one.</summary>
    public TextSpan? Span { get; } = span;
}
