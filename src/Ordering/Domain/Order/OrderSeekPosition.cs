namespace Portfolio.Ordering.Domain;

/// <summary>
/// The keyset position after which a listing continues: the sort value of the last order of the previous page and its
/// identifier as the unique tie-breaker, so pages never duplicate or skip an order (ai/API_CONTRACTS.md §6). Only the
/// member that matches <see cref="OrderListCriteria.SortField"/> is populated.
/// </summary>
/// <param name="Id">Identifier of the last order of the previous page.</param>
/// <param name="PlacedAt">Its placement instant, when listing by placement.</param>
/// <param name="TotalAmount">Its total amount, when listing by total.</param>
internal sealed record OrderSeekPosition(Guid Id, DateTimeOffset? PlacedAt, decimal? TotalAmount);
