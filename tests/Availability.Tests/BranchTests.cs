using Availability.Domain.Entities;

namespace Availability.Tests;

public class BranchTests
{
    [Fact]
    public void A_New_Branch_Is_Active_With_A_Version_7_Id_And_A_Creation_Time()
    {
        var before = DateTime.UtcNow;

        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");

        Assert.Equal(7, branch.Id.Version);
        Assert.True(branch.IsActive);
        Assert.InRange(branch.CreatedAtUtc, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, branch.CreatedAtUtc.Kind);
        Assert.Null(branch.UpdatedAtUtc);
    }

    [Fact]
    public void Every_Text_Field_Is_Trimmed_And_The_Code_Upper_Cased()
    {
        var branch = Branch.Create(" pg-1 ", " Pettah ", " Walk-ins welcome ", " 12 Main Street ", " +94112345678 ");

        Assert.Equal("PG-1", branch.Code);
        Assert.Equal("Pettah", branch.Name);
        Assert.Equal("Walk-ins welcome", branch.Description);
        Assert.Equal("12 Main Street", branch.Address);
        Assert.Equal("+94112345678", branch.PhoneNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Description_Is_Stored_As_Null(string? description)
    {
        var branch = Branch.Create("PG", "Pettah", description, "12 Main Street", "+94112345678");

        Assert.Null(branch.Description);
    }

    [Fact]
    public void Updating_Details_Trims_Them_And_Sets_The_Update_Time_But_Leaves_Code_And_State_Alone()
    {
        var branch = Branch.Create("PG", "Pettah", "Old", "12 Main Street", "+94112345678");
        branch.Deactivate();
        var before = DateTime.UtcNow;

        branch.UpdateDetails(" Pettah North ", "   ", " 14 Main Street ", " +94119999999 ");

        Assert.Equal("Pettah North", branch.Name);
        Assert.Null(branch.Description);
        Assert.Equal("14 Main Street", branch.Address);
        Assert.Equal("+94119999999", branch.PhoneNumber);
        Assert.Equal("PG", branch.Code);
        Assert.False(branch.IsActive);
        Assert.InRange(branch.UpdatedAtUtc!.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public void Deactivating_Then_Activating_Flips_The_State_And_Sets_The_Update_Time()
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");

        Assert.True(branch.Deactivate());
        Assert.False(branch.IsActive);
        Assert.NotNull(branch.UpdatedAtUtc);

        Assert.True(branch.Activate());
        Assert.True(branch.IsActive);
    }

    [Fact]
    public void Requesting_The_Current_State_Changes_Nothing()
    {
        var branch = Branch.Create("PG", "Pettah", null, "12 Main Street", "+94112345678");

        Assert.False(branch.Activate());
        Assert.True(branch.IsActive);
        Assert.Null(branch.UpdatedAtUtc);

        branch.Deactivate();
        var deactivatedAt = branch.UpdatedAtUtc;

        Assert.False(branch.Deactivate());
        Assert.Equal(deactivatedAt, branch.UpdatedAtUtc);
    }

    [Fact]
    public void Normalising_A_Code_Matches_What_Create_Stores()
    {
        // The handler's duplicate check looks up NormaliseCode(input); if that ever differed from
        // what Create stores, the pre-check would miss real duplicates.
        const string input = "  pg-1 ";

        Assert.Equal(Branch.Create(input, "Pettah", null, "12 Main Street", "+94112345678").Code, Branch.NormaliseCode(input));
    }
}
