using Auth.Application.Commands;
using FluentValidation;

namespace Auth.Application.Validations;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        // NotNull only: an empty or whitespace token passes and ends as 204 in the handler.
        RuleFor(x => x.RefreshToken)
            .NotNull().WithMessage("Refresh token is required.");
    }
}
