using System.Text.Json;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.SharedKernel.Infrastructure;

public sealed class OutboxMessageTests
{
    private sealed record SampleEvent(
        Guid EventId,
        Guid AggregateId,
        int AggregateVersion,
        DateTimeOffset OccurredAt,
        string Sku,
        Money Price
    ) : IDomainEvent;

    private static SampleEvent Event() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), 3, TestClock.Start, "CAF-600-PRT", new Money(189.9m, "BRL"));

    [Fact]
    public void Should_queue_an_event_under_its_own_identity_with_its_envelope()
    {
        var domainEvent = Event();

        var message = OutboxMessage.From(domainEvent, "trace-1", "cause-1");

        message.Id.ShouldBe(domainEvent.EventId);
        message.AggregateId.ShouldBe(domainEvent.AggregateId);
        message.AggregateVersion.ShouldBe(3);
        message.OccurredAt.ShouldBe(TestClock.Start);
        message.Type.ShouldBe(typeof(SampleEvent).FullName);
        message.CorrelationId.ShouldBe("trace-1");
        message.CausationId.ShouldBe("cause-1");
        message.ProcessedAt.ShouldBeNull();
        message.Attempts.ShouldBe(0);
    }

    [Fact]
    public void Should_serialize_the_payload_as_camel_case_json_with_money_as_decimal_and_currency()
    {
        var message = OutboxMessage.From(Event(), null, null);

        using var payload = JsonDocument.Parse(message.Payload);
        var root = payload.RootElement;
        root.GetProperty("sku").GetString().ShouldBe("CAF-600-PRT");
        root.GetProperty("aggregateVersion").GetInt32().ShouldBe(3);
        root.GetProperty("price").GetProperty("amount").GetDecimal().ShouldBe(189.9m);
        root.GetProperty("price").GetProperty("currency").GetString().ShouldBe("BRL");
    }

    [Fact]
    public void Should_count_an_attempt_and_hold_the_row_until_the_lease_ends()
    {
        var message = OutboxMessage.From(Event(), null, null);

        message.Lease(TestClock.Start.AddSeconds(30));

        message.Attempts.ShouldBe(1);
        message.LockedUntil.ShouldBe(TestClock.Start.AddSeconds(30));
    }

    [Fact]
    public void Should_mark_processed_and_release_the_lease_and_forget_a_previous_error()
    {
        var message = OutboxMessage.From(Event(), null, null);
        message.Lease(TestClock.Start.AddSeconds(30));
        message.MarkFailed("TimeoutException");

        message.MarkProcessed(TestClock.Start.AddSeconds(5));

        message.ProcessedAt.ShouldBe(TestClock.Start.AddSeconds(5));
        message.LockedUntil.ShouldBeNull();
        message.LastError.ShouldBeNull();
    }

    [Fact]
    public void Should_keep_only_the_error_type_and_bound_its_length()
    {
        var message = OutboxMessage.From(Event(), null, null);

        message.MarkFailed(new string('x', 500));

        message.LastError.ShouldNotBeNull().Length.ShouldBe(OutboxMessage.MaxErrorLength);
        message.LockedUntil.ShouldBeNull();
        message.ProcessedAt.ShouldBeNull();
    }
}
