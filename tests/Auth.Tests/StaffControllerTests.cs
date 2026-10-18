using System.Reflection;
using Auth.Api.Controllers;
using Auth.Application.Commands;
using Auth.Application.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Tests;

public class StaffControllerTests
{
    private static readonly Guid BranchId = Guid.CreateVersion7();
    private static readonly StaffResponse Staff = new(Guid.CreateVersion7(), "Sam", "Staff", "sam@example.com", "+15551234567", "Staff", BranchId);

    private readonly Mock<ISender> _sender = new();

    [Fact]
    public async Task Create_Returns_201_With_The_Staff_Shape_And_Dispatches_The_Command()
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateStaffCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaffResponse>.Success(Staff));
        var request = new CreateStaffRequest("Sam", "Staff", "sam@example.com", "+15551234567", "P@ssw0rd!23", BranchId);

        var result = await new StaffController(_sender.Object).Create(request, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
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
