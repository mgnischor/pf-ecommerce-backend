namespace Portfolio.Catalog.Domain;

/// <summary>
/// The keyset position after which a listing continues: the sort value of the last item of the previous page and
/// its identifier as the unique tie-breaker, so pages never duplicate or skip an item (ai/API_CONTRACTS.md §6).
/// Only the member that matches <see cref="ProductListCriteria.SortField"/> is populated.
/// </summary>
/// <param name="Id">Identifier of the last item of the previous page.</param>
/// <param name="Name">Its name, when listing by name.</param>
/// <param name="PriceAmount">Its price amount, when listing by price.</param>
/// <param name="CreatedAt">Its creation instant, when listing by creation.</param>
internal sealed record ProductSeekPosition(Guid Id, string? Name, decimal? PriceAmount, DateTimeOffset? CreatedAt);
