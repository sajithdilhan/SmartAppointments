using Booking.Domain.Entities;

namespace Booking.Tests;

public class AppointmentTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = Now.AddDays(1);
    private static readonly DateTime End = Start.AddMinutes(30);

    private static Appointment NewAppointment() =>
        Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start, End, Now);

    [Fact]
    public void Book_Sets_Booked_Status_And_Timestamps()
    {
        var id = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var slot = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var service = Guid.NewGuid();

        var appointment = Appointment.Book(id, customer, slot, branch, service, Start, End, Now);

        Assert.Equal(id, appointment.Id);
        Assert.Equal(customer, appointment.CustomerId);
        Assert.Equal(slot, appointment.SlotId);
        Assert.Equal(branch, appointment.BranchId);
        Assert.Equal(service, appointment.ServiceTypeId);
        Assert.Equal(Start, appointment.StartUtc);
        Assert.Equal(End, appointment.EndUtc);
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
        Assert.Equal(Now, appointment.CreatedAtUtc);
        Assert.Equal(Now, appointment.UpdatedAtUtc);
        Assert.Null(appointment.CancelledAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Book_Rejects_Empty_Ids(int emptyIndex)
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        ids[emptyIndex] = Guid.Empty;

        Assert.Throws<ArgumentException>(() =>
            Appointment.Book(ids[0], ids[1], ids[2], ids[3], ids[4], Start, End, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Book_Rejects_End_Not_After_Start(int minutes)
    {
        Assert.Throws<ArgumentException>(() =>
            Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Start, Start.AddMinutes(minutes), Now));
    }

    [Fact]
    public void Cancel_Marks_Cancelled_And_Sets_Timestamps()
    {
        var appointment = NewAppointment();
        var later = Now.AddHours(1);

        appointment.Cancel(later);

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(later, appointment.CancelledAtUtc);
        Assert.Equal(later, appointment.UpdatedAtUtc);
        Assert.Equal(Now, appointment.CreatedAtUtc);
    }

    [Fact]
    public void Cancel_Throws_When_Not_Booked()
    {
        var appointment = NewAppointment();
        appointment.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => appointment.Cancel(Now.AddMinutes(1)));
    }

    public static TheoryData<AppointmentStatus, AppointmentStatus, int, MarkCheck> MarkCases()
    {
        // offset: minutes of "now" relative to the start.
        var data = new TheoryData<AppointmentStatus, AppointmentStatus, int, MarkCheck>();
        foreach (var target in new[] { AppointmentStatus.Completed, AppointmentStatus.NoShow })
        {
            foreach (var status in Enum.GetValues<AppointmentStatus>())
            {
                foreach (var offset in new[] { -1, 0, 1 })
                {
                    MarkCheck expected;
                    if (status == target) expected = MarkCheck.AlreadyMarked;
                    else if (status != AppointmentStatus.Booked) expected = MarkCheck.WrongStatus;
                    else expected = offset < 0 ? MarkCheck.NotStarted : MarkCheck.Allowed;
                    data.Add(status, target, offset, expected);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MarkCases))]
    public void CheckMark_Covers_Every_Status_Target_And_Time(
        AppointmentStatus status, AppointmentStatus target, int offsetMinutes, MarkCheck expected)
    {
        var appointment = status == AppointmentStatus.Cancelled
            ? NewAppointment().WithStatus(AppointmentStatus.Cancelled, Now)
            : NewAppointment().WithStatus(status, Start);

        Assert.Equal(expected, appointment.CheckMark(target, Start.AddMinutes(offsetMinutes)));
    }

    [Fact]
    public void CheckMark_Is_NotStarted_One_Tick_Before_And_Allowed_At_The_Start()
    {
        var appointment = NewAppointment();

        Assert.Equal(MarkCheck.NotStarted, appointment.CheckMark(AppointmentStatus.Completed, Start.AddTicks(-1)));
        Assert.Equal(MarkCheck.Allowed, appointment.CheckMark(AppointmentStatus.NoShow, Start));
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void CheckMark_Rejects_A_Target_That_Is_Not_A_Mark_Outcome(AppointmentStatus target)
    {
        Assert.Throws<ArgumentException>(() => NewAppointment().CheckMark(target, Start));
    }

    [Fact]
    public void Cancel_Throws_For_Completed_And_NoShow()
    {
        var completed = NewAppointment().WithStatus(AppointmentStatus.Completed, Start);
        var noShow = NewAppointment().WithStatus(AppointmentStatus.NoShow, Start);

        Assert.Throws<InvalidOperationException>(() => completed.Cancel(Start));
        Assert.Throws<InvalidOperationException>(() => noShow.Cancel(Start));
    }
}
