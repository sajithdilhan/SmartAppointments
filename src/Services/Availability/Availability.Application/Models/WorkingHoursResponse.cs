namespace Availability.Application.Models;

public sealed record WorkingHoursResponse(DayOfWeek DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt);
