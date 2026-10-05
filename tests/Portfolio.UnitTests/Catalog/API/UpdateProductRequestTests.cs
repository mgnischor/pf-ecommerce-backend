using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Portfolio.Catalog.API.Contracts;

namespace Portfolio.UnitTests.Catalog.API;

/// <summary>
/// JSON Merge Patch needs to tell an omitted member from an explicit <c>null</c> (BR-CAT-001), which a plain
/// <c>null</c> value cannot; the request records which members the body named.
/// </summary>
[Trait("Rule", "BR-CAT-001")]
public sealed class UpdateProductRequestTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static UpdateProductRequest Read(string json) =>
        JsonSerializer.Deserialize<UpdateProductRequest>(json, Web).ShouldNotBeNull();

    [Fact]
    public void Should_record_neither_member_when_the_body_is_empty()
    {
        var request = Read("{}");

        request.NameSpecified.ShouldBeFalse();
        request.DescriptionSpecified.ShouldBeFalse();
    }

    [Fact]
    public void Should_record_only_the_members_the_body_names()
    {
        var request = Read("""{"name":"Cafeteira Premium"}""");

        request.NameSpecified.ShouldBeTrue();
        request.Name.ShouldBe("Cafeteira Premium");
        request.DescriptionSpecified.ShouldBeFalse();
        request.Description.ShouldBeNull();
    }

    [Fact]
    public void Should_tell_an_explicit_null_description_from_an_omitted_one()
    {
        var cleared = Read("""{"description":null}""");
        var omitted = Read("""{"name":"Cafeteira Premium"}""");

        cleared.DescriptionSpecified.ShouldBeTrue();
        cleared.Description.ShouldBeNull();
        omitted.DescriptionSpecified.ShouldBeFalse();
    }

    [Fact]
    public void Should_record_an_explicit_null_name_so_the_use_case_can_reject_it()
    {
        var request = Read("""{"name":null}""");

        request.NameSpecified.ShouldBeTrue();
        request.Name.ShouldBeNull();
    }

    [Fact]
    public void Should_not_expose_the_presence_flags_in_the_wire_format()
    {
        var json = JsonSerializer.Serialize(Read("""{"name":"Cafeteira Premium"}"""), Web);

        json.ShouldNotContain("Specified", Case.Insensitive);
    }

    [Fact]
    public void Should_keep_the_length_validation_of_the_name()
    {
        Validate(Read("""{"name":"ab"}""")).ShouldContain(nameof(UpdateProductRequest.Name));
    }

    [Fact]
    public void Should_keep_the_length_validation_of_the_description()
    {
        Validate(new UpdateProductRequest { Description = new string('x', 2001) })
            .ShouldContain(nameof(UpdateProductRequest.Description));
    }

    [Fact]
    public void Should_accept_a_valid_partial_update()
    {
        Validate(Read("""{"name":"Cafeteira Premium","description":null}""")).ShouldBeEmpty();
    }

    private static List<string> Validate(UpdateProductRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        return [.. results.SelectMany(result => result.MemberNames)];
    }
}
