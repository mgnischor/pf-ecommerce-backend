using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// Token validation per RFC 8725 and ai/SECURITY.md §7.1–§7.3. A correctly signed baseline token must be
/// accepted, and each test then breaks exactly one property and expects a <c>401</c>.
/// </summary>
public sealed class JwtValidationTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private const string MeUrl = "/api/v1/auth/me";

    private (string Subject, int Version, string Level) Principal =>
        TokenForge.Read(fixture.TokenFor("public").AccessToken);

    private ForgedToken Baseline() =>
        new()
        {
            Key = fixture.Factory.SigningKey,
            Subject = Principal.Subject,
            Version = Principal.Version,
            Level = Principal.Level,
        };

    [Fact]
    public async Task Should_accept_a_correctly_signed_token_so_the_negative_tests_are_meaningful()
    {
        using var response = await AuthClient.GetAsync(fixture.Client, MeUrl, TokenForge.Create(Baseline()));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_reject_a_token_from_another_issuer()
    {
        var token = TokenForge.Create(Baseline() with { Issuer = "https://evil.example" });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_a_token_for_another_audience()
    {
        var token = TokenForge.Create(Baseline() with { Audience = "another-api" });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_a_token_with_the_wrong_type_header()
    {
        var token = TokenForge.Create(Baseline() with { Type = "JWT" });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_an_expired_token()
    {
        var token = TokenForge.Create(
            Baseline() with
            {
                IssuedAtOffset = TimeSpan.FromMinutes(-20),
                NotBeforeOffset = TimeSpan.FromMinutes(-20),
                ExpiresOffset = TimeSpan.FromMinutes(-5),
            }
        );

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_a_token_that_is_not_valid_yet()
    {
        var token = TokenForge.Create(Baseline() with { NotBeforeOffset = TimeSpan.FromMinutes(5) });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_a_token_issued_in_the_future()
    {
        var token = TokenForge.Create(Baseline() with { IssuedAtOffset = TimeSpan.FromMinutes(5) });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_a_token_signed_with_an_unknown_key_that_claims_a_known_key_id()
    {
        using var attacker = TokenForge.NewAttackerKey();

        await ShouldBeRejectedAsync(TokenForge.Create(Baseline() with { Key = attacker }));
    }

    [Fact]
    public async Task Should_reject_a_token_with_an_unknown_key_id()
    {
        var token = TokenForge.Create(Baseline() with { KeyId = "rotated-away" });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_an_unsigned_token_with_alg_none()
    {
        await ShouldBeRejectedAsync(TokenForge.Unsigned(Baseline()));
    }

    [Fact]
    public async Task Should_reject_a_token_signed_with_hs256_using_the_public_key_as_the_secret()
    {
        await ShouldBeRejectedAsync(TokenForge.HmacWithPublicKey(Baseline()));
    }

    [Fact]
    public async Task Should_reject_a_token_whose_payload_was_changed_after_signing()
    {
        var real = fixture.TokenFor("public").AccessToken;

        await ShouldBeRejectedAsync(TokenForge.TamperPayload(real, "access_level", "developer"));
    }

    [Fact]
    public async Task Should_reject_a_token_without_a_token_id()
    {
        var token = TokenForge.Create(Baseline() with { TokenId = null });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_a_token_with_a_stale_token_version()
    {
        var token = TokenForge.Create(Baseline() with { Version = Principal.Version + 1 });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_a_token_that_claims_a_higher_level_than_the_server_holds()
    {
        var token = TokenForge.Create(Baseline() with { Level = "developer" });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_a_token_for_an_account_that_does_not_exist()
    {
        var token = TokenForge.Create(Baseline() with { Subject = Guid.NewGuid().ToString("D") });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_reject_a_token_whose_level_claim_is_not_a_known_level()
    {
        var token = TokenForge.Create(Baseline() with { Level = "4" });

        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_never_accept_a_token_sent_in_the_query_string()
    {
        var token = fixture.TokenFor("public").AccessToken;

        using var response = await AuthClient.GetAsync(
            fixture.Client,
            $"{MeUrl}?access_token={Uri.EscapeDataString(token)}",
            accessToken: null
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task Should_reject_a_malformed_bearer_value(string token)
    {
        await ShouldBeRejectedAsync(token);
    }

    [Fact]
    public async Task Should_not_leak_the_reason_for_a_rejection()
    {
        var token = TokenForge.Create(Baseline() with { Issuer = "https://evil.example" });

        using var response = await AuthClient.GetAsync(fixture.Client, MeUrl, token);

        response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer");
    }

    [Fact]
    public async Task Should_issue_tokens_with_the_header_and_claims_the_standard_requires()
    {
        var token = new JsonWebTokenHandler().ReadJsonWebToken(fixture.TokenFor("manager").AccessToken);

        token.Alg.ShouldBe("ES384");
        token.Typ.ShouldBe("at+jwt");
        token.Kid.ShouldBe(ApiFactory.KeyId);
        token.Issuer.ShouldBe(ApiFactory.Issuer);
        token.Audiences.ShouldBe([ApiFactory.Audience]);
        token.GetClaim("access_level").Value.ShouldBe("manager");
        token.Id.ShouldNotBeNullOrWhiteSpace();
        (token.ValidTo - token.ValidFrom).ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Should_keep_personal_data_out_of_the_token_payload()
    {
        var account = fixture.Factory.Accounts["manager"];
        var token = fixture.TokenFor("manager").AccessToken;
        var payload = System.Text.Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]));

        payload.ShouldNotContain(account.Email);
        payload.ShouldNotContain("user@");
    }

    [Fact]
    public async Task Should_publish_a_jwks_whose_public_key_verifies_issued_tokens_and_hides_the_private_key()
    {
        using var response = await AuthClient.GetAsync(fixture.Client, "/.well-known/jwks.json", accessToken: null);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var keySet = new JsonWebKeySet(json);
        var key = keySet.Keys.ShouldHaveSingleItem();
        using var verifier = ECDsa.Create(
            new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP384,
                Q = new ECPoint { X = Base64UrlEncoder.DecodeBytes(key.X), Y = Base64UrlEncoder.DecodeBytes(key.Y) },
            }
        );

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            fixture.TokenFor("public").AccessToken,
            new TokenValidationParameters
            {
                IssuerSigningKey = new ECDsaSecurityKey(verifier) { KeyId = key.Kid },
                ValidIssuer = ApiFactory.Issuer,
                ValidAudience = ApiFactory.Audience,
                ValidAlgorithms = ["ES384"],
            }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        key.Kid.ShouldBe(ApiFactory.KeyId);
        key.Alg.ShouldBe("ES384");
        key.Use.ShouldBe("sig");
        json.ShouldNotContain("\"d\"");
        validation.IsValid.ShouldBeTrue();
    }

    private async Task ShouldBeRejectedAsync(string token)
    {
        using var response = await AuthClient.GetAsync(fixture.Client, MeUrl, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
