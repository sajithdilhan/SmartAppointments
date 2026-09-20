using Auth.Application.Commands;
using Auth.Application.Validations;

namespace Auth.Tests;

public class RegisterCustomerCommandValidatorTests
{
    private static readonly RegisterCustomerCommand Valid =
        new("Cam", "Customer", "cam@example.com", "+15551234567", "P@ssw0rd!23");

    private readonly RegisterCustomerCommandValidator _subject = new();

    [Fact]
    public void A_Fully_Valid_Command_Passes()
    {
        Assert.True(_subject.Validate(Valid).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void First_Name_Is_Required(string firstName)
    {
        AssertInvalid(Valid with { FirstName = firstName }, nameof(RegisterCustomerCommand.FirstName));
    }

    [Fact]
    public void First_Name_Is_Capped_At_50_Characters()
    {
        AssertInvalid(Valid with { FirstName = new string('a', 51) }, nameof(RegisterCustomerCommand.FirstName));
        Assert.True(_subject.Validate(Valid with { FirstName = new string('a', 50) }).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Last_Name_Is_Required(string lastName)
    {
        AssertInvalid(Valid with { LastName = lastName }, nameof(RegisterCustomerCommand.LastName));
    }

    [Fact]
    public void Last_Name_Is_Capped_At_50_Characters()
    {
        AssertInvalid(Valid with { LastName = new string('a', 51) }, nameof(RegisterCustomerCommand.LastName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("cam@")]
    public void Email_Must_Be_Present_And_Well_Formed(string email)
    {
        AssertInvalid(Valid with { Email = email }, nameof(RegisterCustomerCommand.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789")]          // E.164 forbids a leading zero
    [InlineData("+1 555 123 4567")]     // no spaces
    [InlineData("555-1234")]
    [InlineData("+1234567890123456")]   // 16 digits, one over the E.164 maximum
    public void Phone_Number_Must_Match_E164(string phoneNumber)
    {
        AssertInvalid(Valid with { PhoneNumber = phoneNumber }, nameof(RegisterCustomerCommand.PhoneNumber));
    }

    [Theory]
    [InlineData("+15551234567")]
    [InlineData("15551234567")]
    public void A_Well_Formed_Phone_Number_Passes(string phoneNumber)
    {
        Assert.True(_subject.Validate(Valid with { PhoneNumber = phoneNumber }).IsValid);
    }

    [Theory]
    [InlineData("", "Password is required.")]
    [InlineData("P@ss1a", "Password must be at least 8 characters long.")]
    [InlineData("p@ssw0rd!23", "Password must contain at least one uppercase letter.")]
    [InlineData("P@SSW0RD!23", "Password must contain at least one lowercase letter.")]
    [InlineData("P@sswordAbc!", "Password must contain at least one digit.")]
    [InlineData("Passw0rdAbc", "Password must contain at least one special character.")]
    public void Password_Rules_Are_Enforced_Individually(string password, string expectedMessage)
    {
        var result = _subject.Validate(Valid with { Password = password });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == expectedMessage);
    }

    [Fact]
    public void Every_Failed_Rule_Is_Reported_Not_Just_The_First()
    {
        // The handler joins these into the 400 message, so a caller who got three things wrong
        // should learn about all three in one round trip.
        var result = _subject.Validate(new RegisterCustomerCommand("", "", "not-an-email", "nope", "weak"));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 5, $"Expected at least 5 errors, got {result.Errors.Count}.");
    }

    private void AssertInvalid(RegisterCustomerCommand command, string propertyName)
    {
        var result = _subject.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == propertyName);
    }
}
