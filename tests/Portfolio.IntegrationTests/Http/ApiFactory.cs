using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// Hosts the real application pipeline in-process. Backing services (PostgreSQL, Valkey, RabbitMQ) are not
/// wired yet; they will join through Testcontainers when their infrastructure exists (ai/TESTS.md §9.3).
/// </summary>
internal sealed class ApiFactory(string environment, Action<IWebHostBuilder>? configure = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        configure?.Invoke(builder);
    }
}
