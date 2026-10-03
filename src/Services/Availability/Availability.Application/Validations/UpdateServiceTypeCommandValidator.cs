using Availability.Application.Commands;
using FluentValidation;

namespace Availability.Application.Validations;

// The create rules minus the code, which cannot be changed.
public class UpdateServiceTypeCommandValidator : AbstractValidator<UpdateServiceTypeCommand>
{
    public UpdateServiceTypeCommandValidator()
    {
        ServiceTypeRules.Name(RuleFor(x => x.Name));
        ServiceTypeRules.Description(RuleFor(x => x.Description));
        ServiceTypeRules.Duration(RuleFor(x => x.DurationMinutes));
    }
}
