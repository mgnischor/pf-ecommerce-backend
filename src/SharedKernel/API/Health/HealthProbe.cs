using System.Globalization;
using System.Text.RegularExpressions;

namespace Portfolio.SharedKernel.API.Health;

/// <summary>
/// The <c>--health-check</c> mode used by the Compose <c>healthcheck</c> (ai/CONTAINERS.md §2.4, §13.2). The
/// chiseled runtime image ships no shell, <c>curl</c>, or <c>wget</c>, so the application probes itself: it calls
/// its own <c>/health/ready</c> and exits <c>0</c> (ready) or <c>1</c> (anything else, including a timeout).
/// </summary>
internal static partial class HealthProbe
{
    /// <summary>The command-line switch that turns the process into a probe.</summary>
    public const string Switch = "--health-check";

    /// <summary>Port Kestrel listens on in the container image (<c>ASPNETCORE_URLS=http://+:8080</c>).</summary>
    public const int DefaultPort = 8080;

    /// <summary>Path probed.</summary>
    public const string ReadyPath = "/health/ready";

    /// <summary>Whether the command line asks for the probe mode.</summary>
    /// <param name="args">Command-line arguments.</param>
    public static bool IsProbeRequest(IReadOnlyCollection<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains(Switch, StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds the probe URL from <c>ASPNETCORE_URLS</c>: the first <c>http</c> endpoint's port, on <c>localhost</c>
    /// (the host name the default <c>AllowedHosts</c> accepts), or <see cref="DefaultPort"/> when none is set.
    /// </summary>
    /// <param name="urls">The <c>ASPNETCORE_URLS</c> value, if any.</param>
    public static Uri ResolveUrl(string? urls)
    {
        var port = DefaultPort;

        foreach (
            var entry in (urls ?? string.Empty).Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var match = HttpEndpoint().Match(entry);
            if (
                match.Success
                && int.TryParse(
                    match.Groups["port"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsed
                )
                && parsed is > 0 and <= 65535
            )
            {
                port = parsed;
                break;
            }
        }

        return new Uri($"http://localhost:{port}{ReadyPath}");
    }

    /// <summary>Calls the readiness endpoint once.</summary>
    /// <param name="urls">The <c>ASPNETCORE_URLS</c> value, if any.</param>
    /// <param name="timeout">Maximum time to wait for the answer.</param>
    /// <param name="handler">Message handler; tests replace it, production uses the default.</param>
    /// <returns><c>0</c> when the endpoint answered <c>200</c>, <c>1</c> otherwise.</returns>
    public static async Task<int> RunAsync(string? urls, TimeSpan timeout, HttpMessageHandler? handler = null)
    {
        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = timeout;

        try
        {
            using var response = await client.GetAsync(ResolveUrl(urls));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Not ready is an expected outcome here, reported through the exit code; the orchestrator logs it.
            return 1;
        }
    }

    [GeneratedRegex(@"^http://[^:/]+:(?<port>\d+)/?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex HttpEndpoint();
}
