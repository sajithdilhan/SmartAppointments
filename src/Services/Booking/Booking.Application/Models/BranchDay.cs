namespace Booking.Application.Models;

/// <summary>
/// Turns a branch-local calendar day into a UTC half-open range. Pure: the zone comes from the id
/// passed in, never from the machine's local zone. A null or unknown id means UTC.
/// </summary>
public static class BranchDay
{
    /// <summary>The branch-local date at <paramref name="nowUtc"/>.</summary>
    public static DateOnly Today(string? zoneId, DateTime nowUtc)
    {
        var zone = Resolve(zoneId);
        var utc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    }

    /// <summary>
    /// <c>[FromUtc, ToUtc)</c> for the local day: local midnight to the next local midnight, so a DST
    /// day is 23 or 25 hours long. A midnight inside a DST gap moves forward to the first valid time.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) ToUtcRange(DateOnly date, string? zoneId)
    {
        var zone = Resolve(zoneId);
        var from = MidnightToUtc(date, zone);
        var to = date == DateOnly.MaxValue
            ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
            : MidnightToUtc(date.AddDays(1), zone);
        return (from, to);
    }

    private static DateTime MidnightToUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // Gaps are a multiple of 30 minutes and never longer than a day, so this ends quickly.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, zone), DateTimeKind.Utc);
    }

    /// <summary>
    /// Looks the zone up. A null or blank id is not an error (UTC, <c>true</c>); an id the runtime does
    /// not know gives UTC and <c>false</c>, so the caller can report it.
    /// </summary>
    public static bool TryResolve(string? zoneId, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return true;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static TimeZoneInfo Resolve(string? zoneId)
    {
        TryResolve(zoneId, out var zone);
        return zone;
    }
}
