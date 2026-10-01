using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Identity.Infrastructure;

namespace Portfolio.UnitTests.Identity.Infrastructure;

public sealed class RefreshTokenCodecTests
{
    private static RefreshTokenCodec Create(string? key, bool allowEphemeral = false) =>
        new(
            Options.Create(new TokenSecretsOptions { TokenHashKey = key }),
            allowEphemeral,
            NullLogger<RefreshTokenCodec>.Instance
        );

    private static string NewKey(int bytes = 64) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    [Fact]
    public void Should_generate_a_256_bit_url_safe_token_whose_hash_is_not_the_token()
    {
        var token = Create(NewKey()).Generate();

        token.Plain.Length.ShouldBe(43);
        token.Plain.ShouldMatch("^[A-Za-z0-9_-]+$");
        token.Hash.ShouldNotBe(token.Plain);
    }

    [Fact]
    public void Should_generate_a_different_token_every_time()
    {
        var codec = Create(NewKey());

        var tokens = Enumerable.Range(0, 100).Select(_ => codec.Generate().Plain).ToHashSet(StringComparer.Ordinal);

        tokens.Count.ShouldBe(100);
    }

    [Fact]
    public void Should_hash_deterministically_so_a_presented_token_can_be_looked_up()
    {
        var codec = Create(NewKey());
        var token = codec.Generate();

        codec.Hash(token.Plain).ShouldBe(token.Hash);
    }

    [Fact]
    public void Should_produce_a_sha3_512_sized_hash()
    {
        var hash = Create(NewKey()).Hash("anything");

        Convert.FromBase64String(hash.Replace('-', '+').Replace('_', '/') + "==").Length.ShouldBe(64);
    }

    [Fact]
    public void Should_produce_unrelated_hashes_under_different_keys()
    {
        Create(NewKey()).Hash("same-token").ShouldNotBe(Create(NewKey()).Hash("same-token"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_refuse_to_start_without_a_key_outside_development(string? key)
    {
        Should.Throw<InvalidOperationException>(() => Create(key));
    }

    [Fact]
    public void Should_use_an_ephemeral_key_in_development_when_none_is_configured()
    {
        var codec = Create(null, allowEphemeral: true);

        codec.Hash("x").ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Should_refuse_a_key_that_is_not_base64()
    {
        Should.Throw<InvalidOperationException>(() => Create("***not base64***"));
    }

    [Fact]
    public void Should_refuse_a_key_shorter_than_thirty_two_bytes()
    {
        Should.Throw<InvalidOperationException>(() => Create(NewKey(31)));
    }
}
