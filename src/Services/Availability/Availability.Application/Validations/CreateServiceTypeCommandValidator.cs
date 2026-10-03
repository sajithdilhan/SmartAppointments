using Availability.Application.Commands;
using FluentValidation;

namespace Availability.Application.Validations;

public class CreateServiceTypeCommandValidator : AbstractValidator<CreateServiceTypeCommand>
{
    public CreateServiceTypeCommandValidator()
    {
        ServiceTypeRules.Code(RuleFor(x => x.Code));
        ServiceTypeRules.Name(RuleFor(x => x.Name));
        ServiceTypeRules.Description(RuleFor(x => x.Description));
        ServiceTypeRules.Duration(RuleFor(x => x.DurationMinutes));
    }
}
