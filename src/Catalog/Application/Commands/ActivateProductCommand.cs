namespace Portfolio.Catalog.Application;

/// <summary>Request to make a draft product sellable (BR-CAT-003).</summary>
/// <param name="ProductId">Product identifier.</param>
/// <param name="ExpectedVersion">Version the client read (<c>If-Match</c>), or <c>null</c> when the header was malformed.</param>
internal sealed record ActivateProductCommand(Guid ProductId, int? ExpectedVersion);
