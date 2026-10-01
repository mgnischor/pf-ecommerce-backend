using Portfolio.Identity.Infrastructure;

namespace Portfolio.UnitTests.Identity.Infrastructure;

/// <summary>Weak or incomplete security configuration must stop the host instead of weakening authentication.</summary>
public sealed class JwtOptionsValidatorTests
{
    private readonly JwtOptionsValidator _validator = new();

    private static JwtOptions Valid() => new() { Issuer = "https://api.example.com", Audience = "api" };

    [Fact]
    public void Should_accept_the_documented_defaults()
    {
        _validator.Validate(null, Valid()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_require_issuer_and_audience(string value)
    {
        var noIssuer = Valid();
        noIssuer.Issuer = value;
        var noAudience = Valid();
        noAudience.Audience = value;

        _validator.Validate(null, noIssuer).Failed.ShouldBeTrue();
        _validator.Validate(null, noAudience).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:00", false)]
    [InlineData("00:15:00", true)]
    [InlineData("00:15:01", false)]
    [InlineData("-00:05:00", false)]
    public void Should_limit_the_access_token_lifetime_to_fifteen_minutes(string lifetime, bool valid)
    {
        var options = Valid();
        options.AccessTokenLifetime = TimeSpan.Parse(lifetime, System.Globalization.CultureInfo.InvariantCulture);

        _validator.Validate(null, options).Succeeded.ShouldBe(valid);
    }

    [Theory]
    [InlineData("7.00:00:00", true)]
    [InlineData("7.00:00:01", false)]
    [InlineData("00:00:00", false)]
    public void Should_limit_the_refresh_token_lifetime_to_seven_days(string lifetime, bool valid)
    {
        var options = Valid();
        options.RefreshTokenLifetime = TimeSpan.Parse(lifetime, System.Globalization.CultureInfo.InvariantCulture);

        _validator.Validate(null, options).Succeeded.ShouldBe(valid);
    }

    [Theory]
    [InlineData("30.00:00:00", true)]
    [InlineData("30.00:00:01", false)]
    [InlineData("6.00:00:00", false)]
    public void Should_limit_the_session_lifetime_to_thirty_days_and_at_least_one_refresh_token(
        string lifetime,
        bool valid
    )
    {
        var options = Valid();
        options.RefreshFamilyLifetime = TimeSpan.Parse(lifetime, System.Globalization.CultureInfo.InvariantCulture);

        _validator.Validate(null, options).Succeeded.ShouldBe(valid);
    }

    [Fact]
    public void Should_require_the_active_key_to_exist_and_have_a_private_part()
    {
        var missing = Valid();
        missing.ActiveKeyId = "absent";
        missing.Keys = [new() { Id = "k1", PrivateKeyPem = "pem" }];
        var publicOnly = Valid();
        publicOnly.ActiveKeyId = "k1";
        publicOnly.Keys = [new() { Id = "k1", PublicKeyPem = "pem" }];

        _validator.Validate(null, missing).Failed.ShouldBeTrue();
        _validator.Validate(null, publicOnly).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Should_require_unique_non_empty_key_identifiers()
    {
        var duplicated = Valid();
        duplicated.ActiveKeyId = "k1";
        duplicated.Keys = [new() { Id = "k1", PrivateKeyPem = "a" }, new() { Id = "k1", PrivateKeyPem = "b" }];
        var unnamed = Valid();
        unnamed.ActiveKeyId = "";
        unnamed.Keys = [new() { Id = "", PrivateKeyPem = "a" }];

        _validator.Validate(null, duplicated).Failed.ShouldBeTrue();
        _validator.Validate(null, unnamed).Failed.ShouldBeTrue();
    }
}

public sealed class PasswordHashingOptionsValidatorTests
{
    private readonly PasswordHashingOptionsValidator _validator = new();

    [Fact]
    public void Should_accept_the_standard_minimums()
    {
        _validator.Validate(null, new PasswordHashingOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(65_535, 3, 1, false)]
    [InlineData(65_536, 2, 1, false)]
    [InlineData(65_536, 3, 0, false)]
    [InlineData(65_536, 3, 5, false)]
    [InlineData(131_072, 4, 4, true)]
    public void Should_only_allow_parameters_at_or_above_the_standard(
        int memory,
        int iterations,
        int parallelism,
        bool valid
    )
    {
        var options = new PasswordHashingOptions
        {
            MemoryKiB = memory,
            Iterations = iterations,
            Parallelism = parallelism,
        };

        _validator.Validate(null, options).Succeeded.ShouldBe(valid);
    }

    [Fact]
    public void Should_require_at_least_one_concurrent_hashing_slot()
    {
        _validator.Validate(null, new PasswordHashingOptions { MaxConcurrency = 0 }).Failed.ShouldBeTrue();
    }
}
