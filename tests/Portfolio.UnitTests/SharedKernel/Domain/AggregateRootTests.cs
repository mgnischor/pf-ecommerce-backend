using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.SharedKernel.Domain;

public sealed class AggregateVersionTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_start_at_the_initial_version()
    {
        var aggregate = new TestAggregate(Guid.CreateVersion7(), _clock);

        aggregate.Version.ShouldBe(AggregateRoot.InitialVersion);
    }

    [Fact]
    public void Should_advance_the_version_exactly_once_per_state_change()
    {
        var aggregate = new TestAggregate(Guid.CreateVersion7(), _clock);

        aggregate.Touch(_clock);

        aggregate.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
    }

    [Fact]
    public void Should_advance_the_version_when_logically_deleted_and_when_restored()
    {
        var aggregate = new TestAggregate(Guid.CreateVersion7(), _clock);

        aggregate.Delete(_clock);
        aggregate.Undelete(_clock);

        aggregate.Version.ShouldBe(AggregateRoot.InitialVersion + 2);
    }

    [Fact]
    public void Should_not_advance_the_version_when_an_event_is_raised()
    {
        var aggregate = new TestAggregate(Guid.CreateVersion7(), _clock);

        aggregate.Raise(new TestHappened(Guid.CreateVersion7(), aggregate.Id, aggregate.Version, _clock.GetUtcNow()));

        aggregate.Version.ShouldBe(AggregateRoot.InitialVersion);
    }
}

public sealed class AggregateDomainEventTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_collect_raised_events_in_order()
    {
        var aggregate = new TestAggregate(Guid.CreateVersion7(), _clock);
        var first = new TestHappened(Guid.CreateVersion7(), aggregate.Id, 1, _clock.GetUtcNow());
        var second = new TestHappened(Guid.CreateVersion7(), aggregate.Id, 2, _clock.GetUtcNow());

        aggregate.Raise(first);
        aggregate.Raise(second);

        aggregate.DomainEvents.ShouldBe([first, second]);
    }

    [Fact]
    public void Should_clear_events_after_they_are_published()
    {
        var aggregate = new TestAggregate(Guid.CreateVersion7(), _clock);
        aggregate.Raise(new TestHappened(Guid.CreateVersion7(), aggregate.Id, 1, _clock.GetUtcNow()));

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Should_reject_a_null_event()
    {
        var aggregate = new TestAggregate(Guid.CreateVersion7(), _clock);

        // Justification for the suppression: the test passes null on purpose to verify the guard clause.
        Should.Throw<ArgumentNullException>(() => aggregate.Raise(null!));
    }
}
