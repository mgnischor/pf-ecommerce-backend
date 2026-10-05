using System.Globalization;
using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Application;

/// <summary>
/// Lists the caller's orders with keyset pagination (BR-ORD-006): only their own, a stable order with the identifier as
/// tie-breaker, a signed cursor bound to the customer, the sort and the filters it was issued for, and at most
/// <see cref="MaxLimit"/> per page.
/// </summary>
internal sealed class ListOrdersHandler(IOrderRepository orders, IPageCursorCodec cursors)
{
    /// <summary>Default page size.</summary>
    public const int DefaultLimit = 20;

    /// <summary>Largest page size; larger requests are clamped.</summary>
    public const int MaxLimit = 100;

    private const string CursorPurpose = "ordering.orders.v1";
    private const char PositionSeparator = '|';
    private const int GuidLength = 36;

    /// <summary>Executes the query.</summary>
    /// <param name="query">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of orders, or the reason the request is malformed.</returns>
    public async Task<Result<OrderPage>> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TryParseSort(query.Sort, out var field, out var descending))
        {
            return Result<OrderPage>.Failure(OrderErrors.SortFieldNotAllowed);
        }

        var status = query.Status?.ToDomain();
        var placedFrom = query.CreatedFrom is { } date
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : (DateTimeOffset?)null;
        var purpose = PurposeOf(query.CustomerId, field, descending, status, placedFrom);

        OrderSeekPosition? after = null;
        if (query.Cursor is not null && !TryReadPosition(purpose, query.Cursor, field, out after))
        {
            return Result<OrderPage>.Failure(OrderErrors.CursorInvalid);
        }

        var limit = Math.Clamp(query.Limit, 1, MaxLimit);

        // One extra row tells whether another page exists without a second query.
        var found = await orders.ListAsync(
            new OrderListCriteria(query.CustomerId, field, descending, limit + 1, status, placedFrom, after),
            cancellationToken
        );

        var hasMore = found.Count > limit;
        var page = found.Take(limit).ToList();
        var next = hasMore ? cursors.Protect(purpose, WritePosition(page[^1], field)) : null;

        return Result<OrderPage>.Success(new OrderPage([.. page.Select(OrderMapping.ToSummary)], next, hasMore));
    }

    private static bool TryParseSort(string? sort, out OrderSortField field, out bool descending)
    {
        field = OrderSortField.PlacedAt;
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
            case "placedAt":
                field = OrderSortField.PlacedAt;
                return true;
            case "total":
                field = OrderSortField.Total;
                return true;
            default:
                return false;
        }
    }

    // The customer is part of the purpose, so a cursor can never be replayed by another customer.
    private static string PurposeOf(
        Guid customerId,
        OrderSortField field,
        bool descending,
        OrderStatus? status,
        DateTimeOffset? placedFrom
    ) =>
        string.Join(
            PositionSeparator,
            CursorPurpose,
            customerId.ToString("D", CultureInfo.InvariantCulture),
            field,
            descending ? "desc" : "asc",
            status?.ToString() ?? "any",
            placedFrom?.ToString("O", CultureInfo.InvariantCulture) ?? "any"
        );

    private static string WritePosition(Order last, OrderSortField field)
    {
        var value =
            field == OrderSortField.Total
                ? last.Total.Amount.ToString(CultureInfo.InvariantCulture)
                : last.PlacedAt.ToString("O", CultureInfo.InvariantCulture);

        return last.Id.ToString("D", CultureInfo.InvariantCulture) + PositionSeparator + value;
    }

    private bool TryReadPosition(string purpose, string cursor, OrderSortField field, out OrderSeekPosition? after)
    {
        after = null;

        if (!cursors.TryUnprotect(purpose, cursor, out var payload))
        {
            return false;
        }

        if (
            payload.Length <= GuidLength
            || payload[GuidLength] != PositionSeparator
            || !Guid.TryParseExact(payload.AsSpan(0, GuidLength), "D", out var id)
        )
        {
            return false;
        }

        var value = payload[(GuidLength + 1)..];
        switch (field)
        {
            case OrderSortField.Total
                when decimal.TryParse(
                    value,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var amount
                ):
                after = new OrderSeekPosition(id, null, amount);
                return true;
            case OrderSortField.PlacedAt
                when DateTimeOffset.TryParseExact(
                    value,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var placedAt
                ):
                after = new OrderSeekPosition(id, placedAt, null);
                return true;
            default:
                return false;
        }
    }
}
