namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Settings of the outbox relay.</summary>
internal sealed class OutboxRelayOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Outbox:Relay";

    /// <summary>Whether this process runs the relays. On only in the worker role; the API role leaves events in the outbox.</summary>
    public bool Enabled { get; init; }

    /// <summary>Pause between polls when nothing was pending.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Messages claimed per batch.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>How long a claimed row is invisible to other relay instances; must outlast one publish.</summary>
    public TimeSpan Lease { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Attempts after which a message stops being retried and waits for an operator (alert on it).</summary>
    public int MaxAttempts { get; init; } = 10;
}
