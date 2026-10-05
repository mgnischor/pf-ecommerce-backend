namespace Portfolio.Catalog.Application;

/// <summary>Request to logically delete a product (BR-CAT-007).</summary>
/// <param name="ProductId">Product identifier.</param>
/// <param name="IdempotencyKey">Client key: replaying it after the deletion succeeds again and deletes nothing more (BR-CAT-008).</param>
/// <param name="ExpectedVersion">Version the client read (<c>If-Match</c>), or <c>null</c> when the header was malformed.</param>
internal sealed record DeleteProductCommand(Guid ProductId, string IdempotencyKey, int? ExpectedVersion);
