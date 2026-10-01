namespace Portfolio.IntegrationTests.Http;

/// <summary>Key handling at startup: fail closed outside Development, ephemeral keys only inside it (ai/SECURITY.md §5, §7.6).</summary>
public sealed class StartupSecurityTests
{
    [Fact]
    public void Should_refuse_to_start_in_production_without_a_signing_key()
    {
        using var factory = new ApiFactory("Production", withSecrets: false);

        Should.Throw<InvalidOperationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Should_start_in_development_with_an_ephemeral_key_and_still_authenticate()
    {
        using var factory = new ApiFactory("Development", withSecrets: false);
        using var client = factory.CreateClient();
        using var registration = await AuthClient.SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            accessToken: null,
            new { email = "dev.user@example.com", password = ApiFactory.RandomPassword() }
        );

        registration.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("Jwt:AccessTokenLifetime", "00:30:00")]
    [InlineData("Jwt:RefreshTokenLifetime", "8.00:00:00")]
    [InlineData("Jwt:RefreshFamilyLifetime", "31.00:00:00")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", "")]
    [InlineData("Identity:PasswordHashing:MemoryKiB", "1024")]
    [InlineData("Identity:PasswordHashing:Iterations", "1")]
    [InlineData("Identity:PasswordHashing:Parallelism", "9")]
    public void Should_refuse_to_start_with_settings_weaker_than_the_standard_allows(string key, string value)
    {
        using var factory = new ApiFactory("Production", builder => builder.UseSetting(key, value));

        Should.Throw<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void Should_refuse_to_start_when_a_bootstrap_account_violates_the_password_policy()
    {
        using var factory = new ApiFactory(
            "Production",
            builder =>
            {
                builder.UseSetting("Identity:Bootstrap:Accounts:9:Email", "weak.admin@example.com");
                builder.UseSetting("Identity:Bootstrap:Accounts:9:Password", "password12345");
                builder.UseSetting("Identity:Bootstrap:Accounts:9:AccessLevel", "administrator");
            }
        );

        Should.Throw<Exception>(() => factory.CreateClient());
    }
}
