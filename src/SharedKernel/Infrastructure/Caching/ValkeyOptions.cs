namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Settings of the Valkey access (ai/DATABASE.md §4). The connection string is the secret
/// <c>ConnectionStrings:Valkey</c> and never lives here. Valkey is a cache and a source of short-lived hints, never
/// of durable business state: every key written through this layer carries an explicit TTL.
/// </summary>
internal sealed class ValkeyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Valkey";

    /// <summary>Name of the connection string (<c>ConnectionStrings:Valkey</c>).</summary>
    public const string ConnectionStringName = "Valkey";

    /// <summary>First segment of every key (<c>ecommerce:{context}:{entity}:{id}:v{n}</c>). Tests give each host its own.</summary>
    public string KeyPrefix { get; init; } = "ecommerce";

    /// <summary>Milliseconds to wait for the first connection and for each reconnection attempt.</summary>
    public int ConnectTimeoutMilliseconds { get; init; } = 500;

    /// <summary>
    /// Milliseconds a command may take before it counts as a failure. Deliberately short: a slow cache is worse than
    /// no cache, so the request falls back to the source of truth instead of waiting.
    /// </summary>
    public int OperationTimeoutMilliseconds { get; init; } = 250;

    /// <summary>Largest value cached, in bytes; larger values are not cached.</summary>
    public int MaximumPayloadBytes { get; init; } = 65_536;

    /// <summary>
    /// What a token check does when the revocation blocklist cannot be read. <see cref="RevocationFailureMode.Deny"/>
    /// (the default) fails closed, as ai/SECURITY.md requires of authentication paths.
    /// </summary>
    public RevocationFailureMode RevocationCheckFailureMode { get; init; } = RevocationFailureMode.Deny;
}
