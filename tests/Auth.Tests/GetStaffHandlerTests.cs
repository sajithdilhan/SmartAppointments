using Auth.Application.Abstractions;
using Auth.Application.Handlers;
using Auth.Application.Queries;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Moq;

namespace Auth.Tests;

public class GetStaffHandlerTests
{
    private static readonly Guid Branch = Guid.CreateVersion7();

    private readonly Mock<IUserRepository> _users = new();

    private Task<SmartAppointments.BuildingBlocks.Models.Result<Auth.Application.Models.StaffResponse>> Send(Guid id) =>
        new GetStaffHandler(_users.Object).Handle(new GetStaffQuery(id), CancellationToken.None);

    private static User NewStaff() =>
        User.RegisterStaff("Sam", "Staff", Email.Create("sam@example.com"), "+15551234567", "hash", Branch);

    [Fact]
    public async Task A_Staff_Member_Is_Returned_With_The_Shape()
    {
        var staff = NewStaff();
        _users.Setup(r => r.GetByIdAsync(staff.Id, It.IsAny<CancellationToken>())).ReturnsAsync(staff);

        var result = await Send(staff.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(staff.Id, result.Value!.Id);
        Assert.Equal("sam@example.com", result.Value.Email);
        Assert.Equal("Staff", result.Value.Role);
        Assert.Equal(Branch, result.Value.BranchId);
        Assert.True(result.Value.IsActive);
        Assert.Equal(staff.RegistrationDateUtc, result.Value.RegisteredAtUtc);
    }

    [Fact]
    public async Task An_Inactive_Staff_Member_Is_Returned()
    {
        var staff = NewStaff();
        staff.Deactivate();
        _users.Setup(r => r.GetByIdAsync(staff.Id, It.IsAny<CancellationToken>())).ReturnsAsync(staff);

        var result = await Send(staff.Id);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
    }

    [Fact]
    public async Task An_Unknown_Id_Is_404()
    {
        var id = Guid.CreateVersion7();
        _users.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await Send(id);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Staff member '{id}' was not found.", result.Error.Details);
    }

    [Fact]
    public async Task A_Customer_Is_404()
    {
        var customer = User.RegisterCustomer("Cy", "Customer", Email.Create("cy@example.com"), "+15551234569", "hash");
        _users.Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);

        var result = await Send(customer.Id);

        Assert.Equal(404, result.Error!.Status);
    }

    [Fact]
    public async Task An_Admin_Is_404()
    {
        var admin = User.RegisterAdmin("Ada", "Admin", Email.Create("ada@example.com"), "+15551234567", "hash");
        _users.Setup(r => r.GetByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        var result = await Send(admin.Id);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Staff member '{admin.Id}' was not found.", result.Error.Details);
    }

    [Fact]
    public async Task A_Deleted_Staff_Member_Is_404()
    {
        var staff = NewStaff();
        staff.MarkDeleted(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        _users.Setup(r => r.GetByIdAsync(staff.Id, It.IsAny<CancellationToken>())).ReturnsAsync(staff);

        var result = await Send(staff.Id);

        Assert.Equal(404, result.Error!.Status);
    }
}
