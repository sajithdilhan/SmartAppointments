using Auth.Application.Commands;
using Auth.Application.Validations;

namespace Auth.Tests;

public class RefreshTokenCommandValidatorTests
{
    private readonly RefreshTokenCommandValidator _subject = new();

    [Fact]
    public void A_Token_Passes()
    {
        Assert.True(_subject.Validate(new RefreshTokenCommand("some-token")).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Missing_Empty_Or_Whitespace_Token_Fails(string? token)
    {
        var result = _subject.Validate(new RefreshTokenCommand(token));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Refresh token is required.");
    }

    [Fact]
    public void An_Over_Long_Token_Is_Not_A_Validation_Failure()
    {
        // It must be a 401 from the handler, not a 400.
        Assert.True(_subject.Validate(new RefreshTokenCommand(new string('a', 1000))).IsValid);
    }
}
