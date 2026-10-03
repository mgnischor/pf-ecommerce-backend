using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Portfolio.Catalog.Domain;
using Portfolio.Customers.Infrastructure;
using Portfolio.Identity.Domain;
using Portfolio.Inventory.Domain;
using Portfolio.Inventory.Infrastructure;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;

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

    private static readonly IDomainEvent[] Samples =
    [
        new ProductCreated(Id, Id, 1, At, "Cafeteira Elétrica 600ml", "CAF-600-PRT", Price),
        new ProductPriceChanged(Id, Id, 2, At, Price, new Money(199m, "BRL")),
        new ProductStatusChanged(Id, Id, 3, At, ProductStatus.Draft, ProductStatus.Active),
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
                .ShouldMatch("^(catalog|identity|inventory)[.][a-z]+(-[a-z]+)+$");
        }
    }
}
