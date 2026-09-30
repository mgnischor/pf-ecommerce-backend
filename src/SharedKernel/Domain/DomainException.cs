namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Base exception for truly exceptional domain states.
/// Expected business failures use <see cref="Result"/> instead.
/// </summary>
public class DomainException : Exception
{
    /// <summary>Initializes a new domain exception.</summary>
    public DomainException() { }

    /// <summary>Initializes a new domain exception.</summary>
    /// <param name="message">Human-readable description.</param>
    public DomainException(string message)
        : base(message) { }

    /// <summary>Initializes a new domain exception with an inner cause.</summary>
    /// <param name="message">Human-readable description.</param>
    /// <param name="innerException">Underlying cause.</param>
    public DomainException(string message, Exception innerException)
        : base(message, innerException) { }
}
