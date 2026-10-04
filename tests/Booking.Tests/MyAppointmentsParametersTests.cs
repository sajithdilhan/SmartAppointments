using Booking.Application.Models;
using Booking.Domain.Entities;

namespace Booking.Tests;

public class MyAppointmentsParametersTests
{
    [Theory]
    [InlineData("Booked", AppointmentStatus.Booked)]
    [InlineData("booked", AppointmentStatus.Booked)]
    [InlineData("CANCELLED", AppointmentStatus.Cancelled)]
    [InlineData(null, null)]
    public void A_Valid_Or_Omitted_Status_Parses(string? raw, AppointmentStatus? expected)
    {
        Assert.True(MyAppointmentsParameters.TryParseStatus(raw, out var status));
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Completed")]
    [InlineData("NoShow")]
    [InlineData("1")]
    [InlineData(" Booked")]
    [InlineData("Booked,Cancelled")]
    public void Any_Other_Status_Is_Rejected(string raw)
    {
        Assert.False(MyAppointmentsParameters.TryParseStatus(raw, out _));
    }

    [Theory]
    [InlineData("upcoming", AppointmentTimeFilter.Upcoming)]
    [InlineData("PAST", AppointmentTimeFilter.Past)]
    [InlineData(null, null)]
    public void A_Valid_Or_Omitted_When_Parses(string? raw, AppointmentTimeFilter? expected)
    {
        Assert.True(MyAppointmentsParameters.TryParseWhen(raw, out var when));
        Assert.Equal(expected, when);
    }

    [Theory]
    [InlineData("")]
    [InlineData("now")]
    [InlineData("0")]
    public void Any_Other_When_Is_Rejected(string raw)
    {
        Assert.False(MyAppointmentsParameters.TryParseWhen(raw, out _));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("1", 1)]
    [InlineData("7", 7)]
    [InlineData("2147483647", int.MaxValue)]
    public void A_Valid_Or_Omitted_Page_Parses(string? raw, int expected)
    {
        Assert.True(MyAppointmentsParameters.TryParsePage(raw, out var page));
        Assert.Equal(expected, page);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("+1")]
    [InlineData("2147483648")]
    [InlineData("")]
    public void An_Invalid_Page_Is_Rejected(string raw)
    {
        Assert.False(MyAppointmentsParameters.TryParsePage(raw, out _));
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    public void A_Valid_Or_Omitted_PageSize_Parses(string? raw, int expected)
    {
        Assert.True(MyAppointmentsParameters.TryParsePageSize(raw, out var pageSize));
        Assert.Equal(expected, pageSize);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("abc")]
    [InlineData("")]
    public void An_Invalid_PageSize_Is_Rejected(string raw)
    {
        Assert.False(MyAppointmentsParameters.TryParsePageSize(raw, out _));
    }
}
