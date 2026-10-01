using Portfolio.SharedKernel.Domain;

namespace Portfolio.UnitTests.SharedKernel.Domain;

public sealed class ErrorFactoryTests
{
    [Fact]
    public void Should_create_a_validation_error_with_field_and_rule()
    {
        var error = Error.Validation("ORDER_TOTAL_BELOW_MINIMUM", "total", "BR-ORD-001");

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("ORDER_TOTAL_BELOW_MINIMUM");
        error.Field.ShouldBe("total");
        error.RuleId.ShouldBe("BR-ORD-001");
    }

    [Fact]
    public void Should_create_a_not_found_error_without_field_or_rule()
    {
        var error = Error.NotFound("ORDER_NOT_FOUND");

        error.Type.ShouldBe(ErrorType.NotFound);
        error.Field.ShouldBeNull();
        error.RuleId.ShouldBeNull();
    }

    [Fact]
    public void Should_create_a_conflict_error_with_rule()
    {
        var error = Error.Conflict("ORDER_INVALID_TRANSITION", "BR-ORD-002");

        error.Type.ShouldBe(ErrorType.Conflict);
        error.RuleId.ShouldBe("BR-ORD-002");
    }

    [Fact]
    public void Should_create_a_precondition_failed_error_without_field_or_rule()
    {
        var error = Error.PreconditionFailed("ORDER_VERSION_MISMATCH");

        error.Type.ShouldBe(ErrorType.PreconditionFailed);
        error.Code.ShouldBe("ORDER_VERSION_MISMATCH");
        error.Field.ShouldBeNull();
        error.RuleId.ShouldBeNull();
    }
}

public sealed class ErrorEqualityTests
{
    [Fact]
    public void Should_be_equal_when_parameters_have_the_same_content()
    {
        var first = Error.Validation("LENGTH", "name", null, ErrorParameters.Of(("min", 3), ("max", 200)));
        var second = Error.Validation("LENGTH", "name", null, ErrorParameters.Of(("max", 200), ("min", 3)));

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Should_not_be_equal_when_a_parameter_value_differs()
    {
        var first = Error.Validation("LENGTH", "name", null, ErrorParameters.Of(("max", 200)));
        var second = Error.Validation("LENGTH", "name", null, ErrorParameters.Of(("max", 100)));

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Should_not_be_equal_when_a_parameter_name_differs()
    {
        var first = Error.Validation("LENGTH", "name", null, ErrorParameters.Of(("max", 200)));
        var second = Error.Validation("LENGTH", "name", null, ErrorParameters.Of(("min", 200)));

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Should_not_be_equal_when_the_parameter_count_differs()
    {
        var first = Error.Validation("LENGTH", "name", null, ErrorParameters.Of(("max", 200)));
        var second = Error.Validation("LENGTH", "name", null, ErrorParameters.Of(("max", 200), ("min", 3)));

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Should_not_be_equal_when_only_one_side_has_parameters()
    {
        var withParameters = Error.Validation("LENGTH", "name", null, ErrorParameters.Of(("max", 200)));
        var withoutParameters = Error.Validation("LENGTH", "name");

        withParameters.ShouldNotBe(withoutParameters);
        withoutParameters.ShouldNotBe(withParameters);
    }

    [Fact]
    public void Should_be_equal_when_neither_side_has_parameters()
    {
        Error.NotFound("ORDER_NOT_FOUND").ShouldBe(Error.NotFound("ORDER_NOT_FOUND"));
    }

    [Fact]
    public void Should_not_be_equal_when_the_type_differs()
    {
        Error.NotFound("SAME_CODE").ShouldNotBe(Error.Conflict("SAME_CODE"));
    }

    [Fact]
    public void Should_not_be_equal_to_null()
    {
        Error.NotFound("ORDER_NOT_FOUND").Equals(null).ShouldBeFalse();
    }
}
