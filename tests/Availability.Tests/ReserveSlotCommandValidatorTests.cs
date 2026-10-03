using Availability.Application.Commands;
using Availability.Application.Validations;

namespace Availability.Tests;

public class ReserveSlotCommandValidatorTests
{
    private static readonly ReserveSlotCommand Valid = new(Guid.CreateVersion7(), Guid.CreateVersion7());

    private readonly ReserveSlotCommandValidator _validator = new();

    [Fact]
    public void A_Valid_Command_Passes()
    {
        Assert.True(_validator.Validate(Valid).IsValid);
    }

    [Fact]
    public void An_Empty_Slot_Id_Is_Rejected()
    {
        var result = _validator.Validate(Valid with { SlotId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Slot id is required.");
    }

    [Fact]
    public void An_Empty_Appointment_Id_Is_Rejected()
    {
        var result = _validator.Validate(Valid with { AppointmentId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Appointment id is required.");
    }
}
