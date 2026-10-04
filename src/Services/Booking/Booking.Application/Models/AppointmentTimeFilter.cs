namespace Booking.Application.Models;

/// <summary>Which side of "now" the list is limited to. No filter at all is a null, not a member.</summary>
public enum AppointmentTimeFilter
{
    Upcoming,
    Past
}
