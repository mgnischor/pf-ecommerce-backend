using Portfolio.SharedKernel.Domain;

namespace Portfolio.UnitTests.Support;

/// <summary>Assertion helpers that turn a failed <see cref="Result"/> into its non-null <see cref="Error"/>.</summary>
internal static class ResultAssertions
{
    /// <summary>Asserts that the result failed and returns its error.</summary>
    public static Error ShouldFail(this Result result) => result.Error.ShouldNotBeNull();

    /// <summary>Asserts that the result failed and returns its error.</summary>
    public static Error ShouldFail<T>(this Result<T> result)
        where T : notnull => result.Error.ShouldNotBeNull();

    /// <summary>Asserts that the error carries message parameters and returns them.</summary>
    public static IReadOnlyDictionary<string, object> ShouldHaveParameters(this Error error) =>
        error.Parameters.ShouldNotBeNull();
}
