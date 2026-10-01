namespace Portfolio.SharedKernel.API.Contracts;

/// <summary>Runtime facts for engineers. Contains no configuration values, secrets, or personal data.</summary>
/// <param name="ApplicationVersion">Informational version of the running build.</param>
/// <param name="Environment">Hosting environment name.</param>
/// <param name="Framework">Runtime description, for example <c>.NET 10.0.0</c>.</param>
/// <param name="ProcessorCount">Logical processors available to the process.</param>
/// <param name="ServerTime">Current UTC time as the server sees it.</param>
/// <param name="UptimeSeconds">Seconds since the process started.</param>
internal sealed record RuntimeDiagnosticsResponse(
    string ApplicationVersion,
    string Environment,
    string Framework,
    int ProcessorCount,
    DateTimeOffset ServerTime,
    long UptimeSeconds
);
