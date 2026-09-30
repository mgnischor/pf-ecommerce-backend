using Portfolio.SharedKernel.Domain;

namespace Portfolio.UnitTests.Support;

/// <summary>Minimal entity that exposes the protected lifecycle members of <see cref="Entity"/>.</summary>
internal class TestEntity : Entity
{
    public TestEntity(Guid id, TimeProvider timeProvider)
        : base(id, timeProvider) { }

    public void Touch(TimeProvider timeProvider) => MarkUpdated(timeProvider);

    public void Delete(TimeProvider timeProvider) => MarkDeleted(timeProvider);

    public void Undelete(TimeProvider timeProvider) => Restore(timeProvider);
}

/// <summary>Entity of a different runtime type, used to check that equality is type-aware.</summary>
internal sealed class OtherTestEntity(Guid id, TimeProvider timeProvider) : TestEntity(id, timeProvider);

/// <summary>Minimal aggregate that exposes the protected members of <see cref="AggregateRoot"/>.</summary>
internal sealed class TestAggregate(Guid id, TimeProvider timeProvider) : AggregateRoot(id, timeProvider)
{
    public void Touch(TimeProvider clock) => MarkUpdated(clock);

    public void Delete(TimeProvider clock) => MarkDeleted(clock);

    public void Undelete(TimeProvider clock) => Restore(clock);

    public void Raise(IDomainEvent domainEvent) => AddDomainEvent(domainEvent);
}

/// <summary>Event used to exercise event collection.</summary>
internal sealed record TestHappened(Guid EventId, Guid AggregateId, int AggregateVersion, DateTimeOffset OccurredAt)
    : IDomainEvent;
