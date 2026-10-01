namespace Portfolio.SharedKernel.Application;

/// <summary>Why a write could not be committed (ai/DATABASE.md §7.1, ai/BUSINESS.md §5.3).</summary>
internal enum PersistenceConflictKind
{
    /// <summary>The aggregate changed since it was read: its <c>version</c> no longer matches (optimistic concurrency).</summary>
    ConcurrentUpdate = 0,

    /// <summary>A unique constraint rejected the row: a concurrent request created the same business key first.</summary>
    DuplicateRecord = 1,
}
