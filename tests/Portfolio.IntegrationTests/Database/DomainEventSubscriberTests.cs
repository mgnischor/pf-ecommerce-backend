using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// Post-commit reactions to domain events (ai/OBSERVABILITY.md §3.3): business metrics and cache invalidation run after
/// the transaction committed, never for work that rolled back, and a failing subscriber never fails the request.
/// </summary>
public sealed class DomainEventSubscriberTests : DatabaseTestBase
{
    private sealed class Recording(Action<IDomainEvent>? onEvent = null) : IDomainEventSubscriber
    {
        public List<IDomainEvent> Seen { get; } = [];

        public Task OnCommittedAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            Seen.Add(domainEvent);
            onEvent?.Invoke(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class Failing : IDomainEventSubscriber
    {
        public Task OnCommittedAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("subscriber down");
    }

    private Portfolio.Catalog.Infrastructure.CatalogDbContext CatalogWith(params IDomainEventSubscriber[] subscribers)
    {
        var context = TestContexts.Catalog(DataSource, Clock, subscribers);
        context.Database.CanConnect().ShouldBeTrue();
        return context;
    }

    [Fact]
    public async Task Should_hand_every_committed_event_to_the_subscribers_in_the_order_it_was_raised()
    {
        var subscriber = new Recording();
        await using var context = CatalogWith(subscriber);
        var product = NewProduct();
        product.Activate(Clock);
        context.Products.Add(product);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        subscriber.Seen.Select(seen => seen.GetType().Name).ShouldBe(["ProductCreated", "ProductStatusChanged"]);
    }

    [Fact]
    public async Task Should_dispatch_only_after_the_commit_so_the_row_is_already_visible()
    {
        var visibleWhenNotified = false;
        var subscriber = new Recording(_ =>
            visibleWhenNotified =
                Database.ScalarAsync("SELECT count(*) FROM catalog.products").GetAwaiter().GetResult() is 1L
        );
        await using var context = CatalogWith(subscriber);
        context.Products.Add(NewProduct());

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        visibleWhenNotified.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_dispatch_nothing_for_work_that_rolled_back()
    {
        await using (var first = CatalogWith())
        {
            first.Products.Add(NewProduct());
            await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var subscriber = new Recording();
        await using var duplicate = CatalogWith(subscriber);
        duplicate.Products.Add(NewProduct());

        await Should.ThrowAsync<PersistenceConflictException>(() =>
            duplicate.SaveChangesAsync(TestContext.Current.CancellationToken)
        );

        subscriber.Seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_not_fail_a_committed_request_when_a_subscriber_fails_and_still_run_the_others()
    {
        var after = new Recording();
        await using var context = CatalogWith(new Failing(), after);
        context.Products.Add(NewProduct());

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Database.ScalarAsync("SELECT count(*) FROM catalog.products")).ShouldBe(1L);
        after.Seen.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Should_dispatch_each_event_once_even_when_the_context_saves_again()
    {
        var subscriber = new Recording();
        await using var context = CatalogWith(subscriber);
        var product = NewProduct();
        context.Products.Add(product);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        subscriber.Seen.ShouldHaveSingleItem();
    }
}
