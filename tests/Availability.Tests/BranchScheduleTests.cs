using Availability.Domain.Entities;

namespace Availability.Tests;

public class BranchScheduleTests
{
    private static Branch NewBranch() => Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");

    private static WorkingHours Hours(DayOfWeek day, int opensHour, int closesHour) =>
        new(day, new TimeOnly(opensHour, 0), new TimeOnly(closesHour, 0));

    [Fact]
    public void A_New_Branch_Has_No_Time_Zone_And_No_Working_Hours()
    {
        var branch = NewBranch();

        Assert.Null(branch.TimeZoneId);
        Assert.Empty(branch.WorkingHours);
    }

    [Fact]
    public void Setting_A_Schedule_Stores_The_Zone_The_Hours_And_An_Update_Time()
    {
        var branch = NewBranch();
        var before = DateTime.UtcNow;

        branch.SetSchedule("Asia/Colombo", [Hours(DayOfWeek.Monday, 9, 17), Hours(DayOfWeek.Tuesday, 10, 14)]);

        Assert.Equal("Asia/Colombo", branch.TimeZoneId);
        Assert.Equal(2, branch.WorkingHours.Count);
        Assert.InRange(branch.UpdatedAtUtc!.Value, before, DateTime.UtcNow);
        var monday = Assert.Single(branch.WorkingHours, w => w.DayOfWeek == DayOfWeek.Monday);
        Assert.Equal(new TimeOnly(9, 0), monday.OpensAt);
        Assert.Equal(new TimeOnly(17, 0), monday.ClosesAt);
    }

    [Fact]
    public void Setting_A_Schedule_Again_Changes_Retained_Days_In_Place()
    {
        var branch = NewBranch();
        branch.SetSchedule("Asia/Colombo", [Hours(DayOfWeek.Monday, 9, 17)]);
        var original = branch.WorkingHours[0];

        branch.SetSchedule("Asia/Colombo", [Hours(DayOfWeek.Monday, 8, 12)]);

        var monday = Assert.Single(branch.WorkingHours);
        Assert.Same(original, monday);
        Assert.Equal(new TimeOnly(8, 0), monday.OpensAt);
        Assert.Equal(new TimeOnly(12, 0), monday.ClosesAt);
    }

    [Fact]
    public void Setting_A_Schedule_Again_Removes_Unlisted_Days_And_Adds_New_Ones()
    {
        var branch = NewBranch();
        branch.SetSchedule("Asia/Colombo", [Hours(DayOfWeek.Monday, 9, 17), Hours(DayOfWeek.Tuesday, 9, 17)]);

        branch.SetSchedule("Europe/London", [Hours(DayOfWeek.Tuesday, 9, 17), Hours(DayOfWeek.Friday, 9, 13)]);

        Assert.Equal("Europe/London", branch.TimeZoneId);
        Assert.Equal(
            [DayOfWeek.Tuesday, DayOfWeek.Friday],
            branch.WorkingHours.Select(w => w.DayOfWeek).OrderBy(WorkingHours.MondayFirstOrder).ToArray());
    }

    [Fact]
    public void Setting_An_Empty_Schedule_Clears_Every_Day()
    {
        var branch = NewBranch();
        branch.SetSchedule("Asia/Colombo", [Hours(DayOfWeek.Monday, 9, 17)]);

        branch.SetSchedule("Asia/Colombo", []);

        Assert.Empty(branch.WorkingHours);
    }

    [Fact]
    public void A_Duplicate_Day_Is_Rejected_And_Changes_Nothing()
    {
        var branch = NewBranch();
        branch.SetSchedule("Asia/Colombo", [Hours(DayOfWeek.Monday, 9, 17)]);

        Assert.Throws<ArgumentException>(() => branch.SetSchedule(
            "Europe/London", [Hours(DayOfWeek.Friday, 9, 17), Hours(DayOfWeek.Friday, 10, 12)]));

        Assert.Equal("Asia/Colombo", branch.TimeZoneId);
        Assert.Equal(DayOfWeek.Monday, Assert.Single(branch.WorkingHours).DayOfWeek);
    }

    [Theory]
    [InlineData(17, 9)]
    [InlineData(9, 9)]
    public void Working_Hours_That_Do_Not_Open_Before_They_Close_Are_Rejected(int opensHour, int closesHour)
    {
        Assert.Throws<ArgumentException>(() => Hours(DayOfWeek.Monday, opensHour, closesHour));
    }

    [Fact]
    public void Monday_First_Order_Puts_Sunday_Last()
    {
        var ordered = Enum.GetValues<DayOfWeek>().OrderBy(WorkingHours.MondayFirstOrder).ToArray();

        Assert.Equal(DayOfWeek.Monday, ordered[0]);
        Assert.Equal(DayOfWeek.Saturday, ordered[5]);
        Assert.Equal(DayOfWeek.Sunday, ordered[6]);
    }
}
