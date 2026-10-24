using Auth.Application.Models;
using Auth.Application.Queries;
using FluentValidation;

namespace Auth.Application.Validations;

public class ListStaffQueryValidator : AbstractValidator<ListStaffQuery>
{
    public ListStaffQueryValidator()
    {
        RuleFor(x => x.BranchId)
            .Must(b => StaffListParameters.TryParseBranchId(b, out _))
            .WithMessage("The branchId must be a valid, non-empty GUID.");
        RuleFor(x => x.IsActive)
            .Must(a => StaffListParameters.TryParseIsActive(a, out _))
            .WithMessage("The isActive must be true or false.");
        RuleFor(x => x.Page)
            .Must(p => StaffListParameters.TryParsePage(p, out _))
            .WithMessage("The page must be a whole number of at least 1.");
        RuleFor(x => x.PageSize)
            .Must(p => StaffListParameters.TryParsePageSize(p, out _))
            .WithMessage($"The pageSize must be a whole number from 1 to {StaffListParameters.MaxPageSize}.");
    }
}
