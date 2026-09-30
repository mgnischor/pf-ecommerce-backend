using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// Settings live in <c>configuration/</c>, which the default host does not probe; these tests guard the
/// explicit loading done by the composition root.
/// </summary>
public sealed class ConfigurationFolderTests
{
    [Theory]
    [InlineData("Production", "localhost")]
    [InlineData("Development", "*")]
    public void Should_layer_the_environment_file_over_the_base_file(string environment, string expectedAllowedHosts)
    {
        using var factory = new ApiFactory(environment);

        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        configuration["AllowedHosts"].ShouldBe(expectedAllowedHosts);
    }

    [Fact]
    public void Should_let_environment_variables_override_the_files()
    {
        Environment.SetEnvironmentVariable("AllowedHosts", "shop.example.com");
        try
        {
            using var factory = new ApiFactory("Production");

            var configuration = factory.Services.GetRequiredService<IConfiguration>();

            configuration["AllowedHosts"].ShouldBe("shop.example.com");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AllowedHosts", null);
        }
    }

    [Fact]
    public void Should_provide_the_system_clock_through_injection()
    {
        using var factory = new ApiFactory("Production");

        factory.Services.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
    }
}
