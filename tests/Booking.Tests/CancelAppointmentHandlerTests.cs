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
        _appointments.Setup(r => r.GetByIdNoTrackingAsync(_appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_appointment);
        _appointments.Setup(r => r.TryTransitionAsync(It.IsAny<Guid>(), It.IsAny<AppointmentStatus>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _availability.Setup(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
    }

    private Appointment Book(DateTime start) => Appointment.Book(
        Guid.CreateVersion7(), _owner, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        start, start.AddMinutes(30), Now.UtcDateTime.AddDays(-2));

    private void Returns(Appointment appointment) =>
        _appointments.Setup(r => r.GetByIdNoTrackingAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);

    private Task<Result<bool>> Send(Guid caller, string role, Guid? id = null) =>
        new CancelAppointmentCommandHandler(
            _appointments.Object, _availability.Object, new FixedTimeProvider(Now), NullLogger<CancelAppointmentCommandHandler>.Instance)
            .Handle(new CancelAppointmentCommand(id ?? _appointment.Id, caller, role), CancellationToken.None);

    private void VerifyNoTransition() =>
        _appointments.Verify(r => r.TryTransitionAsync(It.IsAny<Guid>(), It.IsAny<AppointmentStatus>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task The_Owner_Cancels_Transitions_Then_Releases()
    {
        var calls = new List<string>();
        _appointments.Setup(r => r.TryTransitionAsync(_appointment.Id, AppointmentStatus.Cancelled, Now.UtcDateTime, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("transition")).ReturnsAsync(true);
        _availability.Setup(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("release")).ReturnsAsync(Result<bool>.Success(true));

        var result = await Send(_owner, Constants.CustomerRole);

        Assert.True(result.IsSuccess);
        Assert.Equal(["transition", "release"], calls);
        _availability.Verify(a => a.ReleaseAsync(_appointment.SlotId, _appointment.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Admin_Can_Cancel_Anyones_Appointment()
    {
        var result = await Send(Guid.CreateVersion7(), Constants.AdminRole);

        Assert.True(result.IsSuccess);
        _appointments.Verify(r => r.TryTransitionAsync(_appointment.Id, AppointmentStatus.Cancelled, Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(Constants.StaffRole)]
    [InlineData(Constants.CustomerRole)]
    public async Task Staff_And_Other_Customers_Who_Are_Not_The_Owner_Get_404_And_Change_Nothing(string role)
    {
        var result = await Send(Guid.CreateVersion7(), role);

        Assert.Equal(404, result.Error!.Status);
        VerifyNoTransition();
        _availability.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_Unknown_Appointment_Is_404()
    {
        var unknown = Guid.CreateVersion7();
        _appointments.Setup(r => r.GetByIdNoTrackingAsync(unknown, It.IsAny<CancellationToken>())).ReturnsAsync((Appointment?)null);

        var result = await Send(_owner, Constants.CustomerRole, unknown);

        Assert.Equal(404, result.Error!.Status);
    }

    [Fact]
    public async Task Cancelling_At_Or_After_The_Start_Is_409()
    {
        var started = Book(Now.UtcDateTime);
        Returns(started);

        var result = await Send(_owner, Constants.CustomerRole, started.Id);

        Assert.Equal(409, result.Error!.Status);
        VerifyNoTransition();
        _availability.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_Release_Failure_Is_503_After_The_Transition()
    {
        _availability.Setup(a => a.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(503, "down")));

        var result = await Send(_owner, Constants.CustomerRole);

        Assert.Equal(503, result.Error!.Status);
        _appointments.Verify(r => r.TryTransitionAsync(_appointment.Id, AppointmentStatus.Cancelled, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Repeat_Cancel_Re_Releases_Without_Transitioning_Even_After_The_Start()
    {
        var started = Book(Now.UtcDateTime.AddMinutes(-10));
        started.Cancel(Now.UtcDateTime.AddHours(-1));
        Returns(started);

        var result = await Send(_owner, Constants.CustomerRole, started.Id);

        Assert.True(result.IsSuccess);
        _availability.Verify(a => a.ReleaseAsync(started.SlotId, started.Id, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoTransition();
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed, "The appointment is Completed and can no longer be cancelled.")]
    [InlineData(AppointmentStatus.NoShow, "The appointment is NoShow and can no longer be cancelled.")]
    public async Task A_Completed_Or_NoShow_Appointment_Is_409_And_Nothing_Changes(AppointmentStatus status, string detail)
    {
        var started = Book(Now.UtcDateTime.AddMinutes(-10));
        started.WithStatus(status, Now.UtcDateTime);
        Returns(started);

        var result = await Send(_owner, Constants.CustomerRole, started.Id);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal(detail, result.Error.Details);
        VerifyNoTransition();
        _availability.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Losing_The_Race_To_Another_Cancel_Takes_The_Repeat_Path_And_Releases()
    {
        // Same id: first read says Booked, the re-read says Cancelled.
        _appointments.SetupSequence(r => r.GetByIdNoTrackingAsync(_appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_appointment)
            .ReturnsAsync(Cancelled(_appointment));
        _appointments.Setup(r => r.TryTransitionAsync(_appointment.Id, AppointmentStatus.Cancelled, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Send(_owner, Constants.CustomerRole);

        Assert.True(result.IsSuccess);
        _availability.Verify(a => a.ReleaseAsync(_appointment.SlotId, _appointment.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Losing_The_Race_To_A_Mark_Is_409_Without_Releasing()
    {
        var completed = Appointment.Book(
            _appointment.Id, _owner, _appointment.SlotId, _appointment.BranchId, _appointment.ServiceTypeId,
            _appointment.StartUtc, _appointment.EndUtc, Now.UtcDateTime.AddDays(-2));
        completed.WithStatus(AppointmentStatus.Completed, _appointment.StartUtc);
        _appointments.SetupSequence(r => r.GetByIdNoTrackingAsync(_appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_appointment)
            .ReturnsAsync(completed);
        _appointments.Setup(r => r.TryTransitionAsync(_appointment.Id, AppointmentStatus.Cancelled, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Send(_owner, Constants.CustomerRole);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("The appointment is Completed and can no longer be cancelled.", result.Error.Details);
        _availability.VerifyNoOtherCalls();
    }

    private Appointment Cancelled(Appointment source)
    {
        var copy = Appointment.Book(
            source.Id, source.CustomerId, source.SlotId, source.BranchId, source.ServiceTypeId,
            source.StartUtc, source.EndUtc, Now.UtcDateTime.AddDays(-2));
        copy.Cancel(Now.UtcDateTime);
        return copy;
    }
}
