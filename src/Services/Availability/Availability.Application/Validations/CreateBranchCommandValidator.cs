using Availability.Application.Commands;
using FluentValidation;
using System.Text.RegularExpressions;

namespace Availability.Application.Validations;

public class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    // 2-10 letters, digits and hyphens, not starting or ending with a hyphen.
    private static readonly Regex CodePattern =
        new(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,8}[A-Za-z0-9])$", RegexOptions.Compiled);

    public CreateBranchCommandValidator()
    {
        // Matched after trimming because the trimmed value is what gets stored: " pg " is a valid
        // way of writing PG. Cascade stops the pattern from running against a null.
        RuleFor(x => x.Code)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Code is required.")
            .Must(code => CodePattern.IsMatch(code.Trim()))
            .WithMessage("Code must be 2-10 letters, digits or hyphens, and cannot start or end with a hyphen.");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");
        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");
        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Address is required.")
            .MaximumLength(200).WithMessage("Address cannot exceed 200 characters.");
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required.")
            .Matches(@"^\+?[1-9]\d{1,14}$").WithMessage("Invalid phone number format.");
    }
}
