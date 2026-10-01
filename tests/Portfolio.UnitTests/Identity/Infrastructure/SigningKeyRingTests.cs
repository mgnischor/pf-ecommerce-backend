using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Identity.Infrastructure;

namespace Portfolio.UnitTests.Identity.Infrastructure;

public sealed class SigningKeyRingTests
{
    private static string NewPrivatePem() => ECDsa.Create(ECCurve.NamedCurves.nistP384).ExportPkcs8PrivateKeyPem();

    private static string PublicPemOf(string privatePem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privatePem);
        return key.ExportSubjectPublicKeyInfoPem();
    }

    private static SigningKeyRing Create(JwtOptions options, bool ephemeral = false) =>
        SigningKeyRing.Create(options, ephemeral, NullLogger.Instance);

    [Fact]
    public void Should_sign_with_es384_using_the_active_key()
    {
        using var ring = Create(
            new JwtOptions
            {
                ActiveKeyId = "k2",
                Keys =
                [
                    new() { Id = "k1", PrivateKeyPem = NewPrivatePem() },
                    new() { Id = "k2", PrivateKeyPem = NewPrivatePem() },
                ],
            }
        );

        ring.Signing.Algorithm.ShouldBe("ES384");
        ring.Signing.Key.KeyId.ShouldBe("k2");
    }

    [Fact]
    public void Should_resolve_a_verification_key_only_by_its_exact_key_id()
    {
        using var ring = Create(
            new JwtOptions { ActiveKeyId = "k1", Keys = [new() { Id = "k1", PrivateKeyPem = NewPrivatePem() }] }
        );

        ring.Resolve("k1").ShouldHaveSingleItem();
        ring.Resolve("K1").ShouldBeEmpty();
        ring.Resolve("unknown").ShouldBeEmpty();
        ring.Resolve(null).ShouldBeEmpty();
    }

    [Fact]
    public void Should_keep_a_retired_public_only_key_for_verification_after_rotation()
    {
        var retiredPublic = PublicPemOf(NewPrivatePem());
        using var ring = Create(
            new JwtOptions
            {
                ActiveKeyId = "new",
                Keys =
                [
                    new() { Id = "new", PrivateKeyPem = NewPrivatePem() },
                    new() { Id = "old", PublicKeyPem = retiredPublic },
                ],
            }
        );

        ring.Resolve("old").ShouldHaveSingleItem();
        ring.PublicKeys.Keys.Select(key => key.Kid).ShouldBe(["new", "old"], ignoreOrder: true);
    }

    [Fact]
    public void Should_refuse_an_active_key_that_has_no_private_part()
    {
        var options = new JwtOptions
        {
            ActiveKeyId = "k1",
            Keys = [new() { Id = "k1", PublicKeyPem = PublicPemOf(NewPrivatePem()) }],
        };

        Should.Throw<InvalidOperationException>(() => Create(options));
    }

    [Fact]
    public void Should_refuse_a_key_on_another_curve()
    {
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = new JwtOptions
        {
            ActiveKeyId = "k1",
            Keys = [new() { Id = "k1", PrivateKeyPem = p256.ExportPkcs8PrivateKeyPem() }],
        };

        Should.Throw<InvalidOperationException>(() => Create(options));
    }

    [Fact]
    public void Should_refuse_a_key_entry_without_any_key_material()
    {
        Should.Throw<InvalidOperationException>(() =>
            Create(new JwtOptions { ActiveKeyId = "k1", Keys = [new() { Id = "k1" }] })
        );
    }

    [Fact]
    public void Should_publish_only_public_parameters()
    {
        using var ring = Create(
            new JwtOptions { ActiveKeyId = "k1", Keys = [new() { Id = "k1", PrivateKeyPem = NewPrivatePem() }] }
        );

        var jwk = ring.PublicKeys.Keys.ShouldHaveSingleItem();

        jwk.Kty.ShouldBe("EC");
        jwk.Crv.ShouldBe("P-384");
        jwk.Use.ShouldBe("sig");
        jwk.Alg.ShouldBe("ES384");
        jwk.D.ShouldBeNull();
    }

    [Fact]
    public void Should_read_the_private_key_from_a_mounted_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, NewPrivatePem());

            using var ring = Create(
                new JwtOptions { ActiveKeyId = "k1", Keys = [new() { Id = "k1", PrivateKeyPemFile = path }] }
            );

            ring.Signing.Key.KeyId.ShouldBe("k1");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Should_refuse_to_start_without_keys_outside_development()
    {
        Should.Throw<InvalidOperationException>(() => Create(new JwtOptions()));
    }

    [Fact]
    public void Should_generate_an_ephemeral_key_in_development()
    {
        using var ring = Create(new JwtOptions(), ephemeral: true);

        ring.Signing.Key.KeyId.ShouldStartWith("ephemeral-");
        ring.PublicKeys.Keys.ShouldHaveSingleItem();
    }
}
