using Portfolio.SharedKernel.Domain;

namespace Portfolio.UnitTests.SharedKernel.Domain;

public sealed class DomainExceptionTests
{
    [Fact]
    public void Should_carry_the_message_of_the_exceptional_state()
    {
        var exception = new DomainException("Aggregate is corrupted.");

        exception.Message.ShouldBe("Aggregate is corrupted.");
    }

    [Fact]
    public void Should_preserve_the_underlying_cause()
    {
        var cause = new InvalidOperationException("root cause");

        var exception = new DomainException("Aggregate is corrupted.", cause);

        exception.InnerException.ShouldBeSameAs(cause);
    }

    [Fact]
    public void Should_be_creatable_without_a_message()
    {
        new DomainException().ShouldBeAssignableTo<Exception>();
    }
}
