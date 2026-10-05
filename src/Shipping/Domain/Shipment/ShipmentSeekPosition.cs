namespace Portfolio.Shipping.Domain;

/// <summary>
/// The keyset position after which a listing of an order's shipments continues: the creation instant of the last
/// shipment of the previous page and its identifier as the unique tie-breaker (ai/API_CONTRACTS.md §6).
/// </summary>
/// <param name="Id">Identifier of the last shipment of the previous page.</param>
/// <param name="CreatedAt">Its creation instant.</param>
internal sealed record ShipmentSeekPosition(Guid Id, DateTimeOffset CreatedAt);
