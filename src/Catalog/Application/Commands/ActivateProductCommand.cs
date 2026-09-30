namespace Portfolio.Catalog.Application;

/// <summary>Request to make a draft product sellable (BR-CAT-003).</summary>
/// <param name="ProductId">Product identifier.</param>
internal sealed record ActivateProductCommand(Guid ProductId);
