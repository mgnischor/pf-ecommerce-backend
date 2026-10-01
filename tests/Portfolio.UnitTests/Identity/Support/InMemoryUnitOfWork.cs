using Portfolio.SharedKernel.Application;

namespace Portfolio.UnitTests.Identity.Support;

/// <summary>No-op unit of work for the in-memory fakes, whose changes apply immediately.</summary>
internal sealed class InMemoryUnitOfWork : IUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}
