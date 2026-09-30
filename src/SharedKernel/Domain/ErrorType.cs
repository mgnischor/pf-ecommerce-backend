namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Category of a domain failure. The API boundary maps it to an HTTP status
/// (<see cref="Validation"/> → 400/422, <see cref="NotFound"/> → 404, <see cref="Conflict"/> → 409).
/// </summary>
internal enum ErrorType
{
    /// <summary>Input or state violates a constraint.</summary>
    Validation = 0,

    /// <summary>The requested resource does not exist.</summary>
    NotFound = 1,

    /// <summary>A state-transition, uniqueness, or concurrency conflict.</summary>
    Conflict = 2,
}
