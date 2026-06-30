using Auth.Application.Commands;
using FluentValidation;

namespace Auth.Application.Validations;

public sealed class LoginUserRequestValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email format.");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
