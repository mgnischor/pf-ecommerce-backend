using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Domain;

/// <summary>
/// IANA time zone of a customer (BR-CUS-005), used to render the local time of what the platform sends them; every
/// stored timestamp stays UTC. The identifier is checked for shape (<c>Area/Location</c>, an IANA area, or <c>UTC</c>),
/// not against a time zone database: the runtime image ships none (it is chiseled), and the domain must not depend on
/// the host anyway. A syntactically valid name that no database knows is rendered as UTC by the consumers of the value.
/// Immutable, compared by value.
/// </summary>
internal sealed record CustomerTimeZone
{
    /// <summary>Maximum length of the identifier.</summary>
    public const int MaxLength = 64;

    private const int MaxSegmentLength = 32;

    private static readonly string[] Areas =
    [
        "Africa",
        "America",
        "Antarctica",
        "Arctic",
        "Asia",
        "Atlantic",
        "Australia",
        "Etc",
        "Europe",
        "Indian",
        "Pacific",
    ];

    /// <summary>The IANA identifier.</summary>
    public string Value { get; }

    private CustomerTimeZone(string value)
    {
        Value = value;
    }

    /// <summary>Time zone of a customer who gave none.</summary>
    public static CustomerTimeZone Default { get; } = new("America/Sao_Paulo");

    /// <summary>Validates a raw identifier.</summary>
    /// <param name="value">Raw identifier, case-sensitive like the IANA names.</param>
    /// <returns>The time zone, or the violated BR-CUS-005 error.</returns>
    public static Result<CustomerTimeZone> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<CustomerTimeZone>.Failure(CustomerErrors.TimeZoneRequired);
        }

        var candidate = value.Trim();

        return IsIanaShaped(candidate)
            ? Result<CustomerTimeZone>.Success(new CustomerTimeZone(candidate))
            : Result<CustomerTimeZone>.Failure(CustomerErrors.TimeZoneInvalid);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsIanaShaped(string candidate)
    {
        if (candidate.Length > MaxLength)
        {
            return false;
        }

        if (string.Equals(candidate, "UTC", StringComparison.Ordinal))
        {
            return true;
        }

        var segments = candidate.Split('/');

        return segments.Length is 2 or 3
            && Areas.Contains(segments[0], StringComparer.Ordinal)
            && segments.Skip(1).All(IsLocation);
    }

    private static bool IsLocation(string segment) =>
        segment.Length is > 0 and <= MaxSegmentLength
        && char.IsAsciiLetter(segment[0])
        && segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '+');
}
