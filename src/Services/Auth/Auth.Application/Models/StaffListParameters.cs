using System.Globalization;

namespace Auth.Application.Models;

/// <summary>
/// Parses the raw query values of the staff list. The validator and the handler both use it, so
/// what is accepted and what is applied cannot drift. A null input is an omitted parameter.
/// </summary>
internal static class StaffListParameters
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static bool TryParseBranchId(string? raw, out Guid? branchId)
    {
        branchId = null;
        if (raw is null)
        {
            return true;
        }

        if (Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty)
        {
            branchId = parsed;
            return true;
        }

        return false;
    }

    // Explicit comparisons on purpose, not bool.TryParse: that also trims whitespace.
    public static bool TryParseIsActive(string? raw, out bool? isActive)
    {
        isActive = null;
        if (raw is null)
        {
            return true;
        }

        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
        {
            isActive = true;
            return true;
        }

        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
        {
            isActive = false;
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
