using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Handlers;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Tests;

public class SetStaffActiveHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid BranchId = Guid.CreateVersion7();

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAvailabilityClient> _availability = new();
    private readonly User _staff = User.RegisterStaff("Sam", "Staff", Email.Create("sam@example.com"), "+15551234567", "hash", BranchId);

    public SetStaffActiveHandlerTests()
    {
        _users.Setup(r => r.GetForUpdateByIdAsync(_staff.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_staff);
        _users.Setup(r => r.SaveAndRevokeSessionsAsync(It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _availability.Setup(a => a.GetBranchAsync(BranchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Success(new BranchInfo(BranchId, true, "UTC")));
    }

    [Fact]
    public async Task Deactivate_Saves_And_Revokes_With_The_Clock_Instant_And_Never_Calls_Availability()
    {
        var result = await CreateSubject().Handle(new SetStaffActiveCommand(_staff.Id, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(_staff.IsActive);
        _users.Verify(r => r.SaveAndRevokeSessionsAsync(
            It.Is<User>(u => u.Id == _staff.Id && !u.IsActive), Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _availability.Verify(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deactivating_An_Already_Inactive_User_Still_Saves_And_Revokes()
    {
        _staff.Deactivate();

        var result = await CreateSubject().Handle(new SetStaffActiveCommand(_staff.Id, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _users.Verify(r => r.SaveAndRevokeSessionsAsync(
            It.Is<User>(u => u.Id == _staff.Id), Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
        _availability.Verify(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_Checks_The_Branch_Then_Activates_With_A_Plain_Save_And_No_Revocation()
    {
        _staff.Deactivate();

        var result = await CreateSubject().Handle(new SetStaffActiveCommand(_staff.Id, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(_staff.IsActive);
        _availability.Verify(a => a.GetBranchAsync(BranchId, It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(r => r.SaveAndRevokeSessionsAsync(It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activating_An_Active_User_Is_Success_With_No_Availability_Call_Or_Save()
    {
        var result = await CreateSubject().Handle(new SetStaffActiveCommand(_staff.Id, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(_staff.IsActive);
        _availability.Verify(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.SaveAndRevokeSessionsAsync(It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_With_An_Unknown_Branch_Is_422_And_The_User_Stays_Inactive_And_Unsaved()
    {
        _staff.Deactivate();
        _availability.Setup(a => a.GetBranchAsync(BranchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Success(null));

        var result = await CreateSubject().Handle(new SetStaffActiveCommand(_staff.Id, true), CancellationToken.None);

        Assert.Equal(422, result.Error!.Status);
        Assert.Equal($"Branch '{BranchId}' does not exist or is inactive.", result.Error.Details);
        Assert.False(_staff.IsActive);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_With_An_Inactive_Branch_Is_422_And_Unsaved()
    {
        _staff.Deactivate();
        _availability.Setup(a => a.GetBranchAsync(BranchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Success(new BranchInfo(BranchId, false, null)));

        var result = await CreateSubject().Handle(new SetStaffActiveCommand(_staff.Id, true), CancellationToken.None);

        Assert.Equal(422, result.Error!.Status);
        Assert.False(_staff.IsActive);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_With_Availability_Down_Is_503_And_The_User_Stays_Inactive_And_Unsaved()
    {
        _staff.Deactivate();
        _availability.Setup(a => a.GetBranchAsync(BranchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Failure(new Error(503, "The availability service is unavailable. Try again later.")));

        var result = await CreateSubject().Handle(new SetStaffActiveCommand(_staff.Id, true), CancellationToken.None);

        Assert.Equal(503, result.Error!.Status);
        Assert.False(_staff.IsActive);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_Unknown_Id_Is_404_With_No_Save(bool isActive)
    {
        var id = Guid.CreateVersion7();
        _users.Setup(r => r.GetForUpdateByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await CreateSubject().Handle(new SetStaffActiveCommand(id, isActive), CancellationToken.None);

        AssertNotFoundAndNothingSaved(result, id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Customer_Is_404_With_No_Save(bool isActive)
    {
        var customer = User.RegisterCustomer("Cam", "Customer", Email.Create("cam@example.com"), "+15551234567", "hash");
        _users.Setup(r => r.GetForUpdateByIdAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);

        var result = await CreateSubject().Handle(new SetStaffActiveCommand(customer.Id, isActive), CancellationToken.None);

        AssertNotFoundAndNothingSaved(result, customer.Id);
        Assert.True(customer.IsActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_Admin_Is_404_With_No_Save(bool isActive)
    {
        var admin = User.RegisterAdmin("Ada", "Admin", Email.Create("ada@example.com"), "+15551234567", "hash");
        _users.Setup(r => r.GetForUpdateByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        var result = await CreateSubject().Handle(new SetStaffActiveCommand(admin.Id, isActive), CancellationToken.None);

        AssertNotFoundAndNothingSaved(result, admin.Id);
        Assert.True(admin.IsActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Deleted_Staff_Member_Is_404_With_No_Save(bool isActive)
    {
        _staff.MarkDeleted(Now.UtcDateTime);

        var result = await CreateSubject().Handle(new SetStaffActiveCommand(_staff.Id, isActive), CancellationToken.None);

        AssertNotFoundAndNothingSaved(result, _staff.Id);
    }

    private void AssertNotFoundAndNothingSaved(Result<MediatR.Unit> result, Guid id)
    {
        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Staff member '{id}' was not found.", result.Error.Details);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.SaveAndRevokeSessionsAsync(It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _availability.Verify(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private SetStaffActiveHandler CreateSubject() => new(
        _users.Object,
        _availability.Object,
        new FixedTimeProvider(Now),
        NullLogger<SetStaffActiveHandler>.Instance);
}
