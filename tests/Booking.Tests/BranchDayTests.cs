using Booking.Application.Models;

namespace Booking.Tests;

public class BranchDayTests
{
    private static DateTime Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_Ordinary_Day_In_Auckland_Summer_Starts_At_Local_Midnight()
    {
        var (from, to) = BranchDay.ToUtcRange(new DateOnly(2030, 1, 15), "Pacific/Auckland");

        Assert.Equal(Utc(2030, 1, 14, 11), from);
        Assert.Equal(Utc(2030, 1, 15, 11), to);
        Assert.Equal(DateTimeKind.Utc, from.Kind);
    }

    [Fact]
    public void An_Ordinary_Day_In_Auckland_Winter_Is_Twelve_Hours_Ahead()
    {
        var (from, to) = BranchDay.ToUtcRange(new DateOnly(2030, 6, 15), "Pacific/Auckland");

        Assert.Equal(Utc(2030, 6, 14, 12), from);
        Assert.Equal(Utc(2030, 6, 15, 12), to);
    }

    [Fact]
    public void An_Ordinary_Day_In_New_York_Winter_Is_Five_Hours_Behind()
    {
        var (from, to) = BranchDay.ToUtcRange(new DateOnly(2030, 1, 15), "America/New_York");

        Assert.Equal(Utc(2030, 1, 15, 5), from);
        Assert.Equal(Utc(2030, 1, 16, 5), to);
    }

    [Fact]
    public void An_Ordinary_Day_In_New_York_Summer_Is_Four_Hours_Behind()
    {
        var (from, to) = BranchDay.ToUtcRange(new DateOnly(2030, 7, 15), "America/New_York");

        Assert.Equal(Utc(2030, 7, 15, 4), from);
        Assert.Equal(Utc(2030, 7, 16, 4), to);
    }

    [Fact]
    public void New_York_Spring_Forward_Day_Is_23_Hours()
    {
        var (from, to) = BranchDay.ToUtcRange(new DateOnly(2030, 3, 10), "America/New_York");

        Assert.Equal(Utc(2030, 3, 10, 5), from);
        Assert.Equal(Utc(2030, 3, 11, 4), to);
        Assert.Equal(TimeSpan.FromHours(23), to - from);
    }

    [Fact]
    public void New_York_Fall_Back_Day_Is_25_Hours()
    {
        var (from, to) = BranchDay.ToUtcRange(new DateOnly(2030, 11, 3), "America/New_York");

        Assert.Equal(Utc(2030, 11, 3, 4), from);
        Assert.Equal(Utc(2030, 11, 4, 5), to);
        Assert.Equal(TimeSpan.FromHours(25), to - from);
    }

    [Fact]
    public void Auckland_Spring_Forward_Day_Is_23_Hours()
    {
        var (from, to) = BranchDay.ToUtcRange(new DateOnly(2030, 9, 29), "Pacific/Auckland");

        Assert.Equal(TimeSpan.FromHours(23), to - from);
        Assert.Equal(Utc(2030, 9, 28, 12), from);
    }

    [Fact]
    public void Auckland_Fall_Back_Day_Is_25_Hours()
    {
        var (from, to) = BranchDay.ToUtcRange(new DateOnly(2030, 4, 7), "Pacific/Auckland");

        Assert.Equal(TimeSpan.FromHours(25), to - from);
        Assert.Equal(Utc(2030, 4, 6, 11), from);
    }

    [Fact]
    public void A_Midnight_In_A_Dst_Gap_Moves_Forward_To_The_First_Valid_Time()
    {
        // Havana skips 00:00-01:00 on 10 March 2030, so the day starts at 01:00 local (05:00Z).
        var (from, to) = BranchDay.ToUtcRange(new DateOnly(2030, 3, 10), "America/Havana");

        Assert.Equal(Utc(2030, 3, 10, 5), from);
        Assert.Equal(Utc(2030, 3, 11, 4), to);
    }

    [Fact]
    public void Consecutive_Days_Tile_Without_Gaps_Across_A_Dst_Change()
    {
        var (_, toFirst) = BranchDay.ToUtcRange(new DateOnly(2030, 3, 10), "America/New_York");
        var (fromSecond, _) = BranchDay.ToUtcRange(new DateOnly(2030, 3, 11), "America/New_York");

        Assert.Equal(toFirst, fromSecond);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mars/Olympus_Mons")]
    public void A_Missing_Or_Unknown_Zone_Means_Utc(string? zoneId)
    {
        var (from, to) = BranchDay.ToUtcRange(new DateOnly(2030, 3, 10), zoneId);

        Assert.Equal(Utc(2030, 3, 10), from);
        Assert.Equal(Utc(2030, 3, 11), to);
        Assert.Equal(new DateOnly(2030, 3, 10), BranchDay.Today(zoneId, Utc(2030, 3, 10, 23)));
    }

    [Fact]
    public void Today_Is_The_Branch_Local_Date()
    {
        var now = Utc(2030, 1, 6, 12);

        Assert.Equal(new DateOnly(2030, 1, 7), BranchDay.Today("Pacific/Auckland", now));
        Assert.Equal(new DateOnly(2030, 1, 6), BranchDay.Today("America/New_York", now));
    }

    [Fact]
    public void Today_Rolls_Over_At_Local_Midnight()
    {
        Assert.Equal(new DateOnly(2030, 1, 14), BranchDay.Today("Pacific/Auckland", new DateTime(2030, 1, 14, 10, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2030, 1, 15), BranchDay.Today("Pacific/Auckland", Utc(2030, 1, 14, 11)));
    }

    [Fact]
    public void The_Last_Representable_Day_Does_Not_Overflow()
    {
        var (_, to) = BranchDay.ToUtcRange(DateOnly.MaxValue, "UTC");

        Assert.True(to > Utc(9999, 12, 31));
    }
}
