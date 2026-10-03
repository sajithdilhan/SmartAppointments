using FluentValidation;
using System.Text.RegularExpressions;

namespace Availability.Application.Validations;

// The rules shared by the create and update validators, so the two cannot drift apart.
internal static class ServiceTypeRules
{
    public const int MinimumDurationMinutes = 5;
    public const int MaximumDurationMinutes = 480;
    public const int DurationStepMinutes = 5;

    // 2-30 letters, digits, hyphens and underscores, not starting or ending with either separator.
    private static readonly Regex CodePattern =
        new(@"^[A-Za-z0-9](?:[A-Za-z0-9_-]{0,28}[A-Za-z0-9])$", RegexOptions.Compiled);

    // Matched after trimming because the trimmed value is what gets stored. Cascade stops the
    // pattern from running against a null.
    public static void Code<T>(IRuleBuilderInitial<T, string> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Code is required.")
            .Must(code => CodePattern.IsMatch(code.Trim()))
            .WithMessage("Code must be 2-30 letters, digits, hyphens or underscores, and cannot start or end with a hyphen or underscore.");

    public static void Name<T>(IRuleBuilderInitial<T, string> rule) =>
        rule.NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

    public static void Description<T>(IRuleBuilderInitial<T, string?> rule) =>
        rule.MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");

    public static void Duration<T>(IRuleBuilderInitial<T, int> rule) =>
        rule.InclusiveBetween(MinimumDurationMinutes, MaximumDurationMinutes)
            .WithMessage($"Duration must be between {MinimumDurationMinutes} and {MaximumDurationMinutes} minutes.")
            .Must(minutes => minutes % DurationStepMinutes == 0)
            .WithMessage($"Duration must be a multiple of {DurationStepMinutes} minutes.");
}
