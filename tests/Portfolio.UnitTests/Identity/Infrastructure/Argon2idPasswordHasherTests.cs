using Microsoft.Extensions.Options;
using Portfolio.Identity.Infrastructure;

namespace Portfolio.UnitTests.Identity.Infrastructure;

/// <summary>Argon2id verifiers per ai/SECURITY.md §4.2. These use the real algorithm, so they stay few.</summary>
public sealed class Argon2idPasswordHasherTests : IDisposable
{
    private const string Password = "correct-horse-battery-staple";

    private readonly Argon2idPasswordHasher _hasher = new(Options.Create(new PasswordHashingOptions()));

    public void Dispose() => _hasher.Dispose();

    [Fact]
    public async Task Should_produce_a_phc_string_with_the_standard_parameters()
    {
        var hash = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);

        hash.ShouldStartWith("$argon2id$v=19$m=65536,t=3,p=1$");
        hash.Split('$').Length.ShouldBe(6);
        hash.ShouldNotContain(Password);
    }

    [Fact]
    public async Task Should_encode_a_sixteen_byte_salt_and_a_thirty_two_byte_hash()
    {
        var parts = (await _hasher.HashAsync(Password, TestContext.Current.CancellationToken)).Split('$');

        Convert.FromBase64String(Pad(parts[4])).Length.ShouldBe(16);
        Convert.FromBase64String(Pad(parts[5])).Length.ShouldBe(32);
    }

    [Fact]
    public async Task Should_use_a_different_salt_for_every_hash_of_the_same_password()
    {
        var first = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);
        var second = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);

        first.ShouldNotBe(second);
    }

    [Fact]
    public async Task Should_verify_the_right_password_and_refuse_the_wrong_one()
    {
        var hash = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);

        (await _hasher.VerifyAsync(Password, hash, TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await _hasher.VerifyAsync(Password + "x", hash, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("$argon2i$v=19$m=65536,t=3,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=x,t=3,p=1$c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=1$%%%$aGFzaA")]
    [InlineData("$argon2id$v=19$m=65536,t=3$c2FsdA$aGFzaA")]
    public async Task Should_treat_a_malformed_verifier_as_a_mismatch_instead_of_throwing(string verifier)
    {
        (await _hasher.VerifyAsync(Password, verifier, TestContext.Current.CancellationToken)).ShouldBeFalse();
        _hasher.NeedsRehash(verifier).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_not_ask_for_a_rehash_when_the_parameters_are_current()
    {
        var hash = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);

        _hasher.NeedsRehash(hash).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_ask_for_a_rehash_when_the_verifier_was_made_with_weaker_parameters()
    {
        var hash = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);

        _hasher.NeedsRehash(hash.Replace("m=65536", "m=32768", StringComparison.Ordinal)).ShouldBeTrue();
        _hasher.NeedsRehash(hash.Replace("t=3", "t=2", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_verify_with_the_parameters_stored_in_the_verifier_not_the_current_ones()
    {
        using var stronger = new Argon2idPasswordHasher(Options.Create(new PasswordHashingOptions { Iterations = 4 }));
        var oldHash = await _hasher.HashAsync(Password, TestContext.Current.CancellationToken);

        (await stronger.VerifyAsync(Password, oldHash, TestContext.Current.CancellationToken)).ShouldBeTrue();
        stronger.NeedsRehash(oldHash).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_burn_the_same_kind_of_work_without_needing_an_account()
    {
        await _hasher.BurnAsync(Password, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_handle_passwords_with_unicode_and_spaces()
    {
        const string passphrase = "senha com acentuação e espaços — 日本語";
        var hash = await _hasher.HashAsync(passphrase, TestContext.Current.CancellationToken);

        (await _hasher.VerifyAsync(passphrase, hash, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_stop_waiting_for_a_slot_when_cancelled()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => _hasher.HashAsync(Password, cancelled.Token));
    }

    private static string Pad(string base64) => base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
}
