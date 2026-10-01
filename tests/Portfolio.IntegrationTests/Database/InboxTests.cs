using Microsoft.EntityFrameworkCore;
using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// The inbox (ai/DATABASE.md §3.1, §8.2): the same message processed twice produces one side effect, because the inbox
/// row commits atomically with the consumer's own writes.
/// </summary>
public sealed class InboxTests : DatabaseTestBase
{
    private const string Consumer = "inventory.open-item-on-product-created";

    private InventoryItem Item(string sku) => InventoryItem.Open(Sku.Create(sku).Value, Clock);

    [Fact]
    public async Task Should_accept_a_message_the_first_time_and_refuse_it_after_it_committed()
    {
        var messageId = Guid.CreateVersion7();
        var first = Inventory();
        (
            await new Inbox(first, Clock).TryBeginAsync(messageId, Consumer, TestContext.Current.CancellationToken)
        ).ShouldBeTrue();
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        var redelivery = Inventory();

        (
            await new Inbox(redelivery, Clock).TryBeginAsync(messageId, Consumer, TestContext.Current.CancellationToken)
        ).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_refuse_a_second_attempt_inside_the_same_unit_of_work_before_it_commits()
    {
        var messageId = Guid.CreateVersion7();
        var context = Inventory();
        var inbox = new Inbox(context, Clock);

        (await inbox.TryBeginAsync(messageId, Consumer, TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await inbox.TryBeginAsync(messageId, Consumer, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_track_a_message_per_consumer_so_two_consumers_can_both_handle_it()
    {
        var messageId = Guid.CreateVersion7();
        var context = Inventory();
        var inbox = new Inbox(context, Clock);

        (await inbox.TryBeginAsync(messageId, "consumer.one", TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await inbox.TryBeginAsync(messageId, "consumer.two", TestContext.Current.CancellationToken)).ShouldBeTrue();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Database.ScalarAsync("SELECT count(*) FROM inventory.inbox_messages")).ShouldBe(2L);
    }

    [Fact]
    public async Task Should_apply_the_side_effect_once_when_the_same_message_is_processed_twice_in_turn()
    {
        var messageId = Guid.CreateVersion7();

        for (var delivery = 0; delivery < 2; delivery++)
        {
            var context = Inventory();
            if (
                await new Inbox(context, Clock).TryBeginAsync(
                    messageId,
                    Consumer,
                    TestContext.Current.CancellationToken
                )
            )
            {
                context.InventoryItems.Add(Item("CAF-600-PRT"));
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        (await Database.ScalarAsync("SELECT count(*) FROM inventory.inventory_items")).ShouldBe(1L);
        (await Database.ScalarAsync("SELECT count(*) FROM inventory.inbox_messages")).ShouldBe(1L);
    }

    [Fact]
    public async Task Should_apply_the_side_effect_once_when_two_deliveries_race()
    {
        var messageId = Guid.CreateVersion7();
        var left = Inventory();
        var right = Inventory();
        (
            await new Inbox(left, Clock).TryBeginAsync(messageId, Consumer, TestContext.Current.CancellationToken)
        ).ShouldBeTrue();
        (
            await new Inbox(right, Clock).TryBeginAsync(messageId, Consumer, TestContext.Current.CancellationToken)
        ).ShouldBeTrue();
        left.InventoryItems.Add(Item("LEFT-0001"));
        right.InventoryItems.Add(Item("RIGHT-0001"));

        await left.SaveChangesAsync(TestContext.Current.CancellationToken);
        var failure = await Should.ThrowAsync<PersistenceConflictException>(() =>
            right.SaveChangesAsync(TestContext.Current.CancellationToken)
        );

        failure.Code.ShouldBe("DUPLICATE_RECORD");
        // Atomic: the loser's inbox row and its side effect rolled back together.
        (await Database.StringsAsync("SELECT sku FROM inventory.inventory_items")).ShouldBe(["LEFT-0001"]);
        (await Database.ScalarAsync("SELECT count(*) FROM inventory.inbox_messages")).ShouldBe(1L);
    }

    [Fact]
    public async Task Should_reject_an_empty_or_oversized_consumer_name()
    {
        var inbox = new Inbox(Inventory(), Clock);

        await Should.ThrowAsync<ArgumentException>(() =>
            inbox.TryBeginAsync(Guid.CreateVersion7(), " ", TestContext.Current.CancellationToken)
        );
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            inbox.TryBeginAsync(
                Guid.CreateVersion7(),
                new string('x', InboxMessage.MaxConsumerLength + 1),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Should_stamp_when_the_message_was_handled_with_the_injected_clock()
    {
        var context = Inventory();
        await new Inbox(context, Clock).TryBeginAsync(
            Guid.CreateVersion7(),
            Consumer,
            TestContext.Current.CancellationToken
        );
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (
            await Inventory().InboxMessages.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)
        ).ProcessedAt.ShouldBe(TestContexts.NewClock().GetUtcNow());
    }
}
