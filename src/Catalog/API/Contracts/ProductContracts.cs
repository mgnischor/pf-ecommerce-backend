using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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
/// <c>null</c> description clears it, and an explicit <c>null</c> name is rejected because a name is required.
/// Status, price, and identity cannot be changed here.
/// </summary>
/// <remarks>
/// Whether a member was sent cannot be read from its value (<c>null</c> is both "omitted" and "clear"), so the
/// setters record it: the JSON deserializer calls a setter only for members present in the body.
/// </remarks>
internal sealed record UpdateProductRequest
{
    private readonly string? _name;
    private readonly string? _description;

    /// <summary>New display name, 3–200 characters.</summary>
    [StringLength(200, MinimumLength = 3)]
    public string? Name
    {
        get => _name;
        init
        {
            _name = value;
            NameSpecified = true;
        }
    }

    /// <summary>New description, up to 2000 characters; <c>null</c> clears it.</summary>
    [StringLength(2000)]
    public string? Description
    {
        get => _description;
        init
        {
            _description = value;
            DescriptionSpecified = true;
        }
    }

    /// <summary>Whether the request named <see cref="Name"/>.</summary>
    [JsonIgnore]
    public bool NameSpecified { get; private init; }

    /// <summary>Whether the request named <see cref="Description"/>.</summary>
    [JsonIgnore]
    public bool DescriptionSpecified { get; private init; }
}

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
    string Sku,
    MoneyResponse Price,
    ProductStatusContract Status,
    int Version,
    IReadOnlyList<string> AllowedActions,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
