using Availability.Domain.Entities;

namespace Availability.Tests;

public class SlotReservationTests
{
    [Fact]
    public void Create_Keeps_The_Slot_The_Appointment_And_The_Time()
    {
        var slotId = Guid.CreateVersion7();
        var appointmentId = Guid.CreateVersion7();
        var createdAt = new DateTime(2030, 1, 6, 12, 0, 0, DateTimeKind.Utc);

        var reservation = SlotReservation.Create(slotId, appointmentId, createdAt);

        Assert.Equal(slotId, reservation.SlotId);
        Assert.Equal(appointmentId, reservation.AppointmentId);
        Assert.Equal(createdAt, reservation.CreatedAtUtc);
    }

    [Fact]
    public void An_Empty_Slot_Id_Is_Rejected()
    {
        Assert.Throws<ArgumentException>(
            () => SlotReservation.Create(Guid.Empty, Guid.CreateVersion7(), DateTime.UtcNow));
    }

    [Fact]
    public void An_Empty_Appointment_Id_Is_Rejected()
    {
        Assert.Throws<ArgumentException>(
            () => SlotReservation.Create(Guid.CreateVersion7(), Guid.Empty, DateTime.UtcNow));
    }
}
