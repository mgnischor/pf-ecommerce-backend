namespace Portfolio.SharedKernel.Domain;

/// <summary>Builds the parameter bag of an <see cref="Error"/>.</summary>
internal static class ErrorParameters
{
    /// <summary>Creates an ordinal, read-only parameter dictionary.</summary>
    /// <param name="pairs">Parameter name and value pairs.</param>
    public static IReadOnlyDictionary<string, object> Of(params (string Name, object Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
}
