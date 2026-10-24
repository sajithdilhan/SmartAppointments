using System.Reflection;
using Auth.Api.Controllers;
using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Tests;

public class StaffControllerTests
{
    private static readonly Guid BranchId = Guid.CreateVersion7();
    private static readonly DateTime RegisteredAt = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly StaffResponse Staff = new(Guid.CreateVersion7(), "Sam", "Staff", "sam@example.com", "+15551234567", "Staff", BranchId, true, RegisteredAt);

    private readonly Mock<ISender> _sender = new();

    [Fact]
    public async Task Create_Returns_201_With_The_Staff_Shape_And_Dispatches_The_Command()
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateStaffCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaffResponse>.Success(Staff));
        var request = new CreateStaffRequest("Sam", "Staff", "sam@example.com", "+15551234567", "P@ssw0rd!23", BranchId);

        var result = await new StaffController(_sender.Object).Create(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(nameof(StaffController.Get), created.ActionName);
        Assert.Equal(Staff.Id, created.RouteValues!["id"]);
        Assert.Same(Staff, created.Value);
        _sender.Verify(s => s.Send(
            It.Is<CreateStaffCommand>(c => c.Email == "sam@example.com" && c.BranchId == BranchId && c.Password == "P@ssw0rd!23" && c.FirstName == "Sam"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(422)]
    [InlineData(503)]
    public async Task Create_Maps_Failures_To_Their_Status(int status)
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateStaffCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaffResponse>.Failure(new Error(status, "problem")));

        var result = await new StaffController(_sender.Object).Create(
            new CreateStaffRequest("a", "b", "c", "d", "e", BranchId), CancellationToken.None);

        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
    }

    private StaffController ControllerWithQuery(string queryString) =>
        new(_sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Request = { QueryString = new QueryString(queryString) } }
            }
        };

    [Fact]
    public async Task List_Returns_200_With_The_Page_And_Sends_The_Raw_Query_Strings()
    {
        var page = new StaffPage([Staff], 2, 5, 11);
        _sender.Setup(s => s.Send(It.IsAny<ListStaffQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaffPage>.Success(page));

        var result = await ControllerWithQuery("?isActive=TRUE&page=abc").List(
            BranchId.ToString(), "TRUE", "abc", null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(page, ok.Value);
        _sender.Verify(s => s.Send(
            new ListStaffQuery(BranchId.ToString(), "TRUE", "abc", null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("?branchId=&isActive=&page=&pageSize=")]
    [InlineData("")]
    public async Task List_Keeps_An_Empty_Query_Value_Empty_And_An_Omitted_One_Null(string queryString)
    {
        _sender.Setup(s => s.Send(It.IsAny<ListStaffQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaffPage>.Success(new StaffPage([], 1, 20, 0)));
        // MVC binds an empty value to null, the same as an omitted one.
        var expected = queryString == "" ? null : "";

        await ControllerWithQuery(queryString).List(null, null, null, null, CancellationToken.None);

        _sender.Verify(s => s.Send(
            new ListStaffQuery(expected, expected, expected, expected), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_Maps_A_400_To_The_Problem_Result()
    {
        _sender.Setup(s => s.Send(It.IsAny<ListStaffQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaffPage>.Failure(new Error(400, "bad")));

        var result = await ControllerWithQuery("").List(null, null, null, null, CancellationToken.None);

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
    }

    [Fact]
    public async Task Get_Returns_200_And_Dispatches_The_Id()
    {
        _sender.Setup(s => s.Send(It.IsAny<GetStaffQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaffResponse>.Success(Staff));

        var result = await new StaffController(_sender.Object).Get(Staff.Id, CancellationToken.None);

        Assert.Same(Staff, Assert.IsType<OkObjectResult>(result).Value);
        _sender.Verify(s => s.Send(It.Is<GetStaffQuery>(q => q.Id == Staff.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_Maps_404_To_The_Problem_Result()
    {
        _sender.Setup(s => s.Send(It.IsAny<GetStaffQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaffResponse>.Failure(new Error(404, "problem")));

        var result = await new StaffController(_sender.Object).Get(Staff.Id, CancellationToken.None);

        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
    }

    [Fact]
    public void List_And_Get_Are_Routed_As_Specified_And_Covered_By_The_Class_Level_Admin_Policy()
    {
        var list = typeof(StaffController).GetMethod(nameof(StaffController.List))!;
        var get = typeof(StaffController).GetMethod(nameof(StaffController.Get))!;

        Assert.Null(list.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{id:guid}", get.GetCustomAttribute<HttpGetAttribute>()!.Template);
        foreach (var action in new[] { list, get })
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());
        }
    }

    [Fact]
    public async Task AssignBranch_Returns_200_And_Dispatches_The_Id_And_Branch()
    {
        _sender.Setup(s => s.Send(It.IsAny<AssignStaffBranchCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaffResponse>.Success(Staff));

        var result = await new StaffController(_sender.Object).AssignBranch(
            Staff.Id, new AssignStaffBranchRequest(BranchId), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(Staff, ok.Value);
        _sender.Verify(s => s.Send(
            It.Is<AssignStaffBranchCommand>(c => c.Id == Staff.Id && c.BranchId == BranchId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(422)]
    [InlineData(503)]
    public async Task AssignBranch_Maps_Failures_To_Their_Status(int status)
    {
        _sender.Setup(s => s.Send(It.IsAny<AssignStaffBranchCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaffResponse>.Failure(new Error(status, "problem")));

        var result = await new StaffController(_sender.Object).AssignBranch(
            Staff.Id, new AssignStaffBranchRequest(BranchId), CancellationToken.None);

        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
    }

    [Fact]
    public void The_Staff_Shape_Maps_IsActive_And_RegisteredAtUtc_And_Exposes_No_Deletion_Time()
    {
        Assert.True(Staff.IsActive);
        Assert.Equal(RegisteredAt, Staff.RegisteredAtUtc);
        Assert.Equal(
            ["Id", "FirstName", "LastName", "Email", "PhoneNumber", "Role", "BranchId", "IsActive", "RegisteredAtUtc"],
            typeof(StaffResponse).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract"));
    }

    [Fact]
    public async Task Deactivate_Dispatches_A_False_SetActive_And_Returns_204()
    {
        _sender.Setup(s => s.Send(It.IsAny<SetStaffActiveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        var result = await new StaffController(_sender.Object).Deactivate(Staff.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _sender.Verify(s => s.Send(
            It.Is<SetStaffActiveCommand>(c => c.Id == Staff.Id && !c.IsActive), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Activate_Dispatches_A_True_SetActive_And_Returns_204()
    {
        _sender.Setup(s => s.Send(It.IsAny<SetStaffActiveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        var result = await new StaffController(_sender.Object).Activate(Staff.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _sender.Verify(s => s.Send(
            It.Is<SetStaffActiveCommand>(c => c.Id == Staff.Id && c.IsActive), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(422)]
    [InlineData(503)]
    public async Task Activate_Maps_Failures_To_Their_Status(int status)
    {
        _sender.Setup(s => s.Send(It.IsAny<SetStaffActiveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Failure(new Error(status, "problem")));

        var result = await new StaffController(_sender.Object).Activate(Staff.Id, CancellationToken.None);

        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
    }

    [Fact]
    public async Task Deactivate_Maps_404_To_The_Problem_Result()
    {
        _sender.Setup(s => s.Send(It.IsAny<SetStaffActiveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Failure(new Error(404, "problem")));

        var result = await new StaffController(_sender.Object).Deactivate(Staff.Id, CancellationToken.None);

        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
    }

    [Fact]
    public async Task Delete_Dispatches_The_Id_And_Returns_204()
    {
        _sender.Setup(s => s.Send(It.IsAny<DeleteStaffCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Success(Unit.Value));

        var result = await new StaffController(_sender.Object).Delete(Staff.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _sender.Verify(s => s.Send(It.Is<DeleteStaffCommand>(c => c.Id == Staff.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_Maps_404_To_The_Problem_Result()
    {
        _sender.Setup(s => s.Send(It.IsAny<DeleteStaffCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Unit>.Failure(new Error(404, "problem")));

        var result = await new StaffController(_sender.Object).Delete(Staff.Id, CancellationToken.None);

        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
    }

    [Fact]
    public void The_Lifecycle_Actions_Are_Routed_As_Specified_And_Covered_By_The_Class_Level_Admin_Policy()
    {
        var deactivate = typeof(StaffController).GetMethod(nameof(StaffController.Deactivate))!;
        var activate = typeof(StaffController).GetMethod(nameof(StaffController.Activate))!;
        var delete = typeof(StaffController).GetMethod(nameof(StaffController.Delete))!;

        Assert.Equal("{id:guid}/deactivate", deactivate.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{id:guid}/activate", activate.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{id:guid}", delete.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
        foreach (var action in new[] { deactivate, activate, delete })
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());
        }

        Assert.Equal(Constants.AdminPolicy, typeof(StaffController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [Fact]
    public void The_Controller_Requires_The_Admin_Policy_And_Is_Routed_Under_Auth_Staff()
    {
        var authorize = typeof(StaffController).GetCustomAttribute<AuthorizeAttribute>();
        var route = typeof(StaffController).GetCustomAttribute<RouteAttribute>();

        Assert.Equal(Constants.AdminPolicy, authorize!.Policy);
        Assert.Equal("api/auth/staff", route!.Template);
        Assert.Null(typeof(StaffController).GetMethod(nameof(StaffController.Create))!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(typeof(StaffController).GetMethod(nameof(StaffController.AssignBranch))!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void The_Actions_Are_Routed_As_Specified()
    {
        Assert.IsType<HttpPostAttribute>(typeof(StaffController).GetMethod(nameof(StaffController.Create))!.GetCustomAttribute<HttpPostAttribute>());
        var put = typeof(StaffController).GetMethod(nameof(StaffController.AssignBranch))!.GetCustomAttribute<HttpPutAttribute>();
        Assert.Equal("{id:guid}/branch", put!.Template);
    }
}
