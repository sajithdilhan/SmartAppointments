using Booking.Application.Abstractions;
using Booking.Application.Commands;
using Booking.Application.Handlers;
using Booking.Application.Models;
using Booking.Domain.Entities;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Tests;

public class MarkAppointmentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Guid _branch = Guid.CreateVersion7();
    private readonly Guid _id = Guid.CreateVersion7();

    private Appointment Make(AppointmentStatus status, DateTime? start = null)
    {
        var startUtc = start ?? Now.UtcDateTime.AddMinutes(-30);
        var a = Appointment.Book(
            _id, Guid.CreateVersion7(), Guid.CreateVersion7(), _branch, Guid.CreateVersion7(),
            startUtc, startUtc.AddMinutes(30), Now.UtcDateTime.AddDays(-2));
        switch (status)
        {
            case AppointmentStatus.Cancelled: a.Cancel(Now.UtcDateTime.AddHours(-1)); break;
            case AppointmentStatus.Completed: a.WithStatus(AppointmentStatus.Completed, Now.UtcDateTime); break;
            case AppointmentStatus.NoShow: a.WithStatus(AppointmentStatus.NoShow, Now.UtcDateTime); break;
        }

        return a;
    }

    private void Reads(params Appointment?[] reads)
    {
        var setup = _appointments.SetupSequence(r => r.GetByIdNoTrackingAsync(_id, It.IsAny<CancellationToken>()));
        foreach (var read in reads)
        {
            setup = setup.ReturnsAsync(read);
        }
    }

    private void Transition(bool changed) =>
        _appointments.Setup(r => r.TryTransitionAsync(_id, It.IsAny<AppointmentStatus>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(changed);

    private Task<Result<AppointmentResponse>> Send(
        AppointmentOutcome outcome, string role = Constants.StaffRole, Guid? callerBranch = null, bool noClaim = false) =>
        new MarkAppointmentHandler(_appointments.Object, new FixedTimeProvider(Now))
            .Handle(new MarkAppointmentCommand(_id, outcome, Guid.CreateVersion7(), role, noClaim ? null : callerBranch ?? _branch),
                CancellationToken.None);

    private static AppointmentStatus Target(AppointmentOutcome o) =>
        o == AppointmentOutcome.Completed ? AppointmentStatus.Completed : AppointmentStatus.NoShow;

    private void VerifyNoTransition() =>
        _appointments.Verify(r => r.TryTransitionAsync(It.IsAny<Guid>(), It.IsAny<AppointmentStatus>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

    [Theory]
    [InlineData(AppointmentOutcome.Completed)]
    [InlineData(AppointmentOutcome.NoShow)]
    public async Task A_Started_Booked_Appointment_Is_Marked_And_The_Re_Read_Is_Returned(AppointmentOutcome outcome)
    {
        Reads(Make(AppointmentStatus.Booked), Make(Target(outcome)));
        Transition(true);

        var result = await Send(outcome);

        Assert.True(result.IsSuccess);
        Assert.Equal(Target(outcome), result.Value!.Status);
        _appointments.Verify(r => r.TryTransitionAsync(_id, Target(outcome), Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Admin_Can_Mark_Any_Branch()
    {
        Reads(Make(AppointmentStatus.Booked), Make(AppointmentStatus.Completed));
        Transition(true);

        var result = await Send(AppointmentOutcome.Completed, Constants.AdminRole, noClaim: true);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Marking_Exactly_At_The_Start_Is_Allowed()
    {
        Reads(Make(AppointmentStatus.Booked, Now.UtcDateTime), Make(AppointmentStatus.Completed));
        Transition(true);

        Assert.True((await Send(AppointmentOutcome.Completed)).IsSuccess);
    }

    [Theory]
    [InlineData(AppointmentOutcome.Completed)]
    [InlineData(AppointmentOutcome.NoShow)]
    public async Task Marking_Before_The_Start_Is_409(AppointmentOutcome outcome)
    {
        Reads(Make(AppointmentStatus.Booked, Now.UtcDateTime.AddMinutes(1)));

        var result = await Send(outcome);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("The appointment has not started yet.", result.Error.Details);
        VerifyNoTransition();
    }

    [Theory]
    [InlineData(AppointmentOutcome.Completed)]
    [InlineData(AppointmentOutcome.NoShow)]
    public async Task A_Repeat_Of_The_Same_Outcome_Is_200_Without_An_Update(AppointmentOutcome outcome)
    {
        Reads(Make(Target(outcome)));

        var result = await Send(outcome);

        Assert.True(result.IsSuccess);
        Assert.Equal(Target(outcome), result.Value!.Status);
        VerifyNoTransition();
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled, AppointmentOutcome.Completed, "Cancelled", "Completed")]
    [InlineData(AppointmentStatus.Cancelled, AppointmentOutcome.NoShow, "Cancelled", "NoShow")]
    [InlineData(AppointmentStatus.Completed, AppointmentOutcome.NoShow, "Completed", "NoShow")]
    [InlineData(AppointmentStatus.NoShow, AppointmentOutcome.Completed, "NoShow", "Completed")]
    public async Task Any_Other_Status_Is_409_Without_An_Update(
        AppointmentStatus status, AppointmentOutcome outcome, string statusText, string targetText)
    {
        Reads(Make(status));

        var result = await Send(outcome);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal($"The appointment is {statusText} and cannot be marked {targetText}.", result.Error.Details);
        VerifyNoTransition();
    }

    [Fact]
    public async Task A_Stale_Update_Re_Reads_And_Gives_200_When_The_Winner_Set_The_Same_Outcome()
    {
        Reads(Make(AppointmentStatus.Booked), Make(AppointmentStatus.Completed));
        Transition(false);

        var result = await Send(AppointmentOutcome.Completed);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Completed, result.Value!.Status);
    }

    [Theory]
    [InlineData(AppointmentStatus.NoShow, "NoShow", "Completed")]
    [InlineData(AppointmentStatus.Cancelled, "Cancelled", "Completed")]
    public async Task A_Stale_Update_Re_Reads_And_Gives_409_When_The_Winner_Set_Another_Status(
        AppointmentStatus winner, string statusText, string targetText)
    {
        Reads(Make(AppointmentStatus.Booked), Make(winner));
        Transition(false);

        var result = await Send(AppointmentOutcome.Completed);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal($"The appointment is {statusText} and cannot be marked {targetText}.", result.Error.Details);
    }

    [Fact]
    public async Task Staff_Of_Another_Branch_Get_404_And_Nothing_Changes()
    {
        Reads(Make(AppointmentStatus.Booked));

        var result = await Send(AppointmentOutcome.Completed, callerBranch: Guid.CreateVersion7());

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Appointment '{_id}' was not found.", result.Error.Details);
        VerifyNoTransition();
    }

    [Fact]
    public async Task Staff_Without_A_Branch_Claim_Get_404()
    {
        Reads(Make(AppointmentStatus.Booked));

        var result = await Send(AppointmentOutcome.NoShow, noClaim: true);

        Assert.Equal(404, result.Error!.Status);
        VerifyNoTransition();
    }

    [Fact]
    public async Task An_Unknown_Appointment_Is_404()
    {
        Reads((Appointment?)null);

        var result = await Send(AppointmentOutcome.Completed);

        Assert.Equal(404, result.Error!.Status);
        VerifyNoTransition();
    }
}
