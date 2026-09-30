using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Portfolio.IntegrationTests.Http;

/// <summary>Unhandled exceptions fail closed: RFC 9457 response, no internals (OWASP A10:2025, ai/SECURITY.md §2).</summary>
public sealed class FailClosedErrorTests : IDisposable
{
    internal const string SecretDetail = "connection string with password=hunter2";

    private readonly ApiFactory _production = new(
        "Production",
        builder =>
            builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(ExplodingController).Assembly)
            )
    );

    public void Dispose() => _production.Dispose();

    [Fact]
    public async Task Should_answer_an_unhandled_exception_with_problem_details_and_no_internal_detail()
    {
        using var client = _production.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/test-only/explode", UriKind.Relative),
            TestContext.Current.CancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        body.ShouldNotContain(SecretDetail);
        body.ShouldNotContain(nameof(ExplodingController));
        body.ShouldNotContain("StackTrace", Case.Insensitive);
    }

    [Fact]
    public async Task Should_keep_the_security_headers_on_error_responses()
    {
        using var client = _production.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/test-only/explode", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }
}

/// <summary>Throws on purpose. Top-level because MVC does not discover nested controllers.</summary>
[ApiController]
[Route("test-only/explode")]
public sealed class ExplodingController : ControllerBase
{
    [HttpGet]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "MVC actions must be instance methods."
    )]
    public IActionResult Get() => throw new InvalidOperationException(FailClosedErrorTests.SecretDetail);
}
