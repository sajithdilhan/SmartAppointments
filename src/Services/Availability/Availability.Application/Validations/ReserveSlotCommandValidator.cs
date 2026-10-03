using Availability.Application.Commands;
using FluentValidation;

namespace Availability.Application.Validations;

public class ReserveSlotCommandValidator : AbstractValidator<ReserveSlotCommand>
{
    public ReserveSlotCommandValidator()
    {
        RuleFor(x => x.SlotId)
            .NotEmpty().WithMessage("Slot id is required.");
        RuleFor(x => x.AppointmentId)
            .NotEmpty().WithMessage("Appointment id is required.");
    }
}
