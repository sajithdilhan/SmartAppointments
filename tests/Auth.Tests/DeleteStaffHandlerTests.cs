using Auth.Application.Abstractions;
using Auth.Application.Commands;
using Auth.Application.Handlers;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Tests;

public class DeleteStaffHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new();
    private readonly User _staff = User.RegisterStaff("Sam", "Staff", Email.Create("sam@example.com"), "+15551234567", "hash", Guid.CreateVersion7());

    public DeleteStaffHandlerTests()
    {
        _users.Setup(r => r.GetForUpdateByIdAsync(_staff.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_staff);
        _users.Setup(r => r.SaveAndRevokeSessionsAsync(It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_Active_Or_Inactive_User_Is_Marked_Deleted_And_Saved_With_The_Clock_Instant(bool active)
    {
        if (!active)
        {
            _staff.Deactivate();
        }

        var result = await CreateSubject().Handle(new DeleteStaffCommand(_staff.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.UtcDateTime, _staff.DeletedAtUtc);
        Assert.False(_staff.IsActive);
        _users.Verify(r => r.SaveAndRevokeSessionsAsync(
            It.Is<User>(u => u.Id == _staff.Id && u.IsDeleted), Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unknown_Id_Is_404_With_No_Save()
    {
        var id = Guid.CreateVersion7();
        _users.Setup(r => r.GetForUpdateByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        AssertNotFoundAndNothingSaved(await CreateSubject().Handle(new DeleteStaffCommand(id), CancellationToken.None), id);
    }

    [Fact]
    public async Task A_Customer_Is_404_With_No_Save()
    {
        var customer = User.RegisterCustomer("Cam", "Customer", Email.Create("cam@example.com"), "+15551234567", "hash");
        _users.Setup(r => r.GetForUpdateByIdAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);

        AssertNotFoundAndNothingSaved(await CreateSubject().Handle(new DeleteStaffCommand(customer.Id), CancellationToken.None), customer.Id);
        Assert.Null(customer.DeletedAtUtc);
    }

    [Fact]
    public async Task An_Admin_Is_404_With_No_Save()
    {
        var admin = User.RegisterAdmin("Ada", "Admin", Email.Create("ada@example.com"), "+15551234567", "hash");
        _users.Setup(r => r.GetForUpdateByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        AssertNotFoundAndNothingSaved(await CreateSubject().Handle(new DeleteStaffCommand(admin.Id), CancellationToken.None), admin.Id);
        Assert.Null(admin.DeletedAtUtc);
    }

    [Fact]
    public async Task An_Already_Deleted_User_Is_404_And_Keeps_Its_First_Deletion_Time()
    {
        var first = Now.UtcDateTime.AddDays(-1);
        _staff.MarkDeleted(first);

        AssertNotFoundAndNothingSaved(await CreateSubject().Handle(new DeleteStaffCommand(_staff.Id), CancellationToken.None), _staff.Id);
        Assert.Equal(first, _staff.DeletedAtUtc);
    }

    private void AssertNotFoundAndNothingSaved(Result<MediatR.Unit> result, Guid id)
    {
        Assert.Equal(404, result.Error!.Status);
        Assert.Equal($"Staff member '{id}' was not found.", result.Error.Details);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.SaveAndRevokeSessionsAsync(It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private DeleteStaffHandler CreateSubject() => new(
        _users.Object,
        new FixedTimeProvider(Now),
        NullLogger<DeleteStaffHandler>.Instance);
}
