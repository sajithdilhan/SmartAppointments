using Booking.Application.Abstractions;
using Booking.Application.Handlers;
using Booking.Application.Models;
using Booking.Application.Queries;
using Booking.Application.Validations;
using Booking.Domain.Entities;
using Moq;

namespace Booking.Tests;

public class ListMyAppointmentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Guid _caller = Guid.CreateVersion7();

    public ListMyAppointmentsHandlerTests()
    {
        _appointments.Setup(r => r.ListForCustomerAsync(
                It.IsAny<Guid>(), It.IsAny<AppointmentStatus?>(), It.IsAny<AppointmentTimeFilter?>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentPage([], 0));
    }

    private Appointment Book(DateTime start) => Appointment.Book(
        Guid.CreateVersion7(), _caller, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        start, start.AddMinutes(30), Now.UtcDateTime.AddDays(-2));

    private Task<SmartAppointments.BuildingBlocks.Models.Result<PagedResponse<AppointmentResponse>>> Send(
        string? status = null, string? when = null, string? page = null, string? pageSize = null, TimeProvider? clock = null) =>
        new ListMyAppointmentsHandler(
            _appointments.Object, new ListMyAppointmentsQueryValidator(), clock ?? new FixedTimeProvider(Now))
            .Handle(new ListMyAppointmentsQuery(_caller, status, when, page, pageSize), CancellationToken.None);

    [Fact]
    public async Task Defaults_Reach_The_Repository_As_Page_1_Size_20_And_No_Filters()
    {
        var result = await Send();

        Assert.True(result.IsSuccess);
        _appointments.Verify(r => r.ListForCustomerAsync(
            _caller, null, null, Now.UtcDateTime, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, result.Value!.Page);
        Assert.Equal(20, result.Value.PageSize);
    }

    [Theory]
    [InlineData("Booked", AppointmentStatus.Booked)]
    [InlineData("booked", AppointmentStatus.Booked)]
    [InlineData("CANCELLED", AppointmentStatus.Cancelled)]
    public async Task A_Parsed_Status_Reaches_The_Repository(string raw, AppointmentStatus expected)
    {
        await Send(status: raw);

        _appointments.Verify(r => r.ListForCustomerAsync(
            _caller, expected, null, It.IsAny<DateTime>(), 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("upcoming", AppointmentTimeFilter.Upcoming)]
    [InlineData("Past", AppointmentTimeFilter.Past)]
    public async Task A_Parsed_When_Reaches_The_Repository(string raw, AppointmentTimeFilter expected)
    {
        await Send(when: raw);

        _appointments.Verify(r => r.ListForCustomerAsync(
            _caller, null, expected, It.IsAny<DateTime>(), 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Both_Filters_Page_And_PageSize_Reach_The_Repository_Together()
    {
        var result = await Send("Booked", "upcoming", "3", "5");

        _appointments.Verify(r => r.ListForCustomerAsync(
            _caller, AppointmentStatus.Booked, AppointmentTimeFilter.Upcoming, Now.UtcDateTime, 3, 5,
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(3, result.Value!.Page);
        Assert.Equal(5, result.Value.PageSize);
    }

    [Fact]
    public async Task The_Clock_Is_Read_Once()
    {
        var clock = new CountingTimeProvider(Now);

        await Send("Booked", "past", clock: clock);

        Assert.Equal(1, clock.Reads);
    }

    [Fact]
    public async Task Items_Map_Like_The_Single_Appointment_And_The_Total_Is_Carried()
    {
        var booked = Book(Now.UtcDateTime.AddDays(1));
        var cancelled = Book(Now.UtcDateTime.AddDays(2));
        cancelled.Cancel(Now.UtcDateTime);
        _appointments.Setup(r => r.ListForCustomerAsync(
                _caller, null, null, Now.UtcDateTime, 2, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentPage([booked, cancelled], 7));

        var result = await Send(page: "2", pageSize: "2");

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value!.TotalCount);
        Assert.Equal([AppointmentResponse.From(booked), AppointmentResponse.From(cancelled)], result.Value.Items);
        Assert.Equal(AppointmentStatus.Booked, result.Value.Items[0].Status);
        Assert.Equal(AppointmentStatus.Cancelled, result.Value.Items[1].Status);
    }

    [Fact]
    public async Task An_Empty_List_Is_A_Success_With_A_Zero_Total()
    {
        var result = await Send();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task A_Page_Past_The_End_Is_A_Success_With_The_Real_Total()
    {
        _appointments.Setup(r => r.ListForCustomerAsync(
                _caller, null, null, It.IsAny<DateTime>(), 9, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentPage([], 3));

        var result = await Send(page: "9");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(9, result.Value.Page);
        Assert.Equal(3, result.Value.TotalCount);
    }

    [Fact]
    public async Task Invalid_Input_Returns_One_400_Listing_Every_Failure_And_Never_Calls_The_Repository()
    {
        var result = await Send("x", "y", "0", "500");

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.Status);
        Assert.StartsWith("Invalid request data. Errors: ", result.Error.Details);
        Assert.Contains("The status must be Booked or Cancelled.", result.Error.Details);
        Assert.Contains("The when must be upcoming or past.", result.Error.Details);
        Assert.Contains("The page must be a whole number of at least 1.", result.Error.Details);
        Assert.Contains("The pageSize must be a whole number from 1 to 100.", result.Error.Details);
        _appointments.VerifyNoOtherCalls();
    }

    private sealed class CountingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return now;
        }
    }
}
