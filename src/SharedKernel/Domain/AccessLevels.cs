namespace Portfolio.SharedKernel.Domain;

/// <summary>Conversions between <see cref="AccessLevel"/> and its stable wire name (token claim, API contract).</summary>
internal static class AccessLevels
{
    /// <summary>Returns the stable lowercase wire name of a level, for example <c>manager</c>.</summary>
    /// <param name="level">A defined level.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is not a defined value.</exception>
    public static string ToWireName(this AccessLevel level) =>
        level switch
        {
            AccessLevel.Public => "public",
            AccessLevel.Collaborator => "collaborator",
            AccessLevel.Manager => "manager",
            AccessLevel.Administrator => "administrator",
            AccessLevel.Developer => "developer",
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Undefined access level."),
        };

    /// <summary>
    /// Parses a wire name strictly: exact lowercase names only, never numbers or other casings, so a
    /// tampered or unexpected value can never be interpreted as a level.
    /// </summary>
    /// <param name="value">Candidate wire name.</param>
    /// <param name="level">The parsed level when the method returns <c>true</c>.</param>
    public static bool TryParseWireName(string? value, out AccessLevel level)
    {
        (var parsed, var ok) = value switch
        {
            "public" => (AccessLevel.Public, true),
            "collaborator" => (AccessLevel.Collaborator, true),
            "manager" => (AccessLevel.Manager, true),
            "administrator" => (AccessLevel.Administrator, true),
            "developer" => (AccessLevel.Developer, true),
            _ => (AccessLevel.Public, false),
        };

        level = parsed;
        return ok;
    }

    /// <summary>Whether <paramref name="level"/> is at least <paramref name="required"/>.</summary>
    /// <param name="level">Level held by the caller.</param>
    /// <param name="required">Minimum level needed.</param>
    public static bool Satisfies(this AccessLevel level, AccessLevel required) => level >= required;
}
