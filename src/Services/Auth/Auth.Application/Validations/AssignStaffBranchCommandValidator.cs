using Auth.Application.Commands;
using FluentValidation;

namespace Auth.Application.Validations;

public class AssignStaffBranchCommandValidator : AbstractValidator<AssignStaffBranchCommand>
{
    public AssignStaffBranchCommandValidator()
    {
        RuleFor(x => x.BranchId)
            .NotEqual(Guid.Empty).WithMessage("Branch is required.");
    }
}
