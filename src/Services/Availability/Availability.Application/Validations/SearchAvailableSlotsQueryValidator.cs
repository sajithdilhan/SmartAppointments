using Availability.Application.Queries;
using FluentValidation;

namespace Availability.Application.Validations;

public class SearchAvailableSlotsQueryValidator : AbstractValidator<SearchAvailableSlotsQuery>
{
    public SearchAvailableSlotsQueryValidator()
    {
        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Branch id is required.");
        RuleFor(x => x.ServiceTypeId)
            .NotEmpty().WithMessage("Service id is required.");
        RuleFor(x => x.Date)
            .NotNull().WithMessage("Date is required.");
    }
}
