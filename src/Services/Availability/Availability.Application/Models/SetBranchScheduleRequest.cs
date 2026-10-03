namespace Availability.Application.Models;

public sealed record SetBranchScheduleRequest(string TimeZoneId, List<WorkingHoursRequest> WorkingHours);
