namespace Availability.Domain.Entities;

/// <summary>
/// The local opening hours of a branch on one day of the week. A day with no entry is closed.
/// Times are wall-clock times in the branch's time zone, and the hours never span midnight.
/// </summary>
public sealed class WorkingHours
{
    private WorkingHours() { }

    public WorkingHours(DayOfWeek dayOfWeek, TimeOnly opensAt, TimeOnly closesAt)
    {
        EnsureOpensBeforeCloses(opensAt, closesAt);

        DayOfWeek = dayOfWeek;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly OpensAt { get; private set; }

    public TimeOnly ClosesAt { get; private set; }

    /// <summary>
    /// Used by <see cref="Branch.SetSchedule"/> to change a retained day in place, because EF
    /// Core would reject removing and re-adding the same key.
    /// </summary>
    internal void ChangeTimes(TimeOnly opensAt, TimeOnly closesAt)
    {
        EnsureOpensBeforeCloses(opensAt, closesAt);

        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    /// <summary>
    /// Sunday is 0 in <see cref="System.DayOfWeek"/>, so a plain sort puts it first. Schedules
    /// are presented Monday to Sunday.
    /// </summary>
    public static int MondayFirstOrder(DayOfWeek dayOfWeek) => ((int)dayOfWeek + 6) % 7;

    private static void EnsureOpensBeforeCloses(TimeOnly opensAt, TimeOnly closesAt)
    {
        if (opensAt >= closesAt)
        {
            throw new ArgumentException("Opening time must be before closing time.");
        }
    }
}
