using Auth.Application.Commands;
using FluentValidation;

namespace Auth.Application.Validations;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        // Deliberately no MaximumLength: an over-long token is a 401, not a 400.
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.");
    }
}
