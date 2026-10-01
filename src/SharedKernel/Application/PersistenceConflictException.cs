namespace Portfolio.SharedKernel.Application;

/// <summary>
/// Raised by a unit of work when the database refuses a commit because of a concurrent request. The use case
/// has already passed its own checks, so this is the race those checks cannot close: the API answers
/// <c>409 Conflict</c> and the client may re-read and retry. Carries no data from the failed statement.
/// </summary>
public sealed class PersistenceConflictException : Exception
{
    /// <summary>Initializes a conflict caused by a concurrent update.</summary>
    public PersistenceConflictException()
        : this(PersistenceConflictKind.ConcurrentUpdate) { }

    /// <summary>Initializes a conflict caused by a concurrent update.</summary>
    /// <param name="message">Human-readable description.</param>
    public PersistenceConflictException(string message)
        : base(message) { }

    /// <summary>Initializes a conflict caused by a concurrent update.</summary>
    /// <param name="message">Human-readable description.</param>
    /// <param name="innerException">The provider exception, kept for diagnostics only.</param>
    public PersistenceConflictException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Creates a conflict of the given kind.</summary>
    /// <param name="kind">Kind of conflict.</param>
    /// <param name="innerException">The provider exception, kept for diagnostics only.</param>
    internal PersistenceConflictException(PersistenceConflictKind kind, Exception? innerException = null)
        : base($"The write conflicts with a concurrent change ({kind}).", innerException)
    {
        Kind = kind;
    }

    /// <summary>Kind of conflict.</summary>
    internal PersistenceConflictKind Kind { get; }

    /// <summary>Stable machine-readable error code of the conflict.</summary>
    internal string Code => Kind == PersistenceConflictKind.DuplicateRecord ? "DUPLICATE_RECORD" : "CONCURRENT_UPDATE";
}
