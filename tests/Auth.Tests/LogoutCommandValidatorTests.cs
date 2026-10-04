using Auth.Application.Commands;
using Auth.Application.Validations;

namespace Auth.Tests;

public class LogoutCommandValidatorTests
{
    private readonly LogoutCommandValidator _subject = new();

    [Fact]
    public void A_Token_Passes()
    {
        Assert.True(_subject.Validate(new LogoutCommand("some-token")).IsValid);
    }

    [Fact]
    public void A_Null_Token_Fails()
    {
        var result = _subject.Validate(new LogoutCommand(null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Refresh token is required.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Empty_Or_Whitespace_Token_Passes(string token)
    {
        // It ends as 204 in the handler: logout is idempotent and never reveals what exists.
        Assert.True(_subject.Validate(new LogoutCommand(token)).IsValid);
    }
}
