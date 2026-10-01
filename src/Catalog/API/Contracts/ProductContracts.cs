using System.ComponentModel.DataAnnotations;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.Catalog.API.Contracts;

/// <summary>Lifecycle status of a product. An open set: clients must tolerate new values.</summary>
internal enum ProductStatusContract
{
    /// <summary>Created but not yet sellable.</summary>
    Draft,

    /// <summary>Visible and sellable.</summary>
    Active,

    /// <summary>Permanently withdrawn.</summary>
    Discontinued,
}

/// <summary>Request to add a product in <c>draft</c> status (BR-CAT-001, BR-CAT-002, BR-CAT-004).</summary>
/// <param name="Name">Display name, 3–200 characters.</param>
/// <param name="Sku">Stock-keeping unit, unique among active products (BR-CAT-005).</param>
/// <param name="Price">Sell price; must be greater than zero.</param>
/// <param name="Description">Optional description, up to 2000 characters.</param>
internal sealed record CreateProductRequest(
    [Required, StringLength(200, MinimumLength = 3)] string Name,
    [
        Required,
        RegularExpression(ContractPatterns.Sku, MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds)
    ]
        string Sku,
    [Required] MoneyRequest Price,
    [StringLength(2000)] string? Description = null
);

/// <summary>
/// Partial update (JSON Merge Patch semantics): omitted members are left unchanged, an explicit
/// <c>null</c> description clears it. Status, price, and identity cannot be changed here.
/// </summary>
/// <param name="Name">New display name, 3–200 characters.</param>
/// <param name="Description">New description, up to 2000 characters; <c>null</c> clears it.</param>
internal sealed record UpdateProductRequest(
    [StringLength(200, MinimumLength = 3)] string? Name = null,
    [StringLength(2000)] string? Description = null
);

/// <summary>Request to change the sell price (BR-CAT-002).</summary>
/// <param name="Price">New price; must be greater than zero.</param>
internal sealed record ChangeProductPriceRequest([Required] MoneyRequest Price);

/// <summary>Product as seen in a collection.</summary>
/// <param name="Id">Product identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Sku">Stock-keeping unit.</param>
/// <param name="Price">Current sell price.</param>
/// <param name="Status">Lifecycle status.</param>
internal sealed record ProductSummaryResponse(
    Guid Id,
    string Name,
    string Sku,
    MoneyResponse Price,
    ProductStatusContract Status
);

/// <summary>Full product resource.</summary>
/// <param name="Id">Product identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Description, omitted when absent.</param>
/// <param name="Sku">Stock-keeping unit.</param>
/// <param name="Price">Current sell price.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="Version">Resource version; also returned as the <c>ETag</c> header.</param>
/// <param name="AllowedActions">Actions the lifecycle currently allows, such as <c>activate</c>.</param>
/// <param name="CreatedAt">UTC creation instant.</param>
/// <param name="UpdatedAt">UTC instant of the last change.</param>
internal sealed record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    string Sku,
    MoneyResponse Price,
    ProductStatusContract Status,
    int Version,
    IReadOnlyList<string> AllowedActions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
