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

    private static readonly DateTime Moment = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static User NewStaff() => User.RegisterStaff("Sam", "Staff", Address, "+15551234567", "hash", Guid.NewGuid());

    [Fact]
    public void A_New_User_Is_Not_Deleted()
    {
        var user = NewStaff();

        Assert.Null(user.DeletedAtUtc);
        Assert.False(user.IsDeleted);
    }

    [Fact]
    public void MarkDeleted_Sets_The_Time_And_Deactivates()
    {
        var user = NewStaff();

        user.MarkDeleted(Moment);

        Assert.Equal(Moment, user.DeletedAtUtc);
        Assert.True(user.IsDeleted);
        Assert.False(user.IsActive);
    }

    [Fact]
    public void MarkDeleted_Rejects_An_Already_Deleted_User_And_Keeps_The_First_Time()
    {
        var user = NewStaff();
        user.MarkDeleted(Moment);

        Assert.Throws<InvalidOperationException>(() => user.MarkDeleted(Moment.AddDays(1)));
        Assert.Equal(Moment, user.DeletedAtUtc);
    }

    [Fact]
    public void MarkDeleted_Rejects_Customers_And_Admins()
    {
        var customer = User.RegisterCustomer("Cam", "Customer", Address, "+15551234567", "hash");
        var admin = User.RegisterAdmin("Ada", "Admin", Address, "+15551234567", "hash");

        Assert.Throws<InvalidOperationException>(() => customer.MarkDeleted(Moment));
        Assert.Throws<InvalidOperationException>(() => admin.MarkDeleted(Moment));
        Assert.Null(customer.DeletedAtUtc);
        Assert.Null(admin.DeletedAtUtc);
    }

    [Fact]
    public void CanSignIn_Is_True_Only_For_An_Active_User_That_Is_Not_Deleted()
    {
        var active = NewStaff();
        var inactive = NewStaff();
        inactive.Deactivate();
        var deleted = NewStaff();
        deleted.MarkDeleted(Moment);
        var racy = NewStaff();
        racy.MarkDeleted(Moment);
        DeletedUserHelper.ForceActive(racy);

        Assert.True(active.CanSignIn);
        Assert.False(inactive.CanSignIn);
        Assert.False(deleted.CanSignIn);
        Assert.True(racy.IsActive);
        Assert.False(racy.CanSignIn);
    }

    [Fact]
    public void Activate_Reactivates_An_Inactive_User_But_Throws_When_Deleted()
    {
        var user = NewStaff();
        user.Deactivate();
        user.Activate();
        Assert.True(user.IsActive);

        user.MarkDeleted(Moment);

        Assert.Throws<InvalidOperationException>(user.Activate);
        Assert.False(user.IsActive);
    }

    [Fact]
    public void AssignBranch_Rejects_Admins()
    {
        var user = User.RegisterAdmin("Ada", "Admin", Address, "+15551234567", "hash");

        Assert.Throws<InvalidOperationException>(() => user.AssignBranch(Guid.NewGuid()));
    }
}
