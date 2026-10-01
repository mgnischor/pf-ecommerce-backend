using System.Net;
using Portfolio.SharedKernel.API.Health;

namespace Portfolio.UnitTests.SharedKernel.API;

/// <summary>The <c>--health-check</c> mode the Compose healthcheck relies on (ai/CONTAINERS.md §13.2).</summary>
public sealed class HealthProbeTests
{
    [Theory]
    [InlineData("--health-check", true)]
    [InlineData("--urls=http://localhost:1", false)]
    [InlineData("--HEALTH-CHECK", false)]
    public void Should_recognize_only_the_exact_probe_switch(string argument, bool expected)
    {
        HealthProbe.IsProbeRequest([argument]).ShouldBe(expected);
    }

    [Fact]
    public void Should_recognize_the_switch_among_other_arguments()
    {
        HealthProbe.IsProbeRequest(["--environment", "Production", "--health-check"]).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "http://localhost:8080/health/ready")]
    [InlineData("", "http://localhost:8080/health/ready")]
    [InlineData("http://+:8080", "http://localhost:8080/health/ready")]
    [InlineData("http://0.0.0.0:5223", "http://localhost:5223/health/ready")]
    [InlineData("http://*:9000/", "http://localhost:9000/health/ready")]
    [InlineData("https://+:8443;http://+:8081", "http://localhost:8081/health/ready")]
    [InlineData("http://+:8080;http://+:9090", "http://localhost:8080/health/ready")]
    [InlineData("https://+:8443", "http://localhost:8080/health/ready")]
    [InlineData("http://+:0", "http://localhost:8080/health/ready")]
    [InlineData("http://+:99999", "http://localhost:8080/health/ready")]
    [InlineData("not a url", "http://localhost:8080/health/ready")]
    public void Should_probe_the_first_http_port_on_localhost_and_default_to_8080(string? urls, string expected)
    {
        HealthProbe.ResolveUrl(urls).ToString().ShouldBe(expected);
    }

    [Fact]
    public async Task Should_exit_zero_when_the_application_is_ready()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var code = await HealthProbe.RunAsync("http://+:8080", TimeSpan.FromSeconds(1), handler);

        code.ShouldBe(0);
        handler.Requests.ShouldHaveSingleItem().ShouldBe("http://localhost:8080/health/ready");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Should_exit_one_for_any_answer_other_than_success(HttpStatusCode status)
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(status));

        (await HealthProbe.RunAsync(null, TimeSpan.FromSeconds(1), handler)).ShouldBe(1);
    }

    [Fact]
    public async Task Should_exit_one_when_the_connection_fails()
    {
        using var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        (await HealthProbe.RunAsync(null, TimeSpan.FromSeconds(1), handler)).ShouldBe(1);
    }

    [Fact]
    public async Task Should_exit_one_when_the_application_does_not_answer_in_time()
    {
        using var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));

        (await HealthProbe.RunAsync(null, TimeSpan.FromMilliseconds(50), handler)).ShouldBe(1);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri?.ToString() ?? string.Empty);
            return Task.FromResult(respond(request));
        }
    }
}
