using Availability.Application.Commands;
using Availability.Application.Models;
using FluentValidation;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Availability.Application.Validations;

public class SetBranchScheduleCommandValidator : AbstractValidator<SetBranchScheduleCommand>
{
    private const string TimePattern = @"^([01]\d|2[0-3]):[0-5]\d$";

    public SetBranchScheduleCommandValidator()
    {
        // IANA ids only: they resolve the same on Windows and Linux, a Windows id would not.
        RuleFor(x => x.TimeZoneId)
            .Must(BeAnIanaTimeZone).WithMessage("Time zone must be an IANA time zone identifier, such as 'Asia/Colombo'.");

        RuleFor(x => x.WorkingHours)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Working hours are required; use an empty list to close every day.")
            .Must(hours => hours.Select(h => h.DayOfWeek).Distinct().Count() == hours.Count)
            .WithMessage("A day of the week can be listed only once.");

        RuleForEach(x => x.WorkingHours).ChildRules(hours =>
        {
            hours.RuleFor(h => h.DayOfWeek)
                .IsInEnum().WithMessage("Day of week is not valid.");
            hours.RuleFor(h => h.OpensAt)
                .NotEmpty().Matches(TimePattern).WithMessage("Opening time must be a 24-hour time in the form HH:mm.");
            hours.RuleFor(h => h.ClosesAt)
                .NotEmpty().Matches(TimePattern).WithMessage("Closing time must be a 24-hour time in the form HH:mm.");
            hours.RuleFor(h => h)
                .Must(OpenBeforeClose).WithMessage("Opening time must be before closing time.")
                .When(h => TryParse(h.OpensAt, out _) && TryParse(h.ClosesAt, out _));
        }).When(x => x.WorkingHours is not null);
    }

    private static bool BeAnIanaTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
        && zone.HasIanaId;

    private static bool OpenBeforeClose(WorkingHoursRequest hours) =>
        TryParse(hours.OpensAt, out var opens) && TryParse(hours.ClosesAt, out var closes) && opens < closes;

    // Only strict HH:mm counts as a time, so the "when valid" guards agree with the format rule.
    private static bool TryParse(string? value, out TimeOnly time)
    {
        time = default;
        return value is { Length: 5 }
            && Regex.IsMatch(value, TimePattern)
            && TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}
