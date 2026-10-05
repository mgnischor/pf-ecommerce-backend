namespace Portfolio.SharedKernel.Application;

/// <summary>
/// Turns a keyset position into the opaque cursor a client sends back, and checks it on the way in
/// (ai/API_CONTRACTS.md §6: cursors are signed, never a raw key). A cursor is bound to the <c>purpose</c> it was
/// issued for, so one issued by a collection is rejected by any other.
/// </summary>
internal interface IPageCursorCodec
{
    /// <summary>Signs <paramref name="payload"/> into a URL-safe cursor.</summary>
    /// <param name="purpose">Stable name of the collection (and query shape) the cursor belongs to.</param>
    /// <param name="payload">Keyset position; opaque to the codec.</param>
    string Protect(string purpose, string payload);

    /// <summary>Verifies a cursor and recovers its payload.</summary>
    /// <param name="purpose">The purpose the cursor must have been issued for.</param>
    /// <param name="cursor">The cursor received from the client.</param>
    /// <param name="payload">The payload, when the cursor is authentic.</param>
    /// <returns><c>false</c> when the cursor is malformed, forged, or issued for another purpose.</returns>
    bool TryUnprotect(string purpose, string cursor, out string payload);
}
