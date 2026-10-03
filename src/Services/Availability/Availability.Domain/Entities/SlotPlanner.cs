namespace Availability.Domain.Entities;

/// <summary>
/// Works out which slots a branch's schedule produces for a service type over a date range.
/// Pure and free of I/O: all the calendar arithmetic (local dates, time zones, clock changes,
/// slots past closing, slots in the past) lives here so each rule is a unit test.
/// </summary>
public static class SlotPlanner
{
    public static IReadOnlyList<Slot> Plan(
        Branch branch,
        ServiceType serviceType,
        DateOnly fromDate,
        DateOnly toDate,
        int capacity,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(branch.TimeZoneId))
        {
            throw new InvalidOperationException($"Branch '{branch.Code}' has no time zone.");
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(branch.TimeZoneId);
        var duration = serviceType.DurationMinutes;
        var slots = new List<Slot>();

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var hours = branch.WorkingHours.FirstOrDefault(w => w.DayOfWeek == date.DayOfWeek);
            if (hours is null)
            {
                continue;
            }

            // Whole minutes since midnight, so a slot can never wrap past midnight.
            var closesAtMinute = hours.ClosesAt.Hour * 60 + hours.ClosesAt.Minute;
            for (var start = hours.OpensAt.Hour * 60 + hours.OpensAt.Minute;
                 start + duration <= closesAtMinute;
                 start += duration)
            {
                var localStart = date.ToDateTime(new TimeOnly(start / 60, start % 60), DateTimeKind.Unspecified);

                // A time skipped by a clock change does not exist on that date.
                if (zone.IsInvalidTime(localStart))
                {
                    continue;
                }

                // An ambiguous time resolves to standard time. The end is added in UTC so the
                // slot lasts the real elapsed duration even across a clock change.
                var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, zone);
                var endUtc = startUtc.AddMinutes(duration);

                if (startUtc <= nowUtc)
                {
                    continue;
                }

                slots.Add(Slot.Create(branch.Id, serviceType.Id, date, startUtc, endUtc, capacity));
            }
        }

        return slots;
    }
}
