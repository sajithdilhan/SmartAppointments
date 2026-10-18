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

    [Fact]
    public void Complete_Marks_Completed_And_Sets_Only_Its_Timestamp()
    {
        var appointment = NewAppointment();
        var at = Start.AddMinutes(5);

        appointment.Complete(at);

        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal(at, appointment.CompletedAtUtc);
        Assert.Equal(at, appointment.UpdatedAtUtc);
        Assert.Null(appointment.NoShowAtUtc);
        Assert.Null(appointment.CancelledAtUtc);
    }

    [Fact]
    public void MarkNoShow_Marks_NoShow_And_Sets_Only_Its_Timestamp()
    {
        var appointment = NewAppointment();
        var at = Start;

        appointment.MarkNoShow(at);

        Assert.Equal(AppointmentStatus.NoShow, appointment.Status);
        Assert.Equal(at, appointment.NoShowAtUtc);
        Assert.Equal(at, appointment.UpdatedAtUtc);
        Assert.Null(appointment.CompletedAtUtc);
    }

    [Fact]
    public void Complete_And_MarkNoShow_Throw_Before_The_Start()
    {
        var appointment = NewAppointment();

        Assert.Throws<InvalidOperationException>(() => appointment.Complete(Start.AddTicks(-1)));
        Assert.Throws<InvalidOperationException>(() => appointment.MarkNoShow(Start.AddTicks(-1)));
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
    }

    [Fact]
    public void Complete_And_MarkNoShow_Throw_When_Not_Booked()
    {
        var cancelled = NewAppointment();
        cancelled.Cancel(Now);
        var completed = NewAppointment();
        completed.Complete(Start);

        Assert.Throws<InvalidOperationException>(() => cancelled.Complete(Start));
        Assert.Throws<InvalidOperationException>(() => cancelled.MarkNoShow(Start));
        Assert.Throws<InvalidOperationException>(() => completed.MarkNoShow(Start));
        Assert.Throws<InvalidOperationException>(() => completed.Complete(Start));
    }

    [Fact]
    public void Cancel_Throws_For_Completed_And_NoShow()
    {
        var completed = NewAppointment();
        completed.Complete(Start);
        var noShow = NewAppointment();
        noShow.MarkNoShow(Start);

        Assert.Throws<InvalidOperationException>(() => completed.Cancel(Start));
        Assert.Throws<InvalidOperationException>(() => noShow.Cancel(Start));
    }
}
