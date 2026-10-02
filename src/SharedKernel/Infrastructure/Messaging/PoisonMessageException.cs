namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// The message can never be handled, however often it is retried (malformed body, a rule it violates by construction):
/// the host dead-letters it at once instead of burning the delivery limit. The message text is a fixed description and
/// never carries payload data, because it reaches logs.
/// </summary>
public sealed class PoisonMessageException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PoisonMessageException() { }

    /// <summary>Creates the exception with a fixed, payload-free description.</summary>
    /// <param name="message">What is wrong, without data from the message.</param>
    public PoisonMessageException(string message)
        : base(message) { }

    /// <summary>Creates the exception around the failure that made the message unreadable.</summary>
    /// <param name="message">What is wrong, without data from the message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public PoisonMessageException(string message, Exception innerException)
        : base(message, innerException) { }
}
