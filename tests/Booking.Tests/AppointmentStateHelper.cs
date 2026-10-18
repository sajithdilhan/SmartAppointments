using System.Reflection;
using Booking.Domain.Entities;

namespace Booking.Tests;

// The marked states are reached in production only through a conditional UPDATE, so the entity has no
// mutator for them; tests set the state directly.
internal static class AppointmentStateHelper
{
    public static Appointment WithStatus(this Appointment appointment, AppointmentStatus status, DateTime atUtc)
    {
        Set(appointment, nameof(Appointment.Status), status);
        Set(appointment, nameof(Appointment.UpdatedAtUtc), atUtc);
        switch (status)
        {
            case AppointmentStatus.Completed: Set(appointment, nameof(Appointment.CompletedAtUtc), (DateTime?)atUtc); break;
            case AppointmentStatus.NoShow: Set(appointment, nameof(Appointment.NoShowAtUtc), (DateTime?)atUtc); break;
        }

        return appointment;
    }

    private static void Set(Appointment appointment, string property, object value) =>
        typeof(Appointment).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(appointment, value);
}
