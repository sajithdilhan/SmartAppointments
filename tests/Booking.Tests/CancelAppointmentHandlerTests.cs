using Booking.Application.Abstractions;
using Booking.Application.Commands;
using Booking.Application.Handlers;
using Booking.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Tests;

public class CancelAppointmentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IAvailabilityClient> _availability = new();
    private readonly Guid _owner = Guid.CreateVersion7();
    private readonly Appointment _appointment;

    public CancelAppointmentHandlerTests()
    {
        _appointment = Book(Now.UtcDateTime.AddDays(1));
        _appointments.Setup(r => r.GetByIdAsync(_appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_appointment);
        _availability.Setup(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
    }

    private Appointment Book(DateTime start) => Appointment.Book(
        Guid.CreateVersion7(), _owner, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        start, start.AddMinutes(30), Now.UtcDateTime.AddDays(-2));

    private Task<Result<bool>> Send(Guid caller, string role, Guid? id = null) =>
        new CancelAppointmentCommandHandler(
            _appointments.Object, _availability.Object, new FixedTimeProvider(Now), NullLogger<CancelAppointmentCommandHandler>.Instance)
            .Handle(new CancelAppointmentCommand(id ?? _appointment.Id, caller, role), CancellationToken.None);

    [Fact]
    public async Task The_Owner_Cancels_Saves_Then_Releases()
    {
        var calls = new List<string>();
        _appointments.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).Returns(Task.CompletedTask);
        _availability.Setup(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("release")).ReturnsAsync(Result<bool>.Success(true));

        var result = await Send(_owner, Constants.CustomerRole);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, _appointment.Status);
        Assert.Equal(Now.UtcDateTime, _appointment.CancelledAtUtc);
        Assert.Equal(["save", "release"], calls);
        _availability.Verify(a => a.ReleaseAsync(_appointment.SlotId, _appointment.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Admin_Can_Cancel_Anyones_Appointment()
    {
        var result = await Send(Guid.CreateVersion7(), Constants.AdminRole);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, _appointment.Status);
    }

    [Theory]
    [InlineData(Constants.StaffRole)]
    [InlineData(Constants.CustomerRole)]
    public async Task Staff_And_Other_Customers_Who_Are_Not_The_Owner_Get_404_And_Change_Nothing(string role)
    {
        var result = await Send(Guid.CreateVersion7(), role);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal(AppointmentStatus.Booked, _appointment.Status);
        _appointments.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _availability.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_Unknown_Appointment_Is_404()
    {
        var unknown = Guid.CreateVersion7();
        _appointments.Setup(r => r.GetByIdAsync(unknown, It.IsAny<CancellationToken>())).ReturnsAsync((Appointment?)null);

        var result = await Send(_owner, Constants.CustomerRole, unknown);

        Assert.Equal(404, result.Error!.Status);
    }

    [Fact]
    public async Task Cancelling_At_Or_After_The_Start_Is_409()
    {
        var started = Book(Now.UtcDateTime);
        _appointments.Setup(r => r.GetByIdAsync(started.Id, It.IsAny<CancellationToken>())).ReturnsAsync(started);

        var result = await Send(_owner, Constants.CustomerRole, started.Id);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal(AppointmentStatus.Booked, started.Status);
        _availability.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_Release_Failure_Is_503_And_The_Appointment_Stays_Cancelled()
    {
        _availability.Setup(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(503, "down")));

        var result = await Send(_owner, Constants.CustomerRole);

        Assert.Equal(503, result.Error!.Status);
        Assert.Equal(AppointmentStatus.Cancelled, _appointment.Status);
        _appointments.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Repeat_Cancel_Re_Releases_Without_Saving_Even_After_The_Start()
    {
        var started = Book(Now.UtcDateTime.AddMinutes(-10));
        started.Cancel(Now.UtcDateTime.AddHours(-1));
        _appointments.Setup(r => r.GetByIdAsync(started.Id, It.IsAny<CancellationToken>())).ReturnsAsync(started);

        var result = await Send(_owner, Constants.CustomerRole, started.Id);

        Assert.True(result.IsSuccess);
        _availability.Verify(a => a.ReleaseAsync(started.SlotId, started.Id, It.IsAny<CancellationToken>()), Times.Once);
        _appointments.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
