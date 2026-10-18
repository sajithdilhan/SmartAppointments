using Auth.Application.Commands;
using Auth.Application.Validations;

namespace Auth.Tests;

public class CreateStaffCommandValidatorTests
{
    private static readonly CreateStaffCommand Valid =
        new("Sam", "Staff", "sam@example.com", "+15551234567", "P@ssw0rd!23", Guid.CreateVersion7());

    private readonly CreateStaffCommandValidator _subject = new();

    [Fact]
    public void A_Fully_Valid_Command_Passes()
    {
        Assert.True(_subject.Validate(Valid).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void First_And_Last_Name_Are_Required(string value)
    {
        AssertInvalid(Valid with { FirstName = value }, nameof(CreateStaffCommand.FirstName));
        AssertInvalid(Valid with { LastName = value }, nameof(CreateStaffCommand.LastName));
    }

    [Fact]
    public void Names_Are_Capped_At_50_Characters()
    {
        AssertInvalid(Valid with { FirstName = new string('a', 51) }, nameof(CreateStaffCommand.FirstName));
        AssertInvalid(Valid with { LastName = new string('a', 51) }, nameof(CreateStaffCommand.LastName));
        Assert.True(_subject.Validate(Valid with { FirstName = new string('a', 50), LastName = new string('b', 50) }).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Email_Is_Required_And_Valid(string email)
    {
        AssertInvalid(Valid with { Email = email }, nameof(CreateStaffCommand.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0123456")]
    public void Phone_Number_Is_Required_And_Matches_The_Pattern(string phone)
    {
        AssertInvalid(Valid with { PhoneNumber = phone }, nameof(CreateStaffCommand.PhoneNumber));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sh0rt!a")]
    [InlineData("alllowercase1!")]
    [InlineData("ALLUPPERCASE1!")]
    [InlineData("NoDigitsHere!!")]
    [InlineData("NoSpecial1234")]
    public void Password_Follows_The_Registration_Rules(string password)
    {
        AssertInvalid(Valid with { Password = password }, nameof(CreateStaffCommand.Password));
    }

    [Fact]
    public void Branch_Is_Required()
    {
        var result = _subject.Validate(Valid with { BranchId = Guid.Empty });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateStaffCommand.BranchId) && e.ErrorMessage == "Branch is required.");
    }

    [Fact]
    public void Every_Failed_Rule_Is_Reported()
    {
        var result = _subject.Validate(new CreateStaffCommand("", "", "x", "y", "z", Guid.Empty));

        Assert.True(result.Errors.Select(e => e.PropertyName).Distinct().Count() >= 6);
    }

    private void AssertInvalid(CreateStaffCommand command, string property)
    {
        var result = _subject.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == property);
    }
}
