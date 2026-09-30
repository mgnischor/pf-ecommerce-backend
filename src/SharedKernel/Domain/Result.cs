using System.Diagnostics.CodeAnalysis;

namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Represents the outcome of a domain operation without using exceptions
/// for expected business failures.
/// </summary>
internal sealed class Result
{
    private Result(Error? error)
    {
        Error = error;
    }

    /// <summary>Whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>Whether the operation failed.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => Error is not null;

    /// <summary>Failure details. <c>null</c> when <see cref="IsSuccess"/> is true.</summary>
    public Error? Error { get; }

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">Failure details. Must not be null.</param>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }
}

/// <summary>
/// Represents the outcome of a domain operation that produces a value.
/// </summary>
/// <typeparam name="T">Type of the success value.</typeparam>
internal sealed class Result<T>
    where T : notnull
{
    private readonly T _value;

    private Result(T value)
    {
        _value = value;
    }

    private Result(Error error)
    {
        Error = error;
        // Justification for the suppression: the value is never exposed while the result is a failure;
        // Value throws in that state.
        _value = default!;
    }

    /// <summary>Whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>Whether the operation failed.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => Error is not null;

    /// <summary>Failure details. <c>null</c> when <see cref="IsSuccess"/> is true.</summary>
    public Error? Error { get; }

    /// <summary>Success value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a failure.</exception>
    public T Value =>
        IsSuccess
            ? _value
            : throw new InvalidOperationException(
                $"Cannot read the value of a failed result (error code '{Error.Code}')."
            );

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">Success value. Must not be null.</param>
    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(value);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">Failure details. Must not be null.</param>
    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(error);
    }
}
