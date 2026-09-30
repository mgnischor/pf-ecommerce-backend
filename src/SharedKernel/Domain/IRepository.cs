namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Persistence abstraction for aggregate roots.
/// Defined in the domain; implemented in Infrastructure.
/// </summary>
/// <typeparam name="TAggregate">Aggregate root type.</typeparam>
public interface IRepository<TAggregate>
    where TAggregate : AggregateRoot
{
    /// <summary>Loads an aggregate by identity, or <c>null</c> when not found or deleted.</summary>
    /// <param name="id">Aggregate identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages a new aggregate for insertion.</summary>
    /// <param name="aggregate">Aggregate to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);

    /// <summary>Stages an existing aggregate for update.</summary>
    /// <param name="aggregate">Aggregate to update.</param>
    void Update(TAggregate aggregate);

    /// <summary>Stages an aggregate for logical deletion.</summary>
    /// <param name="aggregate">Aggregate to remove.</param>
    void Remove(TAggregate aggregate);
}
