namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// One member of a partial update (JSON Merge Patch, RFC 7396): the client either left it out, which means "keep the
/// current value", or sent it, possibly as <c>null</c>, which means "set it to this". The default value is
/// <see cref="Unchanged"/>, so an omitted member needs no code.
/// </summary>
/// <typeparam name="T">Type of the member.</typeparam>
/// <param name="IsSet">Whether the client sent the member.</param>
/// <param name="Value">The value the client sent; meaningful only when <paramref name="IsSet"/>.</param>
internal readonly record struct Change<T>(bool IsSet, T? Value)
{
    /// <summary>The member was not part of the request.</summary>
    public static Change<T> Unchanged => default;

    /// <summary>The member was part of the request with this value (which may be <c>null</c>).</summary>
    /// <param name="value">New value.</param>
    public static Change<T> To(T? value) => new(true, value);
}
