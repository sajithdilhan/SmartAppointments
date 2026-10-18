using Booking.Application.Models;
using Booking.Domain.Entities;

namespace Booking.Tests;

public class BranchAppointmentsParametersTests
{
    [Fact]
    public void An_Omitted_Date_Parses_To_Null()
    {
        Assert.True(BranchAppointmentsParameters.TryParseDate(null, out var date));
        Assert.Null(date);
    }

    [Fact]
    public void A_Valid_Date_Parses()
    {
        Assert.True(BranchAppointmentsParameters.TryParseDate("2030-03-10", out var date));
        Assert.Equal(new DateOnly(2030, 3, 10), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2030-3-10")]
    [InlineData("10/03/2030")]
    [InlineData("2030-02-30")]
    [InlineData("2030-13-01")]
    [InlineData("20300310")]
    [InlineData("2030-03-10T00:00:00")]
    [InlineData(" 2030-03-10")]
    [InlineData("tomorrow")]
    public void Any_Other_Date_Is_Rejected(string raw)
    {
        Assert.False(BranchAppointmentsParameters.TryParseDate(raw, out _));
    }

    [Theory]
    [InlineData("Booked", AppointmentStatus.Booked)]
    [InlineData("cancelled", AppointmentStatus.Cancelled)]
    [InlineData("Completed", AppointmentStatus.Completed)]
    [InlineData("NOSHOW", AppointmentStatus.NoShow)]
    [InlineData(null, null)]
    public void The_Four_Statuses_Parse(string? raw, AppointmentStatus? expected)
    {
        Assert.True(BranchAppointmentsParameters.TryParseStatus(raw, out var status));
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("Pending")]
    public void Any_Other_Status_Is_Rejected(string raw)
    {
        Assert.False(BranchAppointmentsParameters.TryParseStatus(raw, out _));
    }

    [Fact]
    public void Page_And_Page_Size_Share_The_My_Appointments_Rules()
    {
        Assert.True(BranchAppointmentsParameters.TryParsePage(null, out var page));
        Assert.Equal(1, page);
        Assert.True(BranchAppointmentsParameters.TryParsePageSize(null, out var size));
        Assert.Equal(20, size);
        Assert.False(BranchAppointmentsParameters.TryParsePage("0", out _));
        Assert.False(BranchAppointmentsParameters.TryParsePageSize("101", out _));
        Assert.True(BranchAppointmentsParameters.TryParsePageSize("100", out _));
    }
}
