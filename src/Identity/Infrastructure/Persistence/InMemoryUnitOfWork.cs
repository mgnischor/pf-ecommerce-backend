using Portfolio.SharedKernel.Application;

namespace Portfolio.Identity.Infrastructure;

/// <summary>No-op unit of work for the in-memory stores, whose changes apply immediately. Replaced by the <c>DbContext</c>.</summary>
internal sealed class InMemoryUnitOfWork : IUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}
