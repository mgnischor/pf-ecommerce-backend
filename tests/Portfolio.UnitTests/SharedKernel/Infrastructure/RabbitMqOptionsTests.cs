using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.UnitTests.SharedKernel.Infrastructure;

public sealed class RabbitMqOptionsValidatorTests
{
    private static RabbitMqOptionsValidator ValidatorWith(string? connectionString) =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["ConnectionStrings:RabbitMQ"] = connectionString,
                    }
                )
                .Build()
        );

    [Theory]
    [InlineData("amqp://app:secret@rabbitmq:5672")]
    [InlineData("amqps://app:secret@broker.internal:5671/ecommerce")]
    public void Should_accept_the_defaults_with_an_amqp_connection_string(string connectionString)
    {
        ValidatorWith(connectionString).Validate(null, new RabbitMqOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_refuse_to_start_without_a_connection_string_and_name_the_setting_not_a_value(string? missing)
    {
        var result = ValidatorWith(missing).Validate(null, new RabbitMqOptions());

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldContain("ConnectionStrings__RabbitMQ");
    }

    [Theory]
    [InlineData("rabbitmq:5672")]
    [InlineData("http://app:hunter2@rabbitmq:5672")]
    public void Should_reject_a_connection_string_that_is_not_amqp_without_echoing_it(string connectionString)
    {
        var result = ValidatorWith(connectionString).Validate(null, new RabbitMqOptions());

        result.Failed.ShouldBeTrue();
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.ShouldContain("amqp");
        failure.ShouldNotContain("hunter2");
    }

    [Theory]
    [InlineData("")]
    [InlineData(".leading-dot")]
    [InlineData("has space")]
    public void Should_reject_an_exchange_name_the_broker_would_not_accept(string exchange)
    {
        var result = ValidatorWith("amqp://localhost").Validate(null, new RabbitMqOptions { Exchange = exchange });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldStartWith("RabbitMq:Exchange");
    }

    [Fact]
    public void Should_reject_a_dead_letter_exchange_equal_to_the_events_exchange()
    {
        var result = ValidatorWith("amqp://localhost")
            .Validate(null, new RabbitMqOptions { Exchange = "events", DeadLetterExchange = "events" });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldStartWith("RabbitMq:DeadLetterExchange");
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(121, 5)]
    [InlineData(10, 0)]
    [InlineData(10, 61)]
    public void Should_reject_a_timeout_outside_its_range(int publishSeconds, int connectSeconds)
    {
        var options = new RabbitMqOptions
        {
            PublishTimeout = TimeSpan.FromSeconds(publishSeconds),
            ConnectTimeout = TimeSpan.FromSeconds(connectSeconds),
        };

        var result = ValidatorWith("amqp://localhost").Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldStartWith("RabbitMq:");
    }
}

public sealed class RoutingKeysTests
{
    [Theory]
    [InlineData("Portfolio.Catalog.Domain.ProductCreated", "catalog.product-created")]
    [InlineData("Portfolio.Identity.Domain.UserTokensRevoked", "identity.user-tokens-revoked")]
    [InlineData("Portfolio.Inventory.Domain.StockAdjusted", "inventory.stock-adjusted")]
    [InlineData("Portfolio.Catalog.Domain.SKUChanged", "catalog.sku-changed")]
    public void Should_derive_context_and_kebab_case_event_name_from_the_event_type(string type, string expected)
    {
        RoutingKeys.For(type).ShouldBe(expected);
    }

    [Theory]
    [InlineData("ProductCreated", "events.product-created")]
    [InlineData("Other.Company.ThingHappened", "events.thing-happened")]
    public void Should_fall_back_to_a_neutral_context_for_a_type_outside_the_application(string type, string expected)
    {
        RoutingKeys.For(type).ShouldBe(expected);
    }

    [Fact]
    public void Should_never_expose_the_clr_namespace_in_the_published_key()
    {
        RoutingKeys.For("Portfolio.Catalog.Domain.ProductCreated").ShouldNotContain("Domain");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Should_refuse_an_empty_type_name(string type)
    {
        Should.Throw<ArgumentException>(() => RoutingKeys.For(type));
    }
}

public sealed class RetryBackoffTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    public void Should_double_the_pause_with_each_failed_delivery(int attempt, int expectedSeconds)
    {
        RetryBackoff.For(TimeSpan.FromSeconds(2), attempt).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(int.MaxValue)]
    public void Should_never_pause_longer_than_the_cap(int attempt)
    {
        RetryBackoff.For(TimeSpan.FromSeconds(2), attempt).ShouldBe(RetryBackoff.Maximum);
    }

    [Fact]
    public void Should_not_pause_at_all_when_the_base_delay_is_zero()
    {
        RetryBackoff.For(TimeSpan.Zero, 3).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Should_refuse_an_attempt_number_below_one()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RetryBackoff.For(TimeSpan.FromSeconds(2), 0));
    }
}

public sealed class RabbitMqConsumerOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(RabbitMqOptions options) =>
        new RabbitMqOptionsValidator(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["ConnectionStrings:RabbitMQ"] = "amqp://localhost",
                    }
                )
                .Build()
        ).Validate(null, options);

    [Theory]
    [InlineData(0)]
    [InlineData(1_001)]
    public void Should_reject_a_prefetch_outside_its_range(int prefetch)
    {
        var result = Validate(new RabbitMqOptions { PrefetchCount = (ushort)prefetch });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldStartWith("RabbitMq:PrefetchCount");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Should_reject_a_delivery_limit_outside_its_range(int limit)
    {
        var result = Validate(new RabbitMqOptions { MaxDeliveries = limit });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldStartWith("RabbitMq:MaxDeliveries");
    }

    [Fact]
    public void Should_reject_a_negative_or_excessive_retry_backoff()
    {
        Validate(new RabbitMqOptions { RetryBackoff = TimeSpan.FromSeconds(-1) }).Failed.ShouldBeTrue();
        Validate(new RabbitMqOptions { RetryBackoff = TimeSpan.FromMinutes(2) }).Failed.ShouldBeTrue();
    }
}
