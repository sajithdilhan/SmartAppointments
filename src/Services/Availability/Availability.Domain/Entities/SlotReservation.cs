namespace Availability.Domain.Entities;

/// <summary>
/// One place held in a slot for one appointment. The row, keyed by slot and appointment, is what
/// makes reserve and release idempotent: a repeat reserve finds it and does nothing, and a release
/// without it does nothing. <c>AppointmentId</c> points into Booking's database, so it has no
/// foreign key. The repository writes the row with SQL (<c>ON CONFLICT DO NOTHING</c>); this type
/// exists for the model and the migration.
/// </summary>
public sealed class SlotReservation
{
    private SlotReservation() { }

    public Guid SlotId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static SlotReservation Create(Guid slotId, Guid appointmentId, DateTime createdAtUtc)
    {
        if (slotId == Guid.Empty)
        {
            throw new ArgumentException("A reservation needs a slot.", nameof(slotId));
        }

        if (appointmentId == Guid.Empty)
        {
            throw new ArgumentException("A reservation needs an appointment.", nameof(appointmentId));
        }

        return new SlotReservation
        {
            SlotId = slotId,
            AppointmentId = appointmentId,
            CreatedAtUtc = createdAtUtc
        };
    }
}
