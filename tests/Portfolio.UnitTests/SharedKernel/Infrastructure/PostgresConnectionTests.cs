using Npgsql;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.UnitTests.SharedKernel.Infrastructure;

public sealed class PostgresConnectionTests
{
    private const string Base = "Host=db.internal;Database=ecommerce;Username=app_runtime;Password=not-a-real-secret";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_refuse_to_start_without_a_connection_string_and_name_the_setting_not_a_value(string? missing)
    {
        var failure = Should.Throw<InvalidOperationException>(() =>
            PostgresConnection.CreateDataSource(missing, new DatabaseOptions())
        );

        failure.Message.ShouldContain("ConnectionStrings__Postgres");
    }

    [Fact]
    public void Should_apply_the_explicit_pool_and_timeout_settings_over_the_connection_string()
    {
        var options = new DatabaseOptions
        {
            MaxPoolSize = 7,
            CommandTimeoutSeconds = 11,
            ConnectionTimeoutSeconds = 5,
        };

        using var dataSource = PostgresConnection.CreateDataSource(
            Base + ";Maximum Pool Size=100;Command Timeout=300;Timeout=60",
            options
        );

        var settings = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);
        settings.MaxPoolSize.ShouldBe(7);
        settings.CommandTimeout.ShouldBe(11);
        settings.Timeout.ShouldBe(5);
        settings.ApplicationName.ShouldBe(PostgresConnection.ApplicationName);
    }

    [Fact]
    public void Should_not_open_a_connection_while_building_the_data_source()
    {
        // Host db.internal does not exist: building must still succeed because nothing connects yet.
        using var dataSource = PostgresConnection.CreateDataSource(Base, new DatabaseOptions());

        dataSource.ShouldNotBeNull();
    }
}
