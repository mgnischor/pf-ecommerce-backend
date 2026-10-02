namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>The pause before a failed delivery is requeued: exponential, so a failing dependency is not hammered, and capped.</summary>
internal static class RetryBackoff
{
    /// <summary>Longest pause.</summary>
    public static readonly TimeSpan Maximum = TimeSpan.FromSeconds(30);

    /// <summary>Pause before requeueing the <paramref name="attempt"/>th failed delivery.</summary>
    /// <param name="baseDelay">Pause after the first failure; doubles with each further one.</param>
    /// <param name="attempt">Delivery number, starting at 1.</param>
    public static TimeSpan For(TimeSpan baseDelay, int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        var doublings = Math.Min(attempt - 1, 20);
        var ticks = baseDelay.Ticks * Math.Pow(2, doublings);
        return ticks >= Maximum.Ticks ? Maximum : TimeSpan.FromTicks((long)ticks);
    }
}
