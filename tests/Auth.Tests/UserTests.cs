using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using SmartAppointments.BuildingBlocks.Enums;

namespace Auth.Tests;

public class UserTests
{
    private static readonly Email Address = Email.Create("staff@example.com");

    [Fact]
    public void RegisterStaff_Sets_Role_Branch_And_Active()
    {
        var branchId = Guid.NewGuid();

        var user = User.RegisterStaff(" Sam ", " Staff ", Address, "+15551234567", "hash", branchId);

        Assert.Equal(UserRole.Staff, user.Role);
        Assert.Equal(branchId, user.BranchId);
        Assert.True(user.IsActive);
        Assert.Equal("Sam", user.FirstName);
        Assert.Equal("Staff", user.LastName);
    }

    [Fact]
    public void RegisterStaff_Rejects_An_Empty_Branch()
    {
        Assert.Throws<ArgumentException>(() =>
            User.RegisterStaff("Sam", "Staff", Address, "+15551234567", "hash", Guid.Empty));
    }

    [Fact]
    public void Customers_And_Admins_Have_No_Branch()
    {
        Assert.Null(User.RegisterCustomer("Cam", "Customer", Address, "+15551234567", "hash").BranchId);
        Assert.Null(User.RegisterAdmin("Ada", "Admin", Address, "+15551234567", "hash").BranchId);
    }

    [Fact]
    public void AssignBranch_Changes_The_Branch_Of_A_Staff_User()
    {
        var user = User.RegisterStaff("Sam", "Staff", Address, "+15551234567", "hash", Guid.NewGuid());
        var newBranch = Guid.NewGuid();

        user.AssignBranch(newBranch);

        Assert.Equal(newBranch, user.BranchId);
    }

    [Fact]
    public void AssignBranch_Rejects_An_Empty_Branch_And_Keeps_The_Old_One()
    {
        var branchId = Guid.NewGuid();
        var user = User.RegisterStaff("Sam", "Staff", Address, "+15551234567", "hash", branchId);

        Assert.Throws<ArgumentException>(() => user.AssignBranch(Guid.Empty));
        Assert.Equal(branchId, user.BranchId);
    }

    [Fact]
    public void AssignBranch_Rejects_Customers()
    {
        var user = User.RegisterCustomer("Cam", "Customer", Address, "+15551234567", "hash");

        Assert.Throws<InvalidOperationException>(() => user.AssignBranch(Guid.NewGuid()));
        Assert.Null(user.BranchId);
    }

    [Fact]
    public void AssignBranch_Rejects_Admins()
    {
        var user = User.RegisterAdmin("Ada", "Admin", Address, "+15551234567", "hash");

        Assert.Throws<InvalidOperationException>(() => user.AssignBranch(Guid.NewGuid()));
    }
}
