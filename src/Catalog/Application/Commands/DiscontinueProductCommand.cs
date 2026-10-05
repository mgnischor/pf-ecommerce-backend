namespace Portfolio.Catalog.Application;

/// <summary>Request to permanently withdraw an active product (BR-CAT-003).</summary>
/// <param name="ProductId">Product identifier.</param>
/// <param name="ExpectedVersion">Version the client read (<c>If-Match</c>), or <c>null</c> when the header was malformed.</param>
internal sealed record DiscontinueProductCommand(Guid ProductId, int? ExpectedVersion);
