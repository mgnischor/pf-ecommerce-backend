using System.Globalization;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;
using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Application;

/// <summary>
/// Lists the shipments of an order (BR-SHP-004) with keyset pagination: oldest first, the identifier as tie-breaker, a
/// signed cursor bound to the order and the customer it was issued for, at most <see cref="MaxLimit"/> per page. An order
/// the context does not know, or that belongs to another customer, answers <c>404</c> exactly alike, so an order's
/// existence is not revealed; an order of the caller that has no shipment yet answers an empty page.
/// </summary>
internal sealed class ListShipmentsHandler(
    IOrderReferenceRepository references,
    IShipmentRepository shipments,
    IPageCursorCodec cursors
)
{
    /// <summary>Default page size.</summary>
    public const int DefaultLimit = 20;

    /// <summary>Largest page size; larger requests are clamped.</summary>
    public const int MaxLimit = 100;

    private const string CursorPurpose = "shipping.shipments.v1";
    private const char PositionSeparator = '|';
    private const int GuidLength = 36;

    /// <summary>Executes the query.</summary>
    /// <param name="query">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of shipments, or the reason the request cannot be answered.</returns>
    public async Task<Result<ShipmentPage>> HandleAsync(ListShipmentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await references.FindAsync(query.OrderId, cancellationToken);
        if (order is null || order.CustomerId != query.CustomerId)
        {
            return Result<ShipmentPage>.Failure(ShipmentErrors.OrderNotFound);
        }

        var purpose = PurposeOf(query.OrderId, query.CustomerId);
        ShipmentSeekPosition? after = null;
        if (query.Cursor is not null && !TryReadPosition(purpose, query.Cursor, out after))
        {
            return Result<ShipmentPage>.Failure(ShipmentErrors.CursorInvalid);
        }

        var limit = Math.Clamp(query.Limit, 1, MaxLimit);

        // One extra row tells whether another page exists without a second query.
        var found = await shipments.ListByOrderAsync(
            query.OrderId,
            query.CustomerId,
            limit + 1,
            after,
            cancellationToken
        );

        var hasMore = found.Count > limit;
        var page = found.Take(limit).ToList();
        var next = hasMore ? cursors.Protect(purpose, WritePosition(page[^1])) : null;

        return Result<ShipmentPage>.Success(new ShipmentPage([.. page.Select(ShipmentMapping.ToView)], next, hasMore));
    }

    private static string PurposeOf(Guid orderId, Guid customerId) =>
        string.Join(
            PositionSeparator,
            CursorPurpose,
            orderId.ToString("D", CultureInfo.InvariantCulture),
            customerId.ToString("D", CultureInfo.InvariantCulture)
        );

    private static string WritePosition(Shipment last) =>
        last.Id.ToString("D", CultureInfo.InvariantCulture)
        + PositionSeparator
        + last.CreatedAt.ToString("O", CultureInfo.InvariantCulture);

    private bool TryReadPosition(string purpose, string cursor, out ShipmentSeekPosition? after)
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
            || !DateTimeOffset.TryParseExact(
                payload[(GuidLength + 1)..],
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAt
            )
        )
        {
            return false;
        }

        after = new ShipmentSeekPosition(id, createdAt);
        return true;
    }
}
