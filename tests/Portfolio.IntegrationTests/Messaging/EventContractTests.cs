using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Portfolio.Cart.Infrastructure;
using Portfolio.Catalog.Domain;
using Portfolio.Customers.Infrastructure;
using Portfolio.Identity.Domain;
using Portfolio.Inventory.Domain;
using Portfolio.Inventory.Infrastructure;
using Portfolio.Ordering.Domain;
using Portfolio.Ordering.Infrastructure;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.Shipping.Domain;
using Portfolio.Shipping.Infrastructure;

namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// The integration events are a published contract (ai/TESTS.md §5): every domain event, serialized the way the outbox
/// serializes it, validates against its versioned JSON Schema in <c>docs/events</c>; no event lacks a schema and no schema
/// lacks an event; and the schemas are tolerant readers, so adding a property is a compatible change.
/// </summary>
public sealed class EventContractTests
{
    private static readonly Guid Id = Guid.Parse("0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e57");
    private static readonly DateTimeOffset At = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Money Price = new(189.9m, "BRL");
    private static readonly Guid CustomerId = Guid.Parse("0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e58");
    private static readonly Guid ProductId = Guid.Parse("0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e59");
    private static readonly Guid ShipmentId = Guid.Parse("0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e60");

    private static readonly IDomainEvent[] Samples =
    [
        new ProductCreated(Id, Id, 1, At, "Cafeteira Elétrica 600ml", "CAF-600-PRT", Price),
        new ProductPriceChanged(Id, Id, 2, At, Price, new Money(199m, "BRL")),
        new ProductStatusChanged(Id, Id, 3, At, ProductStatus.Draft, ProductStatus.Active),
        new ProductDeleted(Id, Id, 4, At, "CAF-600-PRT"),
        new UserRegistered(Id, Id, 1, At, AccessLevel.Public),
        new CustomerRegistered(
            Id,
            Id,
            1,
            At,
            "ana.souza@example.com",
            "Ana Souza",
            "+5511987654321",
            "pt-BR",
            "America/Sao_Paulo"
        ),
        new UserAccessLevelChanged(Id, Id, 2, At, AccessLevel.Public, AccessLevel.Manager),
        new UserDeactivated(Id, Id, 3, At),
        new UserTokensRevoked(Id, Id, 4, At),
        new InventoryItemOpened(Id, Id, 1, At, "CAF-600-PRT"),
        new StockAdjusted(Id, Id, 2, At, "CAF-600-PRT", -3, "stocktake", 7, 2),
        new StockReserved(Id, Id, 3, At, "CAF-600-PRT", 2, 4, 3),
        new StockReleased(Id, Id, 4, At, "CAF-600-PRT", 1, 3, 4),
        new OrderPlaced(
            Id,
            Id,
            1,
            At,
            CustomerId,
            "PF-2026-000001",
            new Money(379.80m, "BRL"),
            [new OrderPlacedItem(ProductId, "CAF-600-PRT", 2, Price)]
        ),
        new OrderPaid(Id, Id, 2, At, CustomerId, "PF-2026-000001"),
        new OrderShipped(Id, Id, 3, At, CustomerId, "PF-2026-000001"),
        new OrderDelivered(Id, Id, 4, At, CustomerId, "PF-2026-000001"),
        new OrderCancelled(Id, Id, 3, At, CustomerId, "PF-2026-000001", "changedMind", true),
        new ShipmentDispatched(Id, ShipmentId, 2, At, Id, "Correios", "BR123456789", new DateOnly(2026, 10, 9)),
        new ShipmentDelivered(Id, ShipmentId, 3, At, Id),
        new ShipmentFailed(Id, ShipmentId, 3, At, Id, "addressNotFound"),
        new ShipmentCancelled(Id, ShipmentId, 2, At, Id),
    ];

    public static TheoryData<string> EventNames()
    {
        var names = new TheoryData<string>();
        foreach (var sample in Samples)
        {
            names.Add(RoutingKeys.For(sample.GetType().FullName!));
        }

        return names;
    }

    private static IDomainEvent SampleOf(string routingKey) =>
        Samples.Single(sample => RoutingKeys.For(sample.GetType().FullName!) == routingKey);

    private static string Serialize(IDomainEvent domainEvent) =>
        OutboxMessage.From(domainEvent, correlationId: null, causationId: null).Payload;

    private static string SchemaPath(string routingKey) =>
        Path.Combine(AppContext.BaseDirectory, "events", $"{routingKey}.v1.schema.json");

    private static readonly Lock SchemaGate = new();
    private static readonly Dictionary<string, JsonSchema> Schemas = new(StringComparer.Ordinal);

    // A schema registers its $id globally when it is parsed, so each file is parsed once per process.
    private static JsonSchema SchemaOf(string routingKey)
    {
        lock (SchemaGate)
        {
            if (!Schemas.TryGetValue(routingKey, out var schema))
            {
                schema = JsonSchema.FromText(File.ReadAllText(SchemaPath(routingKey)));
                Schemas[routingKey] = schema;
            }

            return schema;
        }
    }

    private static bool IsValid(string routingKey, string json)
    {
        var schema = SchemaOf(routingKey);
        using var document = JsonDocument.Parse(json);
        return schema.Evaluate(document.RootElement, new EvaluationOptions { RequireFormatValidation = true }).IsValid;
    }

    [Theory]
    [MemberData(nameof(EventNames))]
    public void Should_validate_the_event_as_the_outbox_serializes_it(string routingKey)
    {
        IsValid(routingKey, Serialize(SampleOf(routingKey))).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(EventNames))]
    public void Should_reject_the_event_when_a_required_property_is_missing(string routingKey)
    {
        var payload = JsonNode.Parse(Serialize(SampleOf(routingKey)))!.AsObject();
        payload.Remove("eventId");

        IsValid(routingKey, payload.ToJsonString()).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(EventNames))]
    public void Should_accept_a_property_added_later_because_consumers_ignore_unknown_ones(string routingKey)
    {
        var payload = JsonNode.Parse(Serialize(SampleOf(routingKey)))!.AsObject();
        payload["addedInALaterMinorVersion"] = "x";

        IsValid(routingKey, payload.ToJsonString()).ShouldBeTrue();
    }

    [Fact]
    public void Should_have_a_sample_for_every_domain_event_in_the_application()
    {
        var events = typeof(Program)
            .Assembly.GetTypes()
            .Where(type =>
                type is { IsAbstract: false, IsInterface: false } && type.IsAssignableTo(typeof(IDomainEvent))
            )
            .ToList();

        events.ShouldNotBeEmpty();
        events.ShouldBe(Samples.Select(sample => sample.GetType()), ignoreOrder: true);
    }

    [Fact]
    public void Should_have_an_event_for_every_schema_file()
    {
        var schemas = Directory
            .GetFiles(Path.Combine(AppContext.BaseDirectory, "events"), "*.v1.schema.json")
            .Select(path => Path.GetFileName(path)[..^".v1.schema.json".Length]);

        schemas.ShouldBe(Samples.Select(sample => RoutingKeys.For(sample.GetType().FullName!)), ignoreOrder: true);
    }

    [Fact]
    public void Should_write_money_as_a_decimal_string_and_enums_as_camel_case_strings()
    {
        var created = JsonNode.Parse(Serialize(SampleOf("catalog.product-created")))!;
        var statusChanged = JsonNode.Parse(Serialize(SampleOf("catalog.product-status-changed")))!;

        created["price"]!["amount"]!.GetValueKind().ShouldBe(JsonValueKind.String);
        created["price"]!["amount"]!.GetValue<string>().ShouldBe("189.90");
        created["price"]!["currency"]!.GetValue<string>().ShouldBe("BRL");
        statusChanged["from"]!.GetValue<string>().ShouldBe("draft");
        statusChanged["to"]!.GetValue<string>().ShouldBe("active");
    }

    [Fact]
    public void Should_round_trip_money_through_the_event_json()
    {
        var json = Serialize(SampleOf("catalog.product-price-changed"));

        var restored = JsonSerializer.Deserialize<ProductPriceChanged>(json, MessageJson.Options);

        restored.ShouldNotBeNull().NewPrice.ShouldBe(new Money(199m, "BRL"));
        restored.OldPrice.ShouldBe(Price);
    }

    [Fact]
    public void Should_let_the_inventory_consumer_read_the_catalogs_published_event_without_sharing_its_type()
    {
        var json = Serialize(SampleOf("catalog.product-created"));

        var message = JsonSerializer.Deserialize<ProductCreatedMessage>(json, MessageJson.Options);

        message.ShouldNotBeNull().Sku.ShouldBe("CAF-600-PRT");
    }

    [Fact]
    public void Should_let_the_cart_consumer_read_every_catalog_event_it_uses_without_sharing_their_types()
    {
        var created = Read("catalog.product-created");
        var priceChanged = Read("catalog.product-price-changed");
        var statusChanged = Read("catalog.product-status-changed");
        var deleted = Read("catalog.product-deleted");

        (created.AggregateId, created.AggregateVersion, created.Sku, created.Name).ShouldBe(
            (Id, 1, "CAF-600-PRT", "Cafeteira Elétrica 600ml")
        );
        created.Price.ShouldBe(Price);
        priceChanged.NewPrice.ShouldBe(new Money(199m, "BRL"));
        (statusChanged.AggregateVersion, statusChanged.To).ShouldBe((3, "active"));
        (deleted.AggregateVersion, deleted.AggregateId).ShouldBe((4, Id));

        static CatalogProductMessage Read(string routingKey) =>
            JsonSerializer
                .Deserialize<CatalogProductMessage>(Serialize(SampleOf(routingKey)), MessageJson.Options)
                .ShouldNotBeNull();
    }

    [Fact]
    public void Should_let_the_shipping_consumer_read_the_order_events_it_uses_without_sharing_their_types()
    {
        var placed = Read("ordering.order-placed");
        var paid = Read("ordering.order-paid");
        var cancelled = Read("ordering.order-cancelled");

        foreach (var message in new[] { placed, paid, cancelled })
        {
            (message.AggregateId, message.CustomerId, message.Number).ShouldBe((Id, CustomerId, "PF-2026-000001"));
        }

        static OrderEventMessage Read(string routingKey) =>
            JsonSerializer
                .Deserialize<OrderEventMessage>(Serialize(SampleOf(routingKey)), MessageJson.Options)
                .ShouldNotBeNull();
    }

    [Theory]
    [InlineData("shipping.shipment-dispatched")]
    [InlineData("shipping.shipment-delivered")]
    public void Should_let_the_ordering_consumers_read_the_shipment_events_they_use_without_sharing_their_types(
        string routingKey
    )
    {
        var message = JsonSerializer.Deserialize<ShipmentEventMessage>(
            Serialize(SampleOf(routingKey)),
            MessageJson.Options
        );

        message.ShouldNotBeNull().OrderId.ShouldBe(Id);
    }

    [Fact]
    public void Should_write_the_estimated_delivery_as_a_calendar_date_and_absent_optionals_as_null()
    {
        var dispatched = JsonNode.Parse(Serialize(SampleOf("shipping.shipment-dispatched")))!;
        var withoutOptionals = JsonNode.Parse(
            Serialize(new ShipmentDispatched(Id, ShipmentId, 2, At, Id, "Correios", null, null))
        )!;

        dispatched["estimatedDeliveryDate"]!.GetValue<string>().ShouldBe("2026-10-09");
        withoutOptionals["trackingCode"].ShouldBeNull();
        withoutOptionals["estimatedDeliveryDate"].ShouldBeNull();
        IsValid("shipping.shipment-dispatched", withoutOptionals.ToJsonString()).ShouldBeTrue();
    }

    [Fact]
    public void Should_carry_the_order_lines_in_the_placed_event_with_money_as_decimal_strings()
    {
        var placed = JsonNode.Parse(Serialize(SampleOf("ordering.order-placed")))!;

        placed["total"]!["amount"]!.GetValue<string>().ShouldBe("379.80");
        var line = placed["items"]!.AsArray().ShouldHaveSingleItem()!;
        (line["sku"]!.GetValue<string>(), line["quantity"]!.GetValue<int>()).ShouldBe(("CAF-600-PRT", 2));
        line["unitPrice"]!["amount"]!.GetValue<string>().ShouldBe("189.90");
    }

    [Fact]
    public void Should_leave_the_free_text_cancellation_note_out_of_the_event()
    {
        var cancelled = JsonNode.Parse(Serialize(SampleOf("ordering.order-cancelled")))!.AsObject();

        cancelled.ContainsKey("note").ShouldBeFalse();
        (cancelled["reasonCode"]!.GetValue<string>(), cancelled["wasPaid"]!.GetValue<bool>()).ShouldBe(
            ("changedMind", true)
        );
    }

    [Fact]
    public void Should_let_the_customers_consumer_read_the_identity_event_without_sharing_its_type()
    {
        var json = Serialize(SampleOf("identity.customer-registered"));

        var message = JsonSerializer.Deserialize<CustomerRegisteredMessage>(json, MessageJson.Options);

        message.ShouldNotBeNull();
        message.AggregateId.ShouldBe(Id);
        message.Email.ShouldBe("ana.souza@example.com");
        message.FullName.ShouldBe("Ana Souza");
        message.Phone.ShouldBe("+5511987654321");
        message.Locale.ShouldBe("pt-BR");
        message.TimeZone.ShouldBe("America/Sao_Paulo");
    }

    [Fact]
    public void Should_validate_a_registration_that_gave_no_profile_data_with_null_members()
    {
        var empty = new CustomerRegistered(Id, Id, 1, At, "ana.souza@example.com", null, null, null, null);

        IsValid("identity.customer-registered", Serialize(empty)).ShouldBeTrue();
    }

    [Fact]
    public void Should_publish_every_event_under_its_own_context_in_kebab_case()
    {
        foreach (var sample in Samples)
        {
            RoutingKeys
                .For(sample.GetType().FullName!)
                .ShouldMatch("^(catalog|identity|inventory|ordering|shipping)[.][a-z]+(-[a-z]+)+$");
        }
    }
}
