using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.SharedKernel.Domain;

public sealed class EntityTraceabilityTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_stamp_creation_and_update_with_the_injected_clock_when_created()
    {
        var entity = new TestEntity(Guid.CreateVersion7(), _clock);

        entity.CreatedAt.ShouldBe(TestClock.Start);
        entity.UpdatedAt.ShouldBe(TestClock.Start);
        entity.DeletedAt.ShouldBeNull();
        entity.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public void Should_reject_an_empty_identifier()
    {
        Should.Throw<ArgumentException>(() => new TestEntity(Guid.Empty, _clock));
    }

    [Fact]
    public void Should_refresh_the_update_timestamp_but_keep_the_creation_timestamp_when_touched()
    {
        var entity = new TestEntity(Guid.CreateVersion7(), _clock);
        _clock.Advance(TimeSpan.FromMinutes(5));

        entity.Touch(_clock);

        entity.CreatedAt.ShouldBe(TestClock.Start);
        entity.UpdatedAt.ShouldBe(TestClock.Start.AddMinutes(5));
    }

    [Fact]
    public void Should_record_the_logical_deletion_instant_without_removing_the_entity()
    {
        var entity = new TestEntity(Guid.CreateVersion7(), _clock);
        _clock.Advance(TimeSpan.FromHours(1));

        entity.Delete(_clock);

        entity.IsDeleted.ShouldBeTrue();
        entity.DeletedAt.ShouldBe(TestClock.Start.AddHours(1));
        entity.UpdatedAt.ShouldBe(TestClock.Start.AddHours(1));
    }

    [Fact]
    public void Should_clear_the_deletion_instant_when_restored()
    {
        var entity = new TestEntity(Guid.CreateVersion7(), _clock);
        entity.Delete(_clock);
        _clock.Advance(TimeSpan.FromHours(2));

        entity.Undelete(_clock);

        entity.IsDeleted.ShouldBeFalse();
        entity.DeletedAt.ShouldBeNull();
        entity.UpdatedAt.ShouldBe(TestClock.Start.AddHours(2));
    }
}

public sealed class EntityIdentityTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_be_equal_when_type_and_identifier_match()
    {
        var id = Guid.CreateVersion7();

        new TestEntity(id, _clock).ShouldBe(new TestEntity(id, _clock));
    }

    [Fact]
    public void Should_have_the_same_hash_code_when_equal()
    {
        var id = Guid.CreateVersion7();

        new TestEntity(id, _clock).GetHashCode().ShouldBe(new TestEntity(id, _clock).GetHashCode());
    }

    [Fact]
    public void Should_not_be_equal_when_identifiers_differ()
    {
        new TestEntity(Guid.CreateVersion7(), _clock).ShouldNotBe(new TestEntity(Guid.CreateVersion7(), _clock));
    }

    [Fact]
    public void Should_not_be_equal_when_runtime_types_differ_even_with_the_same_identifier()
    {
        var id = Guid.CreateVersion7();

        new TestEntity(id, _clock).Equals(new OtherTestEntity(id, _clock)).ShouldBeFalse();
    }

    [Fact]
    public void Should_be_equal_to_itself()
    {
        var entity = new TestEntity(Guid.CreateVersion7(), _clock);

        entity.Equals(entity).ShouldBeTrue();
    }

    [Fact]
    public void Should_not_be_equal_to_null_or_to_an_unrelated_object()
    {
        var entity = new TestEntity(Guid.CreateVersion7(), _clock);

        entity.Equals(null).ShouldBeFalse();
        entity.Equals("not an entity").ShouldBeFalse();
    }
}

public sealed class EntityIdGenerationTests
{
    [Fact]
    public void Should_generate_a_version_7_identifier_stamped_with_the_injected_clock()
    {
        var clock = TestClock.Create();

        var id = Entity.NewId(clock);

        id.Version.ShouldBe(7);
        id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Should_generate_identifiers_that_sort_by_the_injected_clock()
    {
        var clock = TestClock.Create();
        var earlier = Entity.NewId(clock);
        clock.Advance(TimeSpan.FromSeconds(1));
        var later = Entity.NewId(clock);

        later.CompareTo(earlier).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Should_generate_distinct_identifiers_within_the_same_instant()
    {
        var clock = TestClock.Create();

        Entity.NewId(clock).ShouldNotBe(Entity.NewId(clock));
    }
}
