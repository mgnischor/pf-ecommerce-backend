using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.UnitTests.SharedKernel.Infrastructure;

public sealed class HmacPageCursorCodecTests
{
    private const string Purpose = "catalog.products.v1|CreatedAt|desc|any|";

    private static string NewKey(int bytes = 64) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private static HmacPageCursorCodec Create(string? key, bool allowEphemeral = false) =>
        new(
            Options.Create(new PageCursorOptions { CursorKey = key }),
            allowEphemeral,
            NullLogger<HmacPageCursorCodec>.Instance
        );

    [Fact]
    public void Should_recover_the_payload_of_a_cursor_it_issued()
    {
        var codec = Create(NewKey());

        var cursor = codec.Protect(Purpose, "0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f01|2026-10-01T12:00:00.0000000+00:00");

        codec.TryUnprotect(Purpose, cursor, out var payload).ShouldBeTrue();
        payload.ShouldBe("0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f01|2026-10-01T12:00:00.0000000+00:00");
    }

    [Fact]
    public void Should_round_trip_non_ascii_text()
    {
        var codec = Create(NewKey());

        var cursor = codec.Protect(Purpose, "Cafeteira Elétrica | Açaí ☕");

        codec.TryUnprotect(Purpose, cursor, out var payload).ShouldBeTrue();
        payload.ShouldBe("Cafeteira Elétrica | Açaí ☕");
    }

    [Fact]
    public void Should_issue_a_url_safe_cursor_that_never_needs_escaping()
    {
        var cursor = Create(NewKey()).Protect(Purpose, "payload with spaces/and+symbols?=&");

        cursor.ShouldMatch("^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Should_issue_the_same_cursor_for_the_same_position_so_pages_are_reproducible()
    {
        var codec = Create(NewKey());

        codec.Protect(Purpose, "position").ShouldBe(codec.Protect(Purpose, "position"));
    }

    [Fact]
    public void Should_reject_a_cursor_issued_for_another_purpose()
    {
        var codec = Create(NewKey());
        var cursor = codec.Protect(Purpose, "position");

        codec.TryUnprotect("catalog.products.v1|Name|asc|any|", cursor, out var payload).ShouldBeFalse();

        payload.ShouldBeEmpty();
    }

    [Fact]
    public void Should_not_confuse_two_purposes_that_concatenate_to_the_same_text()
    {
        var codec = Create(NewKey());
        var cursor = codec.Protect("ab", "c");

        codec.TryUnprotect("a", "bc", out _).ShouldBeFalse();
        codec.TryUnprotect("a", cursor, out _).ShouldBeFalse();
    }

    [Fact]
    public void Should_reject_a_cursor_signed_with_another_key()
    {
        var cursor = Create(NewKey()).Protect(Purpose, "position");

        Create(NewKey()).TryUnprotect(Purpose, cursor, out _).ShouldBeFalse();
    }

    [Fact]
    public void Should_reject_a_cursor_whose_payload_was_altered()
    {
        var codec = Create(NewKey());
        var signature = codec.Protect(Purpose, "position").Split('.')[1];
        var forged = WebEncoders.Base64UrlEncode("another position"u8.ToArray()) + "." + signature;

        codec.TryUnprotect(Purpose, forged, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData("a.b.c")]
    [InlineData(".")]
    [InlineData("!!!.???")]
    [InlineData("cG9zaXRpb24.")]
    [InlineData(".AAAA")]
    public void Should_reject_a_malformed_cursor_without_throwing(string cursor)
    {
        Create(NewKey()).TryUnprotect(Purpose, cursor, out var payload).ShouldBeFalse();

        payload.ShouldBeEmpty();
    }

    [Fact]
    public void Should_reject_a_cursor_longer_than_the_limit_before_decoding_it()
    {
        var codec = Create(NewKey());
        var oversized = codec.Protect(Purpose, new string('x', HmacPageCursorCodec.MaxCursorLength));

        codec.TryUnprotect(Purpose, oversized, out _).ShouldBeFalse();
    }

    [Fact]
    public void Should_require_a_key_outside_development()
    {
        Should.Throw<InvalidOperationException>(() => Create(null)).Message.ShouldContain("Pagination:CursorKey");
        Should.Throw<InvalidOperationException>(() => Create("  ")).Message.ShouldContain("Pagination:CursorKey");
    }

    [Fact]
    public void Should_use_a_throw_away_key_in_development_when_none_is_configured()
    {
        var first = Create(null, allowEphemeral: true);
        var second = Create(null, allowEphemeral: true);
        var cursor = first.Protect(Purpose, "position");

        first.TryUnprotect(Purpose, cursor, out _).ShouldBeTrue();
        second.TryUnprotect(Purpose, cursor, out _).ShouldBeFalse();
    }

    [Fact]
    public void Should_reject_a_key_that_is_not_base64()
    {
        Should.Throw<InvalidOperationException>(() => Create("not base64 !!!")).Message.ShouldContain("base64");
    }

    [Fact]
    public void Should_reject_a_key_shorter_than_the_minimum()
    {
        var weak = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PageCursorOptions.MinKeyBytes - 1));

        Should.Throw<InvalidOperationException>(() => Create(weak)).Message.ShouldContain("32 bytes");
    }

    [Fact]
    public void Should_accept_a_key_of_exactly_the_minimum_length()
    {
        Should.NotThrow(() => Create(NewKey(PageCursorOptions.MinKeyBytes)));
    }
}
