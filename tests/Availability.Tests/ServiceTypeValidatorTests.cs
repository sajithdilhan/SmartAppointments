using Availability.Application.Commands;
using Availability.Application.Validations;

namespace Availability.Tests;

public class ServiceTypeValidatorTests
{
    private static readonly CreateServiceTypeCommand ValidCreate = new("INSPECT", "Inspection", "Bring the logbook", 30);
    private static readonly UpdateServiceTypeCommand ValidUpdate = new(Guid.CreateVersion7(), "Inspection", null, 30);

    private readonly CreateServiceTypeCommandValidator _create = new();
    private readonly UpdateServiceTypeCommandValidator _update = new();

    [Fact]
    public void Fully_Valid_Commands_Pass()
    {
        Assert.True(_create.Validate(ValidCreate).IsValid);
        Assert.True(_update.Validate(ValidUpdate).IsValid);
    }

    [Theory]
    [InlineData("PG")]
    [InlineData(" inspect ")]
    [InlineData("VEHICLE_INSPECTION")]
    [InlineData("PASSPORT-RENEWAL")]
    [InlineData("ABCDEFGHIJABCDEFGHIJABCDEFGHIJ")]
    public void Valid_Codes_Are_Accepted(string code)
    {
        Assert.True(_create.Validate(ValidCreate with { Code = code }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJABCDEFGHIJABCDEFGHIJK")]
    [InlineData("_INSPECT")]
    [InlineData("INSPECT-")]
    [InlineData("IN SPECT")]
    [InlineData("INSPECT!")]
    public void Invalid_Codes_Are_Rejected(string? code)
    {
        Assert.False(_create.Validate(ValidCreate with { Code = code! }).IsValid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(7, false)]
    [InlineData(480, true)]
    [InlineData(485, false)]
    public void Duration_Is_5_To_480_Minutes_In_Steps_Of_5(int minutes, bool valid)
    {
        Assert.Equal(valid, _create.Validate(ValidCreate with { DurationMinutes = minutes }).IsValid);
        Assert.Equal(valid, _update.Validate(ValidUpdate with { DurationMinutes = minutes }).IsValid);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Name_Is_At_Most_100_Characters(int length, bool valid)
    {
        Assert.Equal(valid, _create.Validate(ValidCreate with { Name = new string('a', length) }).IsValid);
        Assert.Equal(valid, _update.Validate(ValidUpdate with { Name = new string('a', length) }).IsValid);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Description_Is_At_Most_500_Characters(int length, bool valid)
    {
        Assert.Equal(valid, _create.Validate(ValidCreate with { Description = new string('a', length) }).IsValid);
        Assert.Equal(valid, _update.Validate(ValidUpdate with { Description = new string('a', length) }).IsValid);
    }

    [Fact]
    public void An_Empty_Name_Is_Rejected()
    {
        Assert.False(_create.Validate(ValidCreate with { Name = "" }).IsValid);
        Assert.False(_update.Validate(ValidUpdate with { Name = " " }).IsValid);
    }
}
