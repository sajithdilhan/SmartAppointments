using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Handlers;
using Auth.Application.Validations;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Tests;

public class AssignStaffBranchHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OldBranch = Guid.CreateVersion7();
    private static readonly Guid NewBranch = Guid.CreateVersion7();

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _tokens = new();
    private readonly Mock<IAvailabilityClient> _availability = new();
    private readonly List<string> _calls = [];
    private readonly User _staff = User.RegisterStaff("Sam", "Staff", Email.Create("sam@example.com"), "+15551234567", "hash", OldBranch);

    public AssignStaffBranchHandlerTests()
    {
        _users.Setup(r => r.GetForUpdateByIdAsync(_staff.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_staff);
        _users.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => _calls.Add("save")).Returns(Task.CompletedTask);
        _tokens.Setup(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("revoke")).ReturnsAsync(2);
        _availability.Setup(a => a.GetBranchAsync(NewBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Success(new BranchInfo(NewBranch, true, "UTC")));
    }

    [Fact]
    public async Task Moving_Staff_Sets_The_Branch_Saves_Then_Revokes_Once_With_The_User_Id_And_Now()
    {
        var result = await CreateSubject().Handle(new AssignStaffBranchCommand(_staff.Id, NewBranch), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewBranch, _staff.BranchId);
        Assert.Equal(NewBranch, result.Value!.BranchId);
        Assert.Equal(_staff.Id, result.Value.Id);
        Assert.Equal("Staff", result.Value.Role);
        Assert.Equal(["save", "revoke"], _calls);
        _tokens.Verify(r => r.RevokeAllForUserAsync(_staff.Id, Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Empty_Branch_Is_400_And_Nothing_Is_Read()
    {
        var result = await CreateSubject().Handle(new AssignStaffBranchCommand(_staff.Id, Guid.Empty), CancellationToken.None);

        Assert.Equal(400, result.Error!.Status);
        Assert.Contains("Branch is required.", result.Error.Details);
        _users.Verify(r => r.GetForUpdateByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _tokens.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unknown_User_Is_404()
    {
        var id = Guid.CreateVersion7();
        _users.Setup(r => r.GetForUpdateByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await CreateSubject().Handle(new AssignStaffBranchCommand(id, NewBranch), CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Staff member '{id}' was not found.", result.Error.Details);
        _availability.Verify(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Customer_Or_Admin_Is_404()
    {
        var customer = User.RegisterCustomer("Cam", "Customer", Email.Create("cam@example.com"), "+15551234567", "hash");
        var admin = User.RegisterAdmin("Ada", "Admin", Email.Create("ada@example.com"), "+15551234567", "hash");
        _users.Setup(r => r.GetForUpdateByIdAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _users.Setup(r => r.GetForUpdateByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        var subject = CreateSubject();

        Assert.Equal(404, (await subject.Handle(new AssignStaffBranchCommand(customer.Id, NewBranch), CancellationToken.None)).Error!.Status);
        Assert.Equal(404, (await subject.Handle(new AssignStaffBranchCommand(admin.Id, NewBranch), CancellationToken.None)).Error!.Status);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Same_Branch_Is_200_With_No_Availability_Call_Save_Or_Revoke()
    {
        var result = await CreateSubject().Handle(new AssignStaffBranchCommand(_staff.Id, OldBranch), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OldBranch, result.Value!.BranchId);
        _availability.Verify(a => a.GetBranchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _tokens.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unknown_Branch_Is_422_And_The_User_Is_Unchanged()
    {
        _availability.Setup(a => a.GetBranchAsync(NewBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Success(null));

        var result = await CreateSubject().Handle(new AssignStaffBranchCommand(_staff.Id, NewBranch), CancellationToken.None);

        Assert.Equal(422, result.Error!.Status);
        Assert.Equal(OldBranch, _staff.BranchId);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _tokens.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Inactive_Branch_Is_422_And_The_User_Is_Unchanged()
    {
        _availability.Setup(a => a.GetBranchAsync(NewBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Success(new BranchInfo(NewBranch, false, null)));

        var result = await CreateSubject().Handle(new AssignStaffBranchCommand(_staff.Id, NewBranch), CancellationToken.None);

        Assert.Equal(422, result.Error!.Status);
        Assert.Equal(OldBranch, _staff.BranchId);
        _tokens.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unreachable_Availability_Is_503_And_The_User_Is_Unchanged()
    {
        _availability.Setup(a => a.GetBranchAsync(NewBranch, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchInfo?>.Failure(new Error(503, "The availability service is unavailable. Try again later.")));

        var result = await CreateSubject().Handle(new AssignStaffBranchCommand(_staff.Id, NewBranch), CancellationToken.None);

        Assert.Equal(503, result.Error!.Status);
        Assert.Equal(OldBranch, _staff.BranchId);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _tokens.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Failed_Save_Revokes_Nothing()
    {
        _users.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSubject().Handle(new AssignStaffBranchCommand(_staff.Id, NewBranch), CancellationToken.None));

        _tokens.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private AssignStaffBranchHandler CreateSubject() => new(
        _users.Object,
        _tokens.Object,
        _availability.Object,
        new AssignStaffBranchCommandValidator(),
        new FixedTimeProvider(Now),
        NullLogger<AssignStaffBranchHandler>.Instance);
}
