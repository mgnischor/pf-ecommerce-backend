using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Ordering.Domain;

[Trait("Rule", "BR-ORD-003")]
public sealed class OrderLifecycleTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private Order Order(OrderStatus status = OrderStatus.AwaitingPayment) =>
        OrderBuilder.New().WithStatus(status).Build(_clock);

    [Fact]
    public void Should_follow_the_happy_path_to_delivery_recording_each_instant_and_event()
    {
        var order = Order();
        var steps = new List<Type>();

        _clock.Advance(TimeSpan.FromMinutes(1));
        order.MarkPaid(_clock).IsSuccess.ShouldBeTrue();
        var paidAt = order.PaidAt;
        _clock.Advance(TimeSpan.FromMinutes(1));
        order.MarkShipped(_clock).IsSuccess.ShouldBeTrue();
        _clock.Advance(TimeSpan.FromMinutes(1));
        order.MarkDelivered(_clock).IsSuccess.ShouldBeTrue();
        steps.AddRange(order.DomainEvents.Select(domainEvent => domainEvent.GetType()));

        order.Status.ShouldBe(OrderStatus.Delivered);
        paidAt.ShouldBe(TestClock.Start.AddMinutes(1));
        order.ShippedAt.ShouldBe(TestClock.Start.AddMinutes(2));
        order.DeliveredAt.ShouldBe(TestClock.Start.AddMinutes(3));
        order.Version.ShouldBe(AggregateRoot.InitialVersion + 3);
        steps.ShouldBe([typeof(OrderPaid), typeof(OrderShipped), typeof(OrderDelivered)]);
    }

    [Fact]
    public void Should_raise_each_transition_event_with_the_new_version_the_customer_and_the_number()
    {
        var order = Order();
        order.MarkPaid(_clock);

        var paid = order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderPaid>();

        paid.AggregateId.ShouldBe(order.Id);
        paid.AggregateVersion.ShouldBe(order.Version);
        paid.CustomerId.ShouldBe(order.CustomerId);
        paid.Number.ShouldBe(order.Number);
    }

    [Theory]
    [InlineData("AwaitingPayment", "Shipped")]
    [InlineData("AwaitingPayment", "Delivered")]
    [InlineData("Paid", "Paid")]
    [InlineData("Paid", "Delivered")]
    [InlineData("Shipped", "Paid")]
    [InlineData("Shipped", "Shipped")]
    [InlineData("Delivered", "Paid")]
    [InlineData("Delivered", "Shipped")]
    [InlineData("Delivered", "Delivered")]
    [InlineData("Cancelled", "Paid")]
    [InlineData("Cancelled", "Shipped")]
    [InlineData("Cancelled", "Delivered")]
    public void Should_refuse_a_transition_the_state_machine_does_not_have_and_change_nothing(
        string fromName,
        string toName
    )
    {
        var order = Order(Enum.Parse<OrderStatus>(fromName));
        var versionBefore = order.Version;

        var result = toName switch
        {
            "Paid" => order.MarkPaid(_clock),
            "Shipped" => order.MarkShipped(_clock),
            _ => order.MarkDelivered(_clock),
        };

        var error = result.ShouldFail();
        error.Code.ShouldBe("ORDER_INVALID_STATUS_TRANSITION");
        error.Type.ShouldBe(ErrorType.Conflict);
        error.RuleId.ShouldBe("BR-ORD-003");
        error.ShouldHaveParameters().ShouldBe(new Dictionary<string, object> { ["from"] = fromName, ["to"] = toName });
        order.Status.ShouldBe(Enum.Parse<OrderStatus>(fromName));
        order.Version.ShouldBe(versionBefore);
        order.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Should_keep_the_timestamps_of_steps_that_did_not_happen_empty()
    {
        var order = Order(OrderStatus.Paid);

        (order.ShippedAt, order.DeliveredAt, order.CancelledAt).ShouldBe((null, null, null));
    }
}

[Trait("Rule", "BR-ORD-004")]
public sealed class OrderCancellationTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private Order Order(OrderStatus status = OrderStatus.AwaitingPayment) =>
        OrderBuilder.New().WithStatus(status).Build(_clock);

    [Theory]
    [InlineData("AwaitingPayment", false)]
    [InlineData("Paid", true)]
    public void Should_cancel_an_order_that_was_not_shipped_and_say_whether_it_had_been_paid(
        string statusName,
        bool wasPaid
    )
    {
        var order = Order(Enum.Parse<OrderStatus>(statusName));
        _clock.Advance(TimeSpan.FromHours(1));

        var result = order.Cancel("changedMind", "  Comprei sem querer ", "key-0001", _clock);

        result.IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancelledAt.ShouldBe(TestClock.Start.AddHours(1));
        order.CancellationReason.ShouldBe("changedMind");
        order.CancellationNote.ShouldBe("Comprei sem querer");
        order.CancellationKey.ShouldBe("key-0001");
        var cancelled = order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderCancelled>();
        (cancelled.ReasonCode, cancelled.WasPaid, cancelled.CustomerId, cancelled.Number).ShouldBe(
            ("changedMind", wasPaid, order.CustomerId, order.Number)
        );
        cancelled.AggregateVersion.ShouldBe(order.Version);
    }

    [Fact]
    public void Should_never_put_the_free_text_note_in_the_event()
    {
        var order = Order();

        order.Cancel("changedMind", "dados pessoais aqui", "key-0001", _clock);

        order.DomainEvents.OfType<OrderCancelled>().Single().ToString().ShouldNotContain("dados pessoais");
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("Delivered")]
    [InlineData("Cancelled")]
    public void Should_refuse_to_cancel_an_order_that_is_shipped_delivered_or_already_cancelled(string statusName)
    {
        var order = Order(Enum.Parse<OrderStatus>(statusName));
        var versionBefore = order.Version;

        var result = order.Cancel("changedMind", null, "key-0002", _clock);

        result.ShouldFail().Code.ShouldBe("ORDER_INVALID_STATUS_TRANSITION");
        result.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
        order.Status.ShouldBe(Enum.Parse<OrderStatus>(statusName));
        order.Version.ShouldBe(versionBefore);
        order.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, "ORDER_REASON_REQUIRED")]
    [InlineData("", "ORDER_REASON_REQUIRED")]
    [InlineData("   ", "ORDER_REASON_REQUIRED")]
    [InlineData("a", "ORDER_REASON_INVALID")]
    [InlineData("1abc", "ORDER_REASON_INVALID")]
    [InlineData("changed mind", "ORDER_REASON_INVALID")]
    [InlineData("changed-mind", "ORDER_REASON_INVALID")]
    [InlineData("mudança", "ORDER_REASON_INVALID")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "ORDER_REASON_INVALID")]
    public void Should_reject_a_reason_that_is_missing_or_not_a_short_identifier_and_change_nothing(
        string? reason,
        string code
    )
    {
        var order = Order();

        var result = order.Cancel(reason, null, "key-0001", _clock);

        result.ShouldFail().Code.ShouldBe(code);
        order.Status.ShouldBe(OrderStatus.AwaitingPayment);
        order.CancellationKey.ShouldBeNull();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("changedMind")]
    [InlineData("out_of_stock_2")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Should_accept_a_reason_of_two_to_thirty_two_letters_digits_or_underscores(string reason)
    {
        Order().Cancel(reason, null, "key-0001", _clock).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Should_reject_a_note_above_the_limit_and_accept_one_at_the_limit()
    {
        var tooLong = Order()
            .Cancel(
                "changedMind",
                new string('x', Portfolio.Ordering.Domain.Order.MaxNoteLength + 1),
                "key-0001",
                _clock
            );
        var atLimit = Order()
            .Cancel("changedMind", new string('x', Portfolio.Ordering.Domain.Order.MaxNoteLength), "key-0001", _clock);

        tooLong.ShouldFail().Code.ShouldBe("ORDER_NOTE_TOO_LONG");
        atLimit.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Should_store_a_blank_note_as_absent()
    {
        var order = Order();

        order.Cancel("changedMind", "   ", "key-0001", _clock);

        order.CancellationNote.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_refuse_to_cancel_without_an_idempotency_key(string? key)
    {
        var order = Order();

        Should.Throw<ArgumentException>(() => order.Cancel("changedMind", null, key!, _clock));

        order.Status.ShouldBe(OrderStatus.AwaitingPayment);
    }

    [Fact]
    public void Should_recognise_the_request_that_cancelled_it_and_not_a_different_one()
    {
        var order = Order();
        order.Cancel("changedMind", "  nota ", "key-0001", _clock);

        order.WasCancelledFor("changedMind", "nota").ShouldBeTrue();
        order.WasCancelledFor(" changedMind ", " nota ").ShouldBeTrue();
        order.WasCancelledFor("foundCheaper", "nota").ShouldBeFalse();
        order.WasCancelledFor("changedMind", "outra nota").ShouldBeFalse();
        order.WasCancelledFor("changedMind", null).ShouldBeFalse();
    }

    [Fact]
    public void Should_not_say_an_order_that_was_not_cancelled_was_cancelled_for_anything()
    {
        Order().WasCancelledFor("changedMind", null).ShouldBeFalse();
    }
}
