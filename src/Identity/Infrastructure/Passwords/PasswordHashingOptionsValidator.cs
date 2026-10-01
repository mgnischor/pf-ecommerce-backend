using Microsoft.Extensions.Options;

namespace Portfolio.Identity.Infrastructure;

/// <summary>Rejects Argon2id settings weaker than ai/SECURITY.md §4.2 when the host starts.</summary>
internal sealed class PasswordHashingOptionsValidator : IValidateOptions<PasswordHashingOptions>
{
    private const int MaxParallelism = 4;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, PasswordHashingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.MemoryKiB < PasswordHashingOptions.MinMemoryKiB)
        {
            failures.Add("Argon2id memory must be at least 64 MiB (65536 KiB).");
        }

        if (options.Iterations < PasswordHashingOptions.MinIterations)
        {
            failures.Add("Argon2id iterations must be at least 3.");
        }

        if (options.Parallelism is < 1 or > MaxParallelism)
        {
            failures.Add("Argon2id parallelism must be between 1 and 4.");
        }

        if (options.MaxConcurrency < 1)
        {
            failures.Add("Hashing concurrency must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
