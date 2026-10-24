using Auth.Application.Abstractions;
using Auth.Application.Handlers;
using Auth.Application.Queries;
using Auth.Application.Validations;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Moq;

namespace Auth.Tests;

public class ListStaffHandlerTests
{
    private static readonly Guid Branch = Guid.CreateVersion7();

    private readonly Mock<IUserRepository> _users = new();

    public ListStaffHandlerTests()
    {
        _users.Setup(r => r.ListStaffAsync(
                It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffListPage([], 0));
    }

    private Task<SmartAppointments.BuildingBlocks.Models.Result<Auth.Application.Models.StaffPage>> Send(
        string? branchId = null, string? isActive = null, string? page = null, string? pageSize = null) =>
        new ListStaffHandler(_users.Object, new ListStaffQueryValidator())
            .Handle(new ListStaffQuery(branchId, isActive, page, pageSize), CancellationToken.None);

    [Fact]
    public async Task Defaults_Reach_The_Repository_As_Page_1_Size_20_And_No_Filters()
    {
        var result = await Send();

        Assert.True(result.IsSuccess);
        _users.Verify(r => r.ListStaffAsync(null, null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, result.Value!.Page);
        Assert.Equal(20, result.Value.PageSize);
    }

    [Fact]
    public async Task An_Empty_Page_Gives_No_Items_And_A_Zero_Total()
    {
        var result = await Send();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task Parsed_Filters_And_Paging_Reach_The_Repository()
    {
        await Send(Branch.ToString(), "FALSE", "3", "50");

        _users.Verify(r => r.ListStaffAsync(Branch, false, 3, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Items_Are_Mapped_With_The_Page_And_The_Total_Count()
    {
        var first = User.RegisterStaff("Sam", "Staff", Email.Create("sam@example.com"), "+15551234567", "hash", Branch);
        var second = User.RegisterStaff("Kim", "Staff", Email.Create("kim@example.com"), "+15551234568", "hash", Branch);
        second.Deactivate();
        _users.Setup(r => r.ListStaffAsync(null, null, 2, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffListPage([first, second], 7));

        var result = await Send(page: "2", pageSize: "2");

        var page = result.Value!;
        Assert.Equal(7, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(first.Id, page.Items[0].Id);
        Assert.True(page.Items[0].IsActive);
        Assert.Equal(second.Email.Value, page.Items[1].Email);
        Assert.False(page.Items[1].IsActive);
        Assert.Equal(Branch, page.Items[1].BranchId);
    }

    [Fact]
    public async Task Invalid_Values_Give_One_400_Listing_Every_Error_Without_Calling_The_Repository()
    {
        var result = await Send("xyz", "x", "0");

        Assert.Equal(400, result.Error!.Status);
        Assert.StartsWith("Invalid request data. Errors: ", result.Error.Details);
        Assert.Contains("branchId", result.Error.Details);
        Assert.Contains("isActive", result.Error.Details);
        Assert.Contains("page must", result.Error.Details);
        _users.Verify(r => r.ListStaffAsync(
            It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
