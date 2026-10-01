using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.UnitTests.SharedKernel.Infrastructure;

public sealed class DatabaseOptionsValidatorTests
{
    private readonly DatabaseOptionsValidator _validator = new();

    [Fact]
    public void Should_accept_the_defaults()
    {
        _validator.Validate(null, new DatabaseOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Should_not_migrate_at_startup_by_default()
    {
        new DatabaseOptions().MigrateOnStartup.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, 30, 15, 3, 5)]
    [InlineData(501, 30, 15, 3, 5)]
    [InlineData(20, 0, 15, 3, 5)]
    [InlineData(20, 301, 15, 3, 5)]
    [InlineData(20, 30, 0, 3, 5)]
    [InlineData(20, 30, 121, 3, 5)]
    [InlineData(20, 30, 15, -1, 5)]
    [InlineData(20, 30, 15, 11, 5)]
    [InlineData(20, 30, 15, 3, 0)]
    [InlineData(20, 30, 15, 3, 61)]
    public void Should_reject_a_value_outside_its_range(int pool, int command, int connection, int retries, int delay)
    {
        var options = new DatabaseOptions
        {
            MaxPoolSize = pool,
            CommandTimeoutSeconds = command,
            ConnectionTimeoutSeconds = connection,
            MaxRetryCount = retries,
            MaxRetryDelaySeconds = delay,
        };

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldStartWith("Database:");
    }

    [Fact]
    public void Should_allow_turning_retries_off()
    {
        _validator.Validate(null, new DatabaseOptions { MaxRetryCount = 0 }).Succeeded.ShouldBeTrue();
    }
}
