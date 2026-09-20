using Auth.Application.Commands;
using Auth.Application.Validations;

namespace Auth.Tests;

public class LoginUserRequestValidatorTests
{
    private readonly LoginUserRequestValidator _subject = new();

    [Fact]
    public void A_Valid_Command_Passes()
    {
        Assert.True(_subject.Validate(new LoginUserCommand("cam@example.com", "P@ssw0rd!23")).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public void Email_Must_Be_Present_And_Well_Formed(string email)
    {
        var result = _subject.Validate(new LoginUserCommand(email, "P@ssw0rd!23"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginUserCommand.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Password_Is_Required(string password)
    {
        var result = _subject.Validate(new LoginUserCommand("cam@example.com", password));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginUserCommand.Password));
    }

    [Fact]
    public void Login_Does_Not_Apply_The_Registration_Password_Rules()
    {
        // Login must accept any non-empty password: applying the registration complexity rules
        // here would lock out accounts created before the rules, and would leak the rules to
        // anyone probing the endpoint.
        Assert.True(_subject.Validate(new LoginUserCommand("cam@example.com", "old")).IsValid);
    }
}
