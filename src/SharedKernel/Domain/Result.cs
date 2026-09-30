namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Machine-readable domain error, aligned with the RFC 9457 error contract
/// (<c>code</c>, <c>message</c>, <c>field</c>, <c>ruleId</c>).
/// </summary>
/// <param name="Code">Stable machine-readable error code for client handling.</param>
/// <param name="Message">Human-readable error message.</param>
/// <param name="Field">Field or property that failed validation, if any.</param>
/// <param name="RuleId">Violated business rule identifier (e.g. BR-ORD-001), if any.</param>
public sealed record Error(string Code, string Message, string? Field = null, string? RuleId = null)
{
    /// <summary>Generic validation failure.</summary>
    public static Error Validation(string code, string message, string? field = null, string? ruleId = null) =>
        new(code, message, field, ruleId);

    /// <summary>Requested resource was not found.</summary>
    public static Error NotFound(string code, string message) => new(code, message);

    /// <summary>State-transition or concurrency conflict.</summary>
    public static Error Conflict(string code, string message, string? ruleId = null) =>
        new(code, message, null, ruleId);
}

/// <summary>
/// Represents the outcome of a domain operation without using exceptions
/// for expected business failures.
/// </summary>
public sealed class Result
{
    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Failure details. <c>null</c> when <see cref="IsSuccess"/> is true.</summary>
    public Error? Error { get; }

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">Failure details. Must not be null.</param>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, error);
    }
}

/// <summary>
/// Represents the outcome of a domain operation that produces a value.
/// </summary>
/// <typeparam name="T">Type of the success value.</typeparam>
public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, Error? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Success value. Default when <see cref="IsFailure"/> is true.</summary>
    public T? Value { get; }

    /// <summary>Failure details. <c>null</c> when <see cref="IsSuccess"/> is true.</summary>
    public Error? Error { get; }

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">Success value. Must not be null.</param>
    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(true, value, null);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">Failure details. Must not be null.</param>
    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, default, error);
    }
}
