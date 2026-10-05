namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Secret that signs pagination cursors (<c>Pagination:CursorKey</c>). Inject it from the secrets manager; it is
/// never committed. Development may run without it (an ephemeral key is used); every instance of a deployment
/// must share the same key, or a cursor issued by one instance is refused by another.
/// </summary>
internal sealed class PageCursorOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Pagination";

    /// <summary>Minimum key length in bytes.</summary>
    public const int MinKeyBytes = 32;

    /// <summary>Base64 key of at least <see cref="MinKeyBytes"/> random bytes.</summary>
    public string? CursorKey { get; set; }
}
