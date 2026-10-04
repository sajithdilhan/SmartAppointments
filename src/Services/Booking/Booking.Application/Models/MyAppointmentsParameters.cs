using System.Globalization;
using Booking.Domain.Entities;

namespace Booking.Application.Models;

/// <summary>
/// Parses the raw query values of "my appointments". The validator and the handler both use it, so
/// what is accepted and what is applied cannot drift. A null input is an omitted parameter.
/// </summary>
internal static class MyAppointmentsParameters
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    // Matched by name on purpose, not with Enum.TryParse: that would also accept "0" or "1,0", and
    // Completed and NoShow must stay invalid until their feature adds them here.
    public static bool TryParseStatus(string? raw, out AppointmentStatus? status)
    {
        status = null;
        if (raw is null)
        {
            return true;
        }

        if (string.Equals(raw, nameof(AppointmentStatus.Booked), StringComparison.OrdinalIgnoreCase))
        {
            status = AppointmentStatus.Booked;
            return true;
        }

        if (string.Equals(raw, nameof(AppointmentStatus.Cancelled), StringComparison.OrdinalIgnoreCase))
        {
            status = AppointmentStatus.Cancelled;
            return true;
        }

        return false;
    }

    public static bool TryParseWhen(string? raw, out AppointmentTimeFilter? when)
    {
        when = null;
        if (raw is null)
        {
            return true;
        }

        if (string.Equals(raw, "upcoming", StringComparison.OrdinalIgnoreCase))
        {
            when = AppointmentTimeFilter.Upcoming;
            return true;
        }

        if (string.Equals(raw, "past", StringComparison.OrdinalIgnoreCase))
        {
            when = AppointmentTimeFilter.Past;
            return true;
        }

        return false;
    }

    public static bool TryParsePage(string? raw, out int page)
    {
        if (raw is null)
        {
            page = DefaultPage;
            return true;
        }

        return TryParseWholeNumber(raw, 1, int.MaxValue, out page);
    }

    public static bool TryParsePageSize(string? raw, out int pageSize)
    {
        if (raw is null)
        {
            pageSize = DefaultPageSize;
            return true;
        }

        return TryParseWholeNumber(raw, 1, MaxPageSize, out pageSize);
    }

    // Digits only, so "+1", "1.0", "1e1" and "-1" are all rejected.
    private static bool TryParseWholeNumber(string raw, int min, int max, out int value)
    {
        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value)
               && value >= min && value <= max;
    }
}
