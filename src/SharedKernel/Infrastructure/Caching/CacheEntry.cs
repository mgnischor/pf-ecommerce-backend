using System.Text.RegularExpressions;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// The definition of one kind of cached data (ai/DATABASE.md §4.2: "document which data is cached, where, with which
/// TTL, and with which invalidation trigger"). Each definition is declared once, next to the code that owns the data,
/// with its TTL and invalidation written in the summary. The key scheme is
/// <c>{prefix}:{context}:{entity}:{id}:v{version}</c>; the version is bumped whenever the cached shape changes, so an
/// old payload is never read as the new shape. An identifier is an opaque id, never personal data.
/// </summary>
/// <param name="Name">Logical name, a bounded metric dimension (<c>identity.account</c>).</param>
/// <param name="Context">Bounded context that owns the data, in lowercase.</param>
/// <param name="Entity">Kind of entity, in lowercase.</param>
/// <param name="Version">Schema version of the cached payload; starts at 1.</param>
/// <param name="Ttl">How long the value lives. Mandatory: nothing is cached without an expiry.</param>
/// <param name="AllowLocalCopy">
/// Whether an in-process copy may also be kept. Off by default: a local copy cannot be invalidated from another
/// instance, so it is only for data that tolerates being stale for its whole TTL.
/// </param>
internal sealed partial record CacheEntry(
    string Name,
    string Context,
    string Entity,
    int Version,
    TimeSpan Ttl,
    bool AllowLocalCopy = false
)
{
    /// <summary>Builds the full key of one value.</summary>
    /// <param name="prefix">Deployment prefix (<c>Valkey:KeyPrefix</c>).</param>
    /// <param name="id">Opaque identifier of the cached entity.</param>
    public string KeyFor(string prefix, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        // An identifier with ':' or other separators could collide with, or escape into, another entity's key.
        if (!Identifier().IsMatch(id))
        {
            throw new ArgumentException("A cache identifier is 1 to 128 letters, digits, '-' or '_'.", nameof(id));
        }

        return $"{prefix}:{Context}:{Entity}:{id}:v{Version}";
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Identifier();
}
