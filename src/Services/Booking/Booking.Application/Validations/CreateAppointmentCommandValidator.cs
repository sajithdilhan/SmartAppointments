using Booking.Application.Commands;
using FluentValidation;

namespace Booking.Application.Validations;

public class CreateAppointmentCommandValidator : AbstractValidator<CreateAppointmentCommand>
{
    public const int MaxIdempotencyKeyLength = 128;

    public CreateAppointmentCommandValidator()
    {
        RuleFor(x => x.SlotId)
            .NotEmpty().WithMessage("Slot id is required.");
        RuleFor(x => x.IdempotencyKey)
            .Must(k => !string.IsNullOrWhiteSpace(k)).WithMessage("The Idempotency-Key header is required.")
            .Must(k => k is null || k.Length <= MaxIdempotencyKeyLength)
            .WithMessage($"The Idempotency-Key must be at most {MaxIdempotencyKeyLength} characters.");
    }
}
