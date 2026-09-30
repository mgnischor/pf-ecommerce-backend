namespace Portfolio.UnitTests.Support;

/// <summary>Deterministic clock shared by the unit tests.</summary>
internal static class TestClock
{
    /// <summary>The instant every <see cref="Create"/> clock starts at.</summary>
    public static readonly DateTimeOffset Start = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Creates a fake clock frozen at <see cref="Start"/>.</summary>
    public static FakeTimeProvider Create() => new(Start);
}
