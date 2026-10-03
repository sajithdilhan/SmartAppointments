using Availability.Application.Commands;
using Availability.Application.Models;
using Availability.Application.Validations;

namespace Availability.Tests;

public class SetBranchScheduleCommandValidatorTests
{
    private static readonly SetBranchScheduleCommand Valid = new(
        Guid.CreateVersion7(),
        "Asia/Colombo",
        [new WorkingHoursRequest(DayOfWeek.Monday, "09:00", "17:00"), new WorkingHoursRequest(DayOfWeek.Sunday, "00:00", "23:59")]);

    private readonly SetBranchScheduleCommandValidator _validator = new();

    private static SetBranchScheduleCommand WithHours(params WorkingHoursRequest[] hours) => Valid with { WorkingHours = hours };

    [Fact]
    public void A_Valid_Schedule_Passes()
    {
        Assert.True(_validator.Validate(Valid).IsValid);
    }

    [Fact]
    public void An_Empty_List_Is_Valid_And_Closes_Every_Day()
    {
        Assert.True(_validator.Validate(WithHours()).IsValid);
    }

    [Fact]
    public void A_Null_List_Is_Invalid()
    {
        Assert.False(_validator.Validate(Valid with { WorkingHours = null! }).IsValid);
    }

    [Theory]
    [InlineData("Europe/London")]
    [InlineData("America/New_York")]
    public void Known_Iana_Zones_Pass(string zone)
    {
        Assert.True(_validator.Validate(Valid with { TimeZoneId = zone }).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("Sri Lanka Standard Time")]
    public void Unknown_And_Windows_Style_Zones_Are_Rejected(string zone)
    {
        Assert.False(_validator.Validate(Valid with { TimeZoneId = zone }).IsValid);
    }

    [Fact]
    public void A_Null_Zone_Is_Rejected()
    {
        Assert.False(_validator.Validate(Valid with { TimeZoneId = null! }).IsValid);
    }

    [Fact]
    public void A_Day_Listed_Twice_Is_Rejected()
    {
        var result = _validator.Validate(WithHours(
            new WorkingHoursRequest(DayOfWeek.Monday, "09:00", "12:00"),
            new WorkingHoursRequest(DayOfWeek.Monday, "13:00", "17:00")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("only once"));
    }

    [Fact]
    public void An_Unknown_Day_Is_Rejected()
    {
        Assert.False(_validator.Validate(WithHours(new WorkingHoursRequest((DayOfWeek)9, "09:00", "17:00"))).IsValid);
    }

    [Theory]
    [InlineData("9:00", "17:00")]
    [InlineData("09:00", "24:00")]
    [InlineData("09:00", "12:60")]
    [InlineData("09:00:00", "17:00")]
    [InlineData("nine", "17:00")]
    [InlineData("", "17:00")]
    public void Malformed_Times_Are_Rejected(string opensAt, string closesAt)
    {
        var result = _validator.Validate(WithHours(new WorkingHoursRequest(DayOfWeek.Monday, opensAt, closesAt)));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("09:00", "09:00")]
    [InlineData("17:00", "09:00")]
    public void Opening_At_Or_After_Closing_Is_Rejected(string opensAt, string closesAt)
    {
        var result = _validator.Validate(WithHours(new WorkingHoursRequest(DayOfWeek.Monday, opensAt, closesAt)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("before closing"));
    }

    [Fact]
    public void Every_Failed_Rule_Is_Reported()
    {
        var result = _validator.Validate(new SetBranchScheduleCommand(
            Guid.CreateVersion7(),
            "Sri Lanka Standard Time",
            [new WorkingHoursRequest(DayOfWeek.Monday, "9:00", "17:00"), new WorkingHoursRequest(DayOfWeek.Monday, "10:00", "09:00")]));

        Assert.True(result.Errors.Count >= 3);
    }
}
