using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;
using Portfolio.Identity.Application;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Argon2id password verifiers in PHC string format, <c>$argon2id$v=19$m=…,t=…,p=…$salt$hash</c>
/// (ai/SECURITY.md §4.2). The parameters travel with each verifier, so they can be raised later and old
/// verifiers upgraded transparently at the next successful sign-in. Hashing is CPU- and memory-hungry, so the
/// number of concurrent operations is bounded and runs off the request thread.
/// </summary>
internal sealed class Argon2idPasswordHasher : IPasswordHasher, IDisposable
{
    private const string Prefix = "$argon2id$v=19$";

    private readonly PasswordHashingOptions _options;
    private readonly SemaphoreSlim _gate;
    private readonly Lazy<string> _dummyHash;

    /// <summary>Creates the hasher.</summary>
    /// <param name="options">Validated hashing parameters.</param>
    public Argon2idPasswordHasher(IOptions<PasswordHashingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _gate = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
        _dummyHash = new Lazy<string>(CreateDummyHash, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public Task<string> HashAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        return RunBoundedAsync(
            () =>
            {
                var salt = RandomNumberGenerator.GetBytes(PasswordHashingOptions.SaltLength);
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                try
                {
                    var hash = Compute(
                        passwordBytes,
                        salt,
                        _options.MemoryKiB,
                        _options.Iterations,
                        _options.Parallelism,
                        PasswordHashingOptions.HashLength
                    );
                    return Format(salt, hash, _options.MemoryKiB, _options.Iterations, _options.Parallelism);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(passwordBytes);
                }
            },
            cancellationToken
        );
    }

    /// <inheritdoc />
    public Task<bool> VerifyAsync(string password, string passwordHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(passwordHash);

        return RunBoundedAsync(
            () =>
            {
                if (!TryParse(passwordHash, out var parsed))
                {
                    return false;
                }

                var passwordBytes = Encoding.UTF8.GetBytes(password);
                try
                {
                    var actual = Compute(
                        passwordBytes,
                        parsed.Salt,
                        parsed.MemoryKiB,
                        parsed.Iterations,
                        parsed.Parallelism,
                        parsed.Hash.Length
                    );
                    return CryptographicOperations.FixedTimeEquals(actual, parsed.Hash);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(passwordBytes);
                }
            },
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task BurnAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        _ = await VerifyAsync(password, _dummyHash.Value, cancellationToken);
    }

    /// <inheritdoc />
    public bool NeedsRehash(string passwordHash)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);

        return !TryParse(passwordHash, out var parsed)
            || parsed.MemoryKiB < _options.MemoryKiB
            || parsed.Iterations < _options.Iterations
            || parsed.Parallelism != _options.Parallelism
            || parsed.Hash.Length < PasswordHashingOptions.HashLength
            || parsed.Salt.Length < PasswordHashingOptions.SaltLength;
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private string CreateDummyHash()
    {
        var salt = RandomNumberGenerator.GetBytes(PasswordHashingOptions.SaltLength);
        var hash = Compute(
            [0],
            salt,
            _options.MemoryKiB,
            _options.Iterations,
            _options.Parallelism,
            PasswordHashingOptions.HashLength
        );
        return Format(salt, hash, _options.MemoryKiB, _options.Iterations, _options.Parallelism);
    }

    private async Task<T> RunBoundedAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(work, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static byte[] Compute(
        byte[] password,
        byte[] salt,
        int memoryKiB,
        int iterations,
        int parallelism,
        int length
    )
    {
        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(length);
    }

    private static string Format(byte[] salt, byte[] hash, int memoryKiB, int iterations, int parallelism) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}m={memoryKiB},t={iterations},p={parallelism}${ToBase64(salt)}${ToBase64(hash)}"
        );

    private static string ToBase64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static byte[]? FromBase64(string value)
    {
        var padded = value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '=');
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool TryParse(string encoded, out ParsedHash parsed)
    {
        parsed = default!;

        if (!encoded.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = encoded[Prefix.Length..].Split('$');
        if (parts.Length != 3)
        {
            return false;
        }

        var parameters = parts[0].Split(',');
        if (
            parameters.Length != 3
            || !TryReadParameter(parameters[0], "m=", out var memory)
            || !TryReadParameter(parameters[1], "t=", out var iterations)
            || !TryReadParameter(parameters[2], "p=", out var parallelism)
        )
        {
            return false;
        }

        var salt = FromBase64(parts[1]);
        var hash = FromBase64(parts[2]);
        if (salt is null || hash is null || salt.Length == 0 || hash.Length == 0)
        {
            return false;
        }

        parsed = new ParsedHash(memory, iterations, parallelism, salt, hash);
        return true;
    }

    private static bool TryReadParameter(string text, string name, out int value)
    {
        value = 0;
        return text.StartsWith(name, StringComparison.Ordinal)
            && int.TryParse(text[name.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value > 0;
    }

    private sealed record ParsedHash(int MemoryKiB, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);
}
