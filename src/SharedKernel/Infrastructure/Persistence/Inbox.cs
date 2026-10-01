using Portfolio.SharedKernel.Application;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// <see cref="IInbox"/> over a context's own <c>inbox_messages</c> table. It stages the row in the context the
/// consumer commits, so the row and the consumer's side effect succeed or fail together; the composite primary
/// key closes the race between two concurrent deliveries of the same message.
/// </summary>
/// <param name="context">The context the consuming use case commits.</param>
/// <param name="timeProvider">Source of UTC time.</param>
internal sealed class Inbox(ModuleDbContext context, TimeProvider timeProvider) : IInbox
{
    /// <inheritdoc />
    public async Task<bool> TryBeginAsync(
        Guid messageId,
        string consumer,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);

        // Looks in the change tracker first, then in the table, by the composite key (consumer, message).
        if (await context.InboxMessages.FindAsync([consumer, messageId], cancellationToken) is not null)
        {
            return false;
        }

        context.InboxMessages.Add(InboxMessage.Record(messageId, consumer, timeProvider.GetUtcNow()));
        return true;
    }
}
