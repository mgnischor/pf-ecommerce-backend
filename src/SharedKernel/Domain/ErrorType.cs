namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Category of a domain failure. The API boundary maps it to an HTTP status
/// (<see cref="Validation"/> → 422, <see cref="NotFound"/> → 404, <see cref="Conflict"/> → 409,
/// <see cref="Unauthorized"/> → 401, <see cref="Forbidden"/> → 403, <see cref="PreconditionFailed"/> → 412).
/// </summary>
internal enum ErrorType
{
    /// <summary>Input or state violates a constraint.</summary>
    Validation = 0,

    /// <summary>The requested resource does not exist.</summary>
    NotFound = 1,

    /// <summary>A state-transition, uniqueness, or concurrency conflict.</summary>
    Conflict = 2,

    /// <summary>The caller could not be authenticated (bad credentials, invalid or expired token).</summary>
    Unauthorized = 3,

    /// <summary>The caller is authenticated but not allowed to perform the action.</summary>
    Forbidden = 4,

    /// <summary>The caller acted on a stale or malformed version of the resource (<c>If-Match</c> did not match).</summary>
    PreconditionFailed = 5,
}
