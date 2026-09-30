namespace Portfolio.SharedKernel.Application;

/// <summary>
/// Commits the changes staged by the repositories of one use case in a single transaction
/// (one aggregate per transaction, ai/ARCHITECTURE.md §4.1).
/// Defined in the Application layer; implemented by each context's <c>DbContext</c> in Infrastructure.
/// </summary>
internal interface IUnitOfWork
{
    /// <summary>Persists the staged changes atomically.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of persisted state entries.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
