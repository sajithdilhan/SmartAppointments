using Auth.Application.Queries;
using FluentValidation;

namespace Auth.Application.Validations;

public class LookupUsersQueryValidator : AbstractValidator<LookupUsersQuery>
{
    public const int MaxIds = 100;

    public LookupUsersQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull().WithMessage("Ids are required.")
            .Must(ids => ids is null || ids.Count >= 1).WithMessage("At least one id is required.")
            .Must(ids => ids is null || ids.Count <= MaxIds).WithMessage($"No more than {MaxIds} ids can be looked up at once.")
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty)).WithMessage("Ids cannot be empty GUIDs.");
    }
}
