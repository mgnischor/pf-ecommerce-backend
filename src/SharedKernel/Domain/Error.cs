namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Machine-readable domain failure, aligned with the RFC 9457 error contract
/// (<c>code</c>, <c>field</c>, <c>ruleId</c>, <c>params</c>).
/// Carries no prose: user-facing text is resolved from <paramref name="Code"/> and
/// <paramref name="Parameters"/> at the API boundary (ai/API_CONTRACTS.md §7).
/// </summary>
/// <param name="Type">Failure category, used to pick the HTTP status.</param>
/// <param name="Code">Stable machine-readable error code for client handling.</param>
/// <param name="Field">Field or property that failed validation, if any.</param>
/// <param name="RuleId">Violated business rule identifier (e.g. BR-ORD-001), if any.</param>
/// <param name="Parameters">Values needed to render the localized message (limits, current state, ...).</param>
internal sealed record Error(
    ErrorType Type,
    string Code,
    string? Field = null,
    string? RuleId = null,
    IReadOnlyDictionary<string, object>? Parameters = null
)
{
    /// <summary>Creates a validation failure.</summary>
    /// <param name="code">Stable error code.</param>
    /// <param name="field">Field that failed validation.</param>
    /// <param name="ruleId">Violated business rule identifier.</param>
    /// <param name="parameters">Message parameters.</param>
    public static Error Validation(
        string code,
        string? field = null,
        string? ruleId = null,
        IReadOnlyDictionary<string, object>? parameters = null
    ) => new(ErrorType.Validation, code, field, ruleId, parameters);

    /// <summary>Creates a failure for a resource that does not exist.</summary>
    /// <param name="code">Stable error code.</param>
    /// <param name="parameters">Message parameters.</param>
    public static Error NotFound(string code, IReadOnlyDictionary<string, object>? parameters = null) =>
        new(ErrorType.NotFound, code, Parameters: parameters);

    /// <summary>Creates a state-transition, uniqueness, or concurrency conflict.</summary>
    /// <param name="code">Stable error code.</param>
    /// <param name="ruleId">Violated business rule identifier.</param>
    /// <param name="parameters">Message parameters.</param>
    public static Error Conflict(
        string code,
        string? ruleId = null,
        IReadOnlyDictionary<string, object>? parameters = null
    ) => new(ErrorType.Conflict, code, RuleId: ruleId, Parameters: parameters);

    /// <summary>Value equality, including the message parameters (dictionaries compare by content).</summary>
    /// <param name="other">Error to compare with.</param>
    public bool Equals(Error? other) =>
        other is not null
        && Type == other.Type
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && string.Equals(Field, other.Field, StringComparison.Ordinal)
        && string.Equals(RuleId, other.RuleId, StringComparison.Ordinal)
        && ParametersEqual(Parameters, other.Parameters);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Type, Code, Field, RuleId);

    private static bool ParametersEqual(
        IReadOnlyDictionary<string, object>? left,
        IReadOnlyDictionary<string, object>? right
    )
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));
    }
}
