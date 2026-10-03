using Availability.Domain.Entities;

namespace Availability.Tests;

public class SlotPlannerTests
{
    private static readonly DateTime LongAgo = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Branch BranchWith(string timeZoneId, params WorkingHours[] hours)
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");
        branch.SetSchedule(timeZoneId, hours);
        return branch;
    }

    private static WorkingHours Hours(DayOfWeek day, string opensAt, string closesAt) =>
        new(day, TimeOnly.Parse(opensAt), TimeOnly.Parse(closesAt));

    private static ServiceType Service(int durationMinutes) =>
        ServiceType.Create("HAIRCUT", "Haircut", null, durationMinutes);

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    // 2027-10-04 is a Monday.
    private static readonly DateOnly Monday = new(2027, 10, 4);

    [Fact]
    public void Slots_Run_Back_To_Back_From_Opening_Time()
    {
        var branch = BranchWith("Asia/Colombo", Hours(DayOfWeek.Monday, "09:00", "11:00"));

        var slots = SlotPlanner.Plan(branch, Service(30), Monday, Monday, 2, LongAgo);

        Assert.Equal(4, slots.Count);
        for (var i = 0; i < slots.Count; i++)
        {
            Assert.Equal(Utc(2027, 10, 4, 3, 30).AddMinutes(30 * i), slots[i].StartUtc);
            Assert.Equal(slots[i].StartUtc.AddMinutes(30), slots[i].EndUtc);
            Assert.Equal(2, slots[i].Capacity);
            Assert.Equal(0, slots[i].ReservedCount);
        }

        for (var i = 1; i < slots.Count; i++)
        {
            Assert.Equal(slots[i - 1].EndUtc, slots[i].StartUtc);
        }
    }

    [Fact]
    public void The_Last_Slot_Never_Runs_Past_Closing_Time()
    {
        var branch = BranchWith("Asia/Colombo", Hours(DayOfWeek.Monday, "09:00", "10:45"));

        var slots = SlotPlanner.Plan(branch, Service(30), Monday, Monday, 1, LongAgo);

        Assert.Equal(3, slots.Count);
        Assert.Equal(Utc(2027, 10, 4, 5, 0), slots[^1].EndUtc); // 10:30 local
    }

    [Fact]
    public void A_Service_Longer_Than_The_Opening_Hours_Gets_No_Slots()
    {
        var branch = BranchWith("Asia/Colombo", Hours(DayOfWeek.Monday, "09:00", "09:45"));

        Assert.Empty(SlotPlanner.Plan(branch, Service(60), Monday, Monday, 1, LongAgo));
    }

    [Fact]
    public void Closed_Days_Get_No_Slots()
    {
        var branch = BranchWith("Asia/Colombo", Hours(DayOfWeek.Monday, "09:00", "10:00"));

        var slots = SlotPlanner.Plan(branch, Service(30), Monday.AddDays(1), Monday.AddDays(6), 1, LongAgo);

        Assert.Empty(slots);
    }

    [Fact]
    public void Slots_That_Have_Already_Started_Are_Skipped()
    {
        var branch = BranchWith("Asia/Colombo", Hours(DayOfWeek.Monday, "09:00", "11:00"));

        // 03:30Z, 04:00Z, 04:30Z and 05:00Z are the four starts; now is exactly the second.
        var slots = SlotPlanner.Plan(branch, Service(30), Monday, Monday, 1, Utc(2027, 10, 4, 4, 0));

        Assert.Equal(2, slots.Count);
        Assert.Equal(Utc(2027, 10, 4, 4, 30), slots[0].StartUtc);
    }

    [Fact]
    public void Local_Times_Are_Converted_Using_The_Fixed_Offset_Of_The_Zone()
    {
        var branch = BranchWith("Asia/Colombo", Hours(DayOfWeek.Monday, "09:00", "09:30"));

        var slot = Assert.Single(SlotPlanner.Plan(branch, Service(30), Monday, Monday, 1, LongAgo));

        Assert.Equal(Utc(2027, 10, 4, 3, 30), slot.StartUtc);
        Assert.Equal(DateTimeKind.Utc, slot.StartUtc.Kind);
        Assert.Equal(Monday, slot.LocalDate);
    }

    [Fact]
    public void The_Local_Date_Is_The_Date_At_The_Branch_Even_When_Utc_Is_The_Previous_Day()
    {
        var branch = BranchWith("Asia/Colombo", Hours(DayOfWeek.Monday, "02:00", "03:00"));

        var slot = Assert.Single(SlotPlanner.Plan(branch, Service(60), Monday, Monday, 1, LongAgo));

        Assert.Equal(Utc(2027, 10, 3, 20, 30), slot.StartUtc);
        Assert.Equal(Monday, slot.LocalDate);
    }

    [Fact]
    public void Times_Skipped_By_A_Spring_Forward_Clock_Change_Are_Not_Planned()
    {
        // London clocks go from 01:00 to 02:00 on Sunday 2027-03-28.
        var branch = BranchWith("Europe/London", Hours(DayOfWeek.Sunday, "00:30", "03:00"));
        var date = new DateOnly(2027, 3, 28);

        var slots = SlotPlanner.Plan(branch, Service(30), date, date, 1, LongAgo);

        // 01:00 and 01:30 local do not exist; the rest keep their real UTC instants.
        Assert.Equal(
            [Utc(2027, 3, 28, 0, 30), Utc(2027, 3, 28, 1, 0), Utc(2027, 3, 28, 1, 30)],
            slots.Select(s => s.StartUtc).ToArray());
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromMinutes(30), s.EndUtc - s.StartUtc));
        Assert.All(slots, s => Assert.Equal(date, s.LocalDate));
    }

    [Fact]
    public void An_Ambiguous_Time_On_A_Fall_Back_Day_Uses_Standard_Time()
    {
        // London clocks go from 02:00 back to 01:00 on Sunday 2027-10-31, so 01:00-02:00 occurs twice.
        var branch = BranchWith("Europe/London", Hours(DayOfWeek.Sunday, "00:00", "03:00"));
        var date = new DateOnly(2027, 10, 31);

        var slots = SlotPlanner.Plan(branch, Service(30), date, date, 1, LongAgo);

        Assert.Equal(
            [
                Utc(2027, 10, 30, 23, 0),
                Utc(2027, 10, 30, 23, 30),
                Utc(2027, 10, 31, 1, 0),
                Utc(2027, 10, 31, 1, 30),
                Utc(2027, 10, 31, 2, 0),
                Utc(2027, 10, 31, 2, 30)
            ],
            slots.Select(s => s.StartUtc).ToArray());
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromMinutes(30), s.EndUtc - s.StartUtc));
        Assert.All(slots, s => Assert.Equal(date, s.LocalDate));
    }

    [Fact]
    public void A_Range_Spanning_Several_Weeks_Plans_Every_Open_Day()
    {
        var branch = BranchWith(
            "Asia/Colombo",
            Hours(DayOfWeek.Monday, "09:00", "10:00"),
            Hours(DayOfWeek.Saturday, "09:00", "10:00"));

        // Monday 2027-10-04 to Sunday 2027-10-24: three Mondays and three Saturdays.
        var slots = SlotPlanner.Plan(branch, Service(30), Monday, Monday.AddDays(20), 1, LongAgo);

        Assert.Equal(12, slots.Count);
        Assert.Equal(6, slots.Select(s => s.LocalDate).Distinct().Count());
        Assert.All(slots, s => Assert.Contains(s.LocalDate.DayOfWeek, new[] { DayOfWeek.Monday, DayOfWeek.Saturday }));
        Assert.Equal(slots.Count, slots.Select(s => s.StartUtc).Distinct().Count());
    }

    [Fact]
    public void Every_Slot_Gets_A_Version_7_Id_For_Its_Branch_And_Service_Type()
    {
        var branch = BranchWith("Asia/Colombo", Hours(DayOfWeek.Monday, "09:00", "10:00"));
        var service = Service(30);

        var slots = SlotPlanner.Plan(branch, service, Monday, Monday, 1, LongAgo);

        Assert.All(slots, s =>
        {
            Assert.Equal(7, s.Id.Version);
            Assert.Equal(branch.Id, s.BranchId);
            Assert.Equal(service.Id, s.ServiceTypeId);
        });
    }

    [Fact]
    public void A_Branch_Without_A_Time_Zone_Cannot_Be_Planned()
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");

        Assert.Throws<InvalidOperationException>(() => SlotPlanner.Plan(branch, Service(30), Monday, Monday, 1, LongAgo));
    }

    [Fact]
    public void Available_Capacity_Is_Capacity_Less_Reserved()
    {
        var slot = Slot.Create(Guid.NewGuid(), Guid.NewGuid(), Monday, LongAgo, LongAgo.AddMinutes(30), 5);

        Assert.Equal(5, slot.AvailableCapacity);
        Assert.Equal(0, slot.ReservedCount);
    }
}
