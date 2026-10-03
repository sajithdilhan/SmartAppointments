namespace Availability.Application.Models;

/// <param name="OpensAt">24-hour "HH:mm".</param>
/// <param name="ClosesAt">24-hour "HH:mm".</param>
public sealed record WorkingHoursRequest(DayOfWeek DayOfWeek, string OpensAt, string ClosesAt);
