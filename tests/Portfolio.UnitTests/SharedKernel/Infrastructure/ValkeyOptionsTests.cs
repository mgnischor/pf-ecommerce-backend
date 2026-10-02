using Microsoft.Extensions.Configuration;
using Portfolio.SharedKernel.Infrastructure;
using StackExchange.Redis;

namespace Portfolio.UnitTests.SharedKernel.Infrastructure;

public sealed class ValkeyOptionsValidatorTests
{
    private static ValkeyOptionsValidator ValidatorWith(string? connectionString) =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["ConnectionStrings:Valkey"] = connectionString,
                    }
                )
                .Build()
        );

    [Fact]
    public void Should_accept_the_defaults_with_a_connection_string()
    {
        ValidatorWith("localhost:6379").Validate(null, new ValkeyOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_refuse_to_start_without_a_connection_string_and_name_the_setting_not_a_value(string? missing)
    {
        var result = ValidatorWith(missing).Validate(null, new ValkeyOptions());

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldContain("ConnectionStrings__Valkey");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ecommerce")]
    [InlineData("a b")]
    [InlineData(":lead")]
    public void Should_reject_a_key_prefix_that_would_break_the_key_scheme(string prefix)
    {
        var result = ValidatorWith("localhost").Validate(null, new ValkeyOptions { KeyPrefix = prefix });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldStartWith("Valkey:KeyPrefix");
    }

    [Theory]
    [InlineData(49, 250, 65_536)]
    [InlineData(10_001, 250, 65_536)]
    [InlineData(500, 9, 65_536)]
    [InlineData(500, 5_001, 65_536)]
    [InlineData(500, 250, 1_023)]
    [InlineData(500, 250, 1_048_577)]
    public void Should_reject_a_timeout_or_a_payload_limit_outside_its_range(int connect, int operation, int payload)
    {
        var options = new ValkeyOptions
        {
            ConnectTimeoutMilliseconds = connect,
            OperationTimeoutMilliseconds = operation,
            MaximumPayloadBytes = payload,
        };

        ValidatorWith("localhost").Validate(null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Should_reject_an_undefined_failure_mode()
    {
        var options = new ValkeyOptions { RevocationCheckFailureMode = (RevocationFailureMode)99 };

        ValidatorWith("localhost")
            .Validate(null, options)
            .Failures.ShouldHaveSingleItem()
            .ShouldContain("RevocationCheckFailureMode");
    }

    [Fact]
    public void Should_fail_closed_on_revocation_by_default()
    {
        new ValkeyOptions().RevocationCheckFailureMode.ShouldBe(RevocationFailureMode.Deny);
    }
}

public sealed class ValkeyConnectionTests
{
    [Fact]
    public void Should_build_client_settings_that_never_abort_start_up_and_time_out_quickly()
    {
        var settings = ValkeyConnection.BuildOptions(
            "valkey:6379,password=not-a-real-secret",
            new ValkeyOptions { ConnectTimeoutMilliseconds = 400, OperationTimeoutMilliseconds = 120 }
        );

        settings.AbortOnConnectFail.ShouldBeFalse();
        settings.ConnectTimeout.ShouldBe(400);
        settings.SyncTimeout.ShouldBe(120);
        settings.AsyncTimeout.ShouldBe(120);
        settings.ClientName.ShouldBe("pf-ecommerce-api");
        settings.ReconnectRetryPolicy.ShouldBeOfType<ExponentialRetry>();
        settings.EndPoints.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Should_name_the_missing_setting_and_never_a_value(string? missing)
    {
        var failure = Should.Throw<InvalidOperationException>(() =>
            ValkeyConnection.BuildOptions(missing, new ValkeyOptions())
        );

        failure.Message.ShouldContain("ConnectionStrings__Valkey");
    }
}

public sealed class CacheEntryTests
{
    private static readonly CacheEntry Entry = new(
        "identity.account",
        "identity",
        "account",
        3,
        TimeSpan.FromSeconds(30)
    );

    [Fact]
    public void Should_build_a_namespaced_versioned_key()
    {
        Entry
            .KeyFor("ecommerce", "0199f3a27c107d3e8a512b9d4c6e1f01")
            .ShouldBe("ecommerce:identity:account:0199f3a27c107d3e8a512b9d4c6e1f01:v3");
    }

    [Fact]
    public void Should_change_the_key_when_the_cached_shape_changes_so_an_old_payload_is_never_read_as_the_new_one()
    {
        Entry.KeyFor("ecommerce", "x").ShouldNotBe((Entry with { Version = 4 }).KeyFor("ecommerce", "x"));
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("a*")]
    [InlineData("")]
    public void Should_refuse_an_identifier_that_could_escape_into_another_key(string id)
    {
        Should.Throw<ArgumentException>(() => Entry.KeyFor("ecommerce", id));
    }

    [Fact]
    public void Should_refuse_an_identifier_longer_than_the_limit()
    {
        Should.Throw<ArgumentException>(() => Entry.KeyFor("ecommerce", new string('a', 129)));
    }

    [Fact]
    public void Should_not_keep_an_in_process_copy_unless_the_entry_asks_for_one()
    {
        Entry.AllowLocalCopy.ShouldBeFalse();
    }
}
