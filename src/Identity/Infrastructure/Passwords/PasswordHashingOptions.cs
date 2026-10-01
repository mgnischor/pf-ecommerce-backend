namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Argon2id parameters (<c>Identity:PasswordHashing</c>). The defaults are the minimums of
/// ai/SECURITY.md §4.2; configuration may only raise them, never lower them.
/// </summary>
internal sealed class PasswordHashingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Identity:PasswordHashing";

    /// <summary>Minimum memory cost in KiB (64 MiB).</summary>
    public const int MinMemoryKiB = 65_536;

    /// <summary>Minimum iterations.</summary>
    public const int MinIterations = 3;

    /// <summary>Salt length in bytes (128 bits).</summary>
    public const int SaltLength = 16;

    /// <summary>Output length in bytes (256 bits).</summary>
    public const int HashLength = 32;

    /// <summary>Memory cost in KiB.</summary>
    public int MemoryKiB { get; set; } = MinMemoryKiB;

    /// <summary>Iterations (time cost).</summary>
    public int Iterations { get; set; } = MinIterations;

    /// <summary>Degree of parallelism; the standard asks for 1 or the core count up to 4.</summary>
    public int Parallelism { get; set; } = 1;

    /// <summary>Maximum hashing operations at once; bounds memory and CPU an attacker can consume.</summary>
    public int MaxConcurrency { get; set; } = Math.Clamp(Environment.ProcessorCount, 1, 4);
}
