using System.Globalization;

namespace Portfolio.SharedKernel.API;

/// <summary>
/// The <c>ETag</c> of a stateful resource is its aggregate version as a quoted string (<c>"3"</c>), and
/// <c>If-Match</c> echoes it back (ai/API_CONTRACTS.md §2). Opaque to clients, derived server-side only.
/// </summary>
internal static class ETags
{
    private const string WeakPrefix = "W/";

    /// <summary>Formats the entity tag of a resource version.</summary>
    /// <param name="version">Aggregate version.</param>
    public static string ForVersion(int version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary>
    /// Reads the version out of an <c>If-Match</c> value (strong or weak form of a tag issued by
    /// <see cref="ForVersion"/>). Lists and <c>*</c> are not supported: a write needs one exact version.
    /// </summary>
    /// <param name="ifMatch">Raw header value.</param>
    /// <param name="version">The version, when the value is a well-formed tag.</param>
    /// <returns><c>true</c> when <paramref name="ifMatch"/> is a well-formed tag.</returns>
    public static bool TryParseVersion(string? ifMatch, out int version)
    {
        version = 0;
        var tag = ifMatch?.Trim();
        if (tag is null)
        {
            return false;
        }

        if (tag.StartsWith(WeakPrefix, StringComparison.Ordinal))
        {
            tag = tag[WeakPrefix.Length..];
        }

        return tag.Length > 2
            && tag[0] == '"'
            && tag[^1] == '"'
            && int.TryParse(tag[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version)
            && version > 0;
    }
}
