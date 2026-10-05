using System.Globalization;
using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>
/// Lists products with keyset pagination (BR-CAT-006): a stable order with the identifier as tie-breaker, a signed
/// cursor bound to the sort and filters it was issued for, and a page size of at most <see cref="MaxLimit"/>.
/// The public sees only active products; catalog staff see every status.
/// </summary>
internal sealed class ListProductsHandler(IProductRepository products, IPageCursorCodec cursors)
{
    /// <summary>Default page size.</summary>
    public const int DefaultLimit = 20;

    /// <summary>Largest page size; larger requests are clamped.</summary>
    public const int MaxLimit = 100;

    private const string CursorPurpose = "catalog.products.v1";
    private const char PositionSeparator = '|';

    /// <summary>Executes the query.</summary>
    /// <param name="query">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of products, or the reason the request is malformed.</returns>
    public async Task<Result<ProductPage>> HandleAsync(ListProductsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TryParseSort(query.Sort, out var field, out var descending))
        {
            return Result<ProductPage>.Failure(ProductErrors.SortFieldNotAllowed);
        }

        var status = EffectiveStatus(query, out var nothingVisible);
        if (nothingVisible)
        {
            return Result<ProductPage>.Success(new ProductPage([], null, false));
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var purpose = PurposeOf(field, descending, status, search);

        ProductSeekPosition? after = null;
        if (query.Cursor is not null && !TryReadPosition(purpose, query.Cursor, field, out after))
        {
            return Result<ProductPage>.Failure(ProductErrors.CursorInvalid);
        }

        var limit = Math.Clamp(query.Limit, 1, MaxLimit);

        // One extra row tells whether another page exists without a second query.
        var found = await products.ListAsync(
            new ProductListCriteria(field, descending, limit + 1, search, status, after),
            cancellationToken
        );

        var hasMore = found.Count > limit;
        var page = found.Take(limit).ToList();
        var next = hasMore ? cursors.Protect(purpose, WritePosition(page[^1], field)) : null;

        return Result<ProductPage>.Success(new ProductPage([.. page.Select(ProductMapping.ToSummary)], next, hasMore));
    }

    private static bool TryParseSort(string? sort, out ProductSortField field, out bool descending)
    {
        field = ProductSortField.CreatedAt;
        descending = true;

        if (string.IsNullOrWhiteSpace(sort))
        {
            return true;
        }

        descending = sort.StartsWith('-');
        var name = descending ? sort[1..] : sort;

        // An explicit allowlist: Enum.TryParse would also accept numbers and any casing.
        switch (name)
        {
            case "name":
                field = ProductSortField.Name;
                return true;
            case "price":
                field = ProductSortField.Price;
                return true;
            case "createdAt":
                field = ProductSortField.CreatedAt;
                return true;
            default:
                return false;
        }
    }

    private static ProductStatus? EffectiveStatus(ListProductsQuery query, out bool nothingVisible)
    {
        nothingVisible = false;
        var requested = query.Status?.ToDomain();

        if (query.CanSeeAllStatuses)
        {
            return requested;
        }

        // The public sees active products only, so asking for another status finds nothing.
        nothingVisible = requested is not null and not ProductStatus.Active;
        return ProductStatus.Active;
    }

    private static string PurposeOf(ProductSortField field, bool descending, ProductStatus? status, string? search) =>
        string.Join(
            PositionSeparator,
            CursorPurpose,
            field,
            descending ? "desc" : "asc",
            status?.ToString() ?? "any",
            search ?? string.Empty
        );

    private static string WritePosition(Product last, ProductSortField field)
    {
        var value = field switch
        {
            ProductSortField.Name => last.Name,
            ProductSortField.Price => last.Price.Amount.ToString(CultureInfo.InvariantCulture),
            _ => last.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        };

        return last.Id.ToString("D", CultureInfo.InvariantCulture) + PositionSeparator + value;
    }

    private bool TryReadPosition(string purpose, string cursor, ProductSortField field, out ProductSeekPosition? after)
    {
        after = null;

        if (!cursors.TryUnprotect(purpose, cursor, out var payload))
        {
            return false;
        }

        // "D" format is 36 characters, followed by the separator and the sort value.
        const int guidLength = 36;
        if (
            payload.Length <= guidLength
            || payload[guidLength] != PositionSeparator
            || !Guid.TryParseExact(payload.AsSpan(0, guidLength), "D", out var id)
        )
        {
            return false;
        }

        var value = payload[(guidLength + 1)..];
        switch (field)
        {
            case ProductSortField.Name:
                after = new ProductSeekPosition(id, value, null, null);
                return true;
            case ProductSortField.Price
                when decimal.TryParse(
                    value,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var amount
                ):
                after = new ProductSeekPosition(id, null, amount, null);
                return true;
            case ProductSortField.CreatedAt
                when DateTimeOffset.TryParseExact(
                    value,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var createdAt
                ):
                after = new ProductSeekPosition(id, null, null, createdAt);
                return true;
            default:
                return false;
        }
    }
}
