using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>
/// Account e-mail (BR-IDN-001). Immutable, compared by value, normalized to trimmed lowercase ASCII so
/// that visually identical addresses cannot create distinct accounts.
/// </summary>
internal sealed record EmailAddress
{
    /// <summary>Maximum total length (RFC 5321).</summary>
    public const int MaxLength = 254;

    /// <summary>Maximum length of the local part (RFC 5321).</summary>
    public const int MaxLocalPartLength = 64;

    /// <summary>Normalized address.</summary>
    public string Value { get; }

    private EmailAddress(string value)
    {
        Value = value;
    }

    /// <summary>The part before the <c>@</c>.</summary>
    public string LocalPart => Value[..Value.IndexOf('@', StringComparison.Ordinal)];

    /// <summary>Validates and normalizes a raw address.</summary>
    /// <param name="value">Raw address.</param>
    /// <returns>The normalized address, or <c>EMAIL_INVALID</c>.</returns>
    public static Result<EmailAddress> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        return normalized is not null && IsValid(normalized)
            ? Result<EmailAddress>.Success(new EmailAddress(normalized))
            : Result<EmailAddress>.Failure(IdentityErrors.EmailInvalid);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsValid(string candidate)
    {
        if (candidate.Length is 0 or > MaxLength || !candidate.All(IsAllowedCharacter))
        {
            return false;
        }

        var at = candidate.IndexOf('@', StringComparison.Ordinal);
        if (at < 1 || at != candidate.LastIndexOf('@'))
        {
            return false;
        }

        var local = candidate[..at];
        var domain = candidate[(at + 1)..];

        return local.Length <= MaxLocalPartLength
            && !local.StartsWith('.')
            && !local.EndsWith('.')
            && !local.Contains("..", StringComparison.Ordinal)
            && IsValidDomain(domain);
    }

    private static bool IsValidDomain(string domain)
    {
        var labels = domain.Split('.');

        return labels.Length >= 2
            && labels.All(label => label.Length is > 0 and <= 63 && !label.StartsWith('-') && !label.EndsWith('-'))
            && labels[^1].Length >= 2
            && labels.All(label => label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    private static bool IsAllowedCharacter(char c) =>
        c is > ' ' and < (char)127
        && c is not ('"' or '(' or ')' or ',' or ':' or ';' or '<' or '>' or '[' or '\\' or ']');
}
