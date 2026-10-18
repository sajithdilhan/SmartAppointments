using System.Globalization;
using Booking.Domain.Entities;

namespace Booking.Application.Models;

/// <summary>
/// Parses the raw query values of the branch day list. Status, page and page size are the same
/// parsers "my appointments" uses; only the date is new. A null input is an omitted parameter.
/// </summary>
internal static class BranchAppointmentsParameters
{
    public static bool TryParseDate(string? raw, out DateOnly? date)
    {
        date = null;
        if (raw is null)
        {
            return true;
        }

        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            return true;
        }

        return false;
    }

    public static bool TryParseStatus(string? raw, out AppointmentStatus? status) =>
        MyAppointmentsParameters.TryParseStatus(raw, out status);

    public static bool TryParsePage(string? raw, out int page) =>
        MyAppointmentsParameters.TryParsePage(raw, out page);

    public static bool TryParsePageSize(string? raw, out int pageSize) =>
        MyAppointmentsParameters.TryParsePageSize(raw, out pageSize);
}
