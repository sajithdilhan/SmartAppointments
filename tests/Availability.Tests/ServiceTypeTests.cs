using Availability.Domain.Entities;

namespace Availability.Tests;

public class ServiceTypeTests
{
    [Fact]
    public void A_New_Service_Type_Is_Active_With_A_Version_7_Id_And_A_Creation_Time()
    {
        var before = DateTime.UtcNow;

        var serviceType = ServiceType.Create("INSPECT", "Inspection", null, 30);

        Assert.Equal(7, serviceType.Id.Version);
        Assert.True(serviceType.IsActive);
        Assert.Equal(30, serviceType.DurationMinutes);
        Assert.InRange(serviceType.CreatedAtUtc, before, DateTime.UtcNow);
        Assert.Null(serviceType.UpdatedAtUtc);
    }

    [Fact]
    public void Text_Is_Trimmed_The_Code_Upper_Cased_And_A_Blank_Description_Stored_As_Null()
    {
        var serviceType = ServiceType.Create(" vehicle_inspection ", " Vehicle inspection ", "   ", 30);

        Assert.Equal("VEHICLE_INSPECTION", serviceType.Code);
        Assert.Equal("Vehicle inspection", serviceType.Name);
        Assert.Null(serviceType.Description);
    }

    [Fact]
    public void Normalising_A_Code_Matches_What_Create_Stores()
    {
        const string input = "  passport-renewal ";

        Assert.Equal(ServiceType.Create(input, "Passport", null, 15).Code, ServiceType.NormaliseCode(input));
    }

    [Fact]
    public void Updating_Details_Changes_Name_Description_And_Duration_But_Not_Code_Or_State()
    {
        var serviceType = ServiceType.Create("INSPECT", "Inspection", null, 30);
        serviceType.Deactivate();

        serviceType.UpdateDetails(" Full inspection ", " Bring the logbook ", 45);

        Assert.Equal("Full inspection", serviceType.Name);
        Assert.Equal("Bring the logbook", serviceType.Description);
        Assert.Equal(45, serviceType.DurationMinutes);
        Assert.Equal("INSPECT", serviceType.Code);
        Assert.False(serviceType.IsActive);
        Assert.NotNull(serviceType.UpdatedAtUtc);
    }

    [Fact]
    public void Activation_Reports_Whether_Anything_Changed()
    {
        var serviceType = ServiceType.Create("INSPECT", "Inspection", null, 30);

        Assert.False(serviceType.Activate());
        Assert.Null(serviceType.UpdatedAtUtc);
        Assert.True(serviceType.Deactivate());
        Assert.False(serviceType.Deactivate());
        Assert.True(serviceType.Activate());
        Assert.True(serviceType.IsActive);
    }
}
