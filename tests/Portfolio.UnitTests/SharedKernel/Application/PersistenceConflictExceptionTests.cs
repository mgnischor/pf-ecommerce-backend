using Portfolio.SharedKernel.Application;

namespace Portfolio.UnitTests.SharedKernel.Application;

public sealed class PersistenceConflictExceptionTests
{
    [Fact]
    public void Should_map_a_concurrent_update_to_its_stable_code()
    {
        var failure = new PersistenceConflictException(PersistenceConflictKind.ConcurrentUpdate);

        failure.Code.ShouldBe("CONCURRENT_UPDATE");
        failure.Kind.ShouldBe(PersistenceConflictKind.ConcurrentUpdate);
    }

    [Fact]
    public void Should_map_a_taken_unique_key_to_its_stable_code()
    {
        new PersistenceConflictException(PersistenceConflictKind.DuplicateRecord).Code.ShouldBe("DUPLICATE_RECORD");
    }

    [Fact]
    public void Should_default_to_a_concurrent_update_and_keep_the_provider_exception_for_diagnostics()
    {
        var cause = new InvalidOperationException("provider detail");

        var failure = new PersistenceConflictException("conflict", cause);

        failure.Code.ShouldBe("CONCURRENT_UPDATE");
        failure.InnerException.ShouldBeSameAs(cause);
        new PersistenceConflictException().Code.ShouldBe("CONCURRENT_UPDATE");
        new PersistenceConflictException("conflict").Message.ShouldBe("conflict");
    }

    [Fact]
    public void Should_not_put_provider_data_in_its_own_message()
    {
        var failure = new PersistenceConflictException(
            PersistenceConflictKind.DuplicateRecord,
            new InvalidOperationException("Key (email)=(someone@example.com) already exists.")
        );

        failure.Message.ShouldNotContain("someone@example.com");
    }
}
