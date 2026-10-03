namespace Booking.Domain.Entities;

public sealed class Appointment
{
    private Appointment()
    {
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }

    // SlotId, BranchId and ServiceTypeId are copies taken at booking time; they point into
    // Availability's database, so there are no foreign keys.
    public Guid SlotId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ServiceTypeId { get; private set; }
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    // The id is a parameter because the handler generated it when it claimed the idempotency key
    // and has already used it as the reservation key in Availability.
    public static Appointment Book(
        Guid id,
        Guid customerId,
        Guid slotId,
        Guid branchId,
        Guid serviceTypeId,
        DateTime startUtc,
        DateTime endUtc,
        DateTime nowUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Appointment id is required.", nameof(id));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (slotId == Guid.Empty) throw new ArgumentException("Slot id is required.", nameof(slotId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch id is required.", nameof(branchId));
        if (serviceTypeId == Guid.Empty) throw new ArgumentException("Service type id is required.", nameof(serviceTypeId));
        if (endUtc <= startUtc) throw new ArgumentException("End must be after start.", nameof(endUtc));

        return new Appointment
        {
            Id = id,
            CustomerId = customerId,
            SlotId = slotId,
            BranchId = branchId,
            ServiceTypeId = serviceTypeId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = AppointmentStatus.Booked,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    // Handlers check the state first, so reaching this with anything but Booked is a bug.
    public void Cancel(DateTime nowUtc)
    {
        if (Status != AppointmentStatus.Booked)
        {
            throw new InvalidOperationException($"Only a booked appointment can be cancelled; this one is {Status}.");
        }

        Status = AppointmentStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }
}
