using Portfolio.Identity.Infrastructure;

namespace Portfolio.UnitTests.Identity.Infrastructure;

public sealed class CommonPasswordScreenTests
{
    private readonly CommonPasswordScreen _screen = new();

    [Theory]
    [InlineData("password12345")]
    [InlineData("PASSWORD12345")]
    [InlineData("qwerty123456")]
    [InlineData("administrator")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("abababababababab")]
    public async Task Should_flag_trivially_guessable_passwords(string password)
    {
        (await _screen.IsBreachedAsync(password, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("correct horse battery staple again")]
    [InlineData("7f3c9a2e0b6d4c1e")]
    public async Task Should_let_ordinary_strong_passwords_through(string password)
    {
        (await _screen.IsBreachedAsync(password, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }
}
