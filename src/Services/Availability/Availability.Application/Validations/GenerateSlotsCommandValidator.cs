using Availability.Application.Commands;
using FluentValidation;

namespace Availability.Application.Validations;

public class GenerateSlotsCommandValidator : AbstractValidator<GenerateSlotsCommand>
{
    private const int MaxDaysSpanned = 31;

    public GenerateSlotsCommandValidator()
    {
        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Branch id is required.");
        RuleFor(x => x.ServiceTypeId)
            .NotEmpty().WithMessage("Service type id is required.");
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate).WithMessage("From date cannot be after to date.");
        RuleFor(x => x)
            .Must(x => x.ToDate.DayNumber - x.FromDate.DayNumber < MaxDaysSpanned)
            .WithMessage($"The date range cannot span more than {MaxDaysSpanned} days.")
            .When(x => x.FromDate <= x.ToDate);
        RuleFor(x => x.Capacity)
            .InclusiveBetween(1, 100).WithMessage("Capacity must be between 1 and 100.");
    }
}
