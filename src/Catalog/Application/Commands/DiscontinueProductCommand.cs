namespace Portfolio.Catalog.Application;

/// <summary>Request to permanently withdraw an active product (BR-CAT-003).</summary>
/// <param name="ProductId">Product identifier.</param>
internal sealed record DiscontinueProductCommand(Guid ProductId);
