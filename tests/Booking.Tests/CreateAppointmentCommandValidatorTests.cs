using Booking.Application.Commands;
using Booking.Application.Validations;

namespace Booking.Tests;

public class CreateAppointmentCommandValidatorTests
{
    private static readonly CreateAppointmentCommand Valid = new(Guid.CreateVersion7(), "key-1", Guid.CreateVersion7());

    private readonly CreateAppointmentCommandValidator _validator = new();

    [Fact]
    public void A_Valid_Command_Passes()
    {
        Assert.True(_validator.Validate(Valid).IsValid);
    }

    [Fact]
    public void A_Key_Of_128_Characters_Passes()
    {
        Assert.True(_validator.Validate(Valid with { IdempotencyKey = new string('k', 128) }).IsValid);
    }

    [Fact]
    public void A_Key_Of_129_Characters_Is_Rejected()
    {
        var result = _validator.Validate(Valid with { IdempotencyKey = new string('k', 129) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at most 128"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Missing_Or_Blank_Key_Is_Rejected(string? key)
    {
        var result = _validator.Validate(Valid with { IdempotencyKey = key });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "The Idempotency-Key header is required.");
    }

    [Fact]
    public void An_Empty_Slot_Id_Is_Rejected()
    {
        var result = _validator.Validate(Valid with { SlotId = Guid.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Slot id is required.");
    }
}
