using Availability.Application.Commands;
using Availability.Application.Validations;

namespace Availability.Tests;

public class CreateBranchCommandValidatorTests
{
    private static readonly CreateBranchCommand Valid =
        new("PG", "Pettah", "Walk-ins welcome", "12 Main Street", "+94112345678");

    private readonly CreateBranchCommandValidator _subject = new();

    [Fact]
    public void A_Fully_Valid_Command_Passes()
    {
        Assert.True(_subject.Validate(Valid).IsValid);
    }

    [Theory]
    [InlineData("PG")]
    [InlineData("pg")]
    [InlineData(" PG ")]
    [InlineData("PG-01")]
    [InlineData("A1")]
    [InlineData("ABCDEFGHIJ")]
    public void Valid_Codes_Are_Accepted(string code)
    {
        Assert.True(_subject.Validate(Valid with { Code = code }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("P")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("-PG")]
    [InlineData("PG-")]
    [InlineData("P G")]
    [InlineData("PG_1")]
    public void Invalid_Codes_Are_Rejected(string? code)
    {
        AssertInvalid(Valid with { Code = code! }, nameof(CreateBranchCommand.Code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_Is_Required(string name)
    {
        AssertInvalid(Valid with { Name = name }, nameof(CreateBranchCommand.Name));
    }

    [Fact]
    public void Name_Is_Capped_At_100_Characters()
    {
        AssertInvalid(Valid with { Name = new string('a', 101) }, nameof(CreateBranchCommand.Name));
        Assert.True(_subject.Validate(Valid with { Name = new string('a', 100) }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Description_Is_Optional(string? description)
    {
        Assert.True(_subject.Validate(Valid with { Description = description }).IsValid);
    }

    [Fact]
    public void Description_Is_Capped_At_500_Characters()
    {
        AssertInvalid(Valid with { Description = new string('a', 501) }, nameof(CreateBranchCommand.Description));
        Assert.True(_subject.Validate(Valid with { Description = new string('a', 500) }).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Address_Is_Required(string address)
    {
        AssertInvalid(Valid with { Address = address }, nameof(CreateBranchCommand.Address));
    }

    [Fact]
    public void Address_Is_Capped_At_200_Characters()
    {
        AssertInvalid(Valid with { Address = new string('a', 201) }, nameof(CreateBranchCommand.Address));
        Assert.True(_subject.Validate(Valid with { Address = new string('a', 200) }).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0112345678")]
    [InlineData("+94 11 234 5678")]
    [InlineData("+1234567890123456")]
    public void Phone_Number_Must_Be_E164(string phoneNumber)
    {
        AssertInvalid(Valid with { PhoneNumber = phoneNumber }, nameof(CreateBranchCommand.PhoneNumber));
    }

    [Fact]
    public void Every_Failed_Rule_Is_Reported_Not_Just_The_First()
    {
        var result = _subject.Validate(new CreateBranchCommand("-", "", new string('a', 501), "", "nope"));

        Assert.False(result.IsValid);
        Assert.Equal(5, result.Errors.Select(e => e.PropertyName).Distinct().Count());
    }

    private void AssertInvalid(CreateBranchCommand command, string propertyName)
    {
        var result = _subject.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == propertyName);
    }
}
