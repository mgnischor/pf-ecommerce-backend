using Portfolio.SharedKernel.Domain;

namespace Portfolio.UnitTests.SharedKernel.Domain;

public sealed class ResultTests
{
    private static readonly Error SomeError = Error.Validation("SOME_ERROR", "field", "BR-TST-001");

    [Fact]
    public void Should_be_successful_without_error_when_created_as_success()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Should_carry_the_error_when_created_as_failure()
    {
        var result = Result.Failure(SomeError);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SomeError);
    }

    [Fact]
    public void Should_reject_a_failure_without_error()
    {
        // Justification for the suppression: the test passes null on purpose to verify the guard clause.
        Should.Throw<ArgumentNullException>(() => Result.Failure(null!));
    }
}

public sealed class ResultOfValueTests
{
    private static readonly Error SomeError = Error.NotFound("SOME_ERROR");

    [Fact]
    public void Should_expose_the_value_when_successful()
    {
        var result = Result<string>.Success("ok");

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBeNull();
        result.Value.ShouldBe("ok");
    }

    [Fact]
    public void Should_expose_the_error_when_failed()
    {
        var result = Result<string>.Failure(SomeError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SomeError);
    }

    [Fact]
    public void Should_refuse_to_expose_a_value_when_failed()
    {
        var result = Result<string>.Failure(SomeError);

        var exception = Should.Throw<InvalidOperationException>(() => result.Value);

        exception.Message.ShouldContain("SOME_ERROR");
    }

    [Fact]
    public void Should_reject_a_null_success_value()
    {
        // Justification for the suppression: the test passes null on purpose to verify the guard clause.
        Should.Throw<ArgumentNullException>(() => Result<string>.Success(null!));
    }

    [Fact]
    public void Should_reject_a_failure_without_error()
    {
        // Justification for the suppression: the test passes null on purpose to verify the guard clause.
        Should.Throw<ArgumentNullException>(() => Result<string>.Failure(null!));
    }
}
