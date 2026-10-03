using Availability.Api.Controllers;
using Availability.Application.Commands;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;
using System.Reflection;
using System.Security.Claims;

namespace Availability.Tests;

public class ServicesControllerTests
{
    private static readonly ServiceTypeResponse Response =
        new(Guid.CreateVersion7(), "INSPECT", "Inspection", null, 30, true, DateTime.UtcNow, null);

    private readonly Mock<ISender> _sender = new();

    [Fact]
    public async Task Create_Returns_201_Pointing_At_GetServiceType()
    {
        _sender.Setup(s => s.Send(new CreateServiceTypeCommand("INSPECT", "Inspection", null, 30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ServiceTypeResponse>.Success(Response));

        var result = await Subject().Create(new CreateServiceTypeRequest("INSPECT", "Inspection", null, 30), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(ServicesController.GetServiceType), created.ActionName);
        Assert.Equal(Response.Id, created.RouteValues!["id"]);
        Assert.Same(Response, created.Value);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestObjectResult))]
    [InlineData(409, typeof(ConflictObjectResult))]
    public async Task Create_Failure_Maps_To_The_Handler_Status(int status, Type expected)
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateServiceTypeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ServiceTypeResponse>.Failure(new Error(status, "failure")));

        var result = await Subject().Create(new CreateServiceTypeRequest("X", "", null, 0), CancellationToken.None);

        Assert.IsType(expected, result);
    }

    [Fact]
    public async Task Update_Passes_The_Route_Id_And_Returns_Ok()
    {
        var id = Guid.CreateVersion7();
        _sender.Setup(s => s.Send(new UpdateServiceTypeCommand(id, "Inspection", "d", 45), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ServiceTypeResponse>.Success(Response));

        var result = await Subject().Update(id, new UpdateServiceTypeRequest("Inspection", "d", 45), CancellationToken.None);

        Assert.Same(Response, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Update_Of_An_Unknown_Id_Returns_404()
    {
        _sender.Setup(s => s.Send(It.IsAny<UpdateServiceTypeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ServiceTypeResponse>.Failure(new Error(404, "not found")));

        var result = await Subject().Update(Guid.CreateVersion7(), new UpdateServiceTypeRequest("a", null, 30), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Activate_And_Deactivate_Return_204_And_Send_The_Requested_State()
    {
        var id = Guid.CreateVersion7();
        _sender.Setup(s => s.Send(It.IsAny<SetServiceTypeActiveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(false));
        var controller = Subject();

        Assert.IsType<NoContentResult>(await controller.Activate(id, CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.Deactivate(id, CancellationToken.None));

        _sender.Verify(s => s.Send(new SetServiceTypeActiveCommand(id, true), It.IsAny<CancellationToken>()), Times.Once);
        _sender.Verify(s => s.Send(new SetServiceTypeActiveCommand(id, false), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deactivate_An_Unknown_Id_Returns_404()
    {
        _sender.Setup(s => s.Send(It.IsAny<SetServiceTypeActiveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(404, "not found")));

        Assert.IsType<NotFoundObjectResult>(await Subject().Deactivate(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Fact]
    public async Task GetServiceTypes_Passes_The_Flag_And_The_Callers_Role()
    {
        _sender.Setup(s => s.Send(It.IsAny<GetServiceTypesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ServiceTypeResponse>>.Success([Response]));

        var result = await Subject(Constants.StaffRole).GetServiceTypes(includeInactive: true, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _sender.Verify(s => s.Send(new GetServiceTypesQuery(true, Constants.StaffRole), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetServiceType_Passes_The_Callers_Role_And_Maps_404()
    {
        var id = Guid.CreateVersion7();
        _sender.Setup(s => s.Send(new GetServiceTypeQuery(id, Constants.CustomerRole), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ServiceTypeResponse>.Failure(new Error(404, "not found")));

        var result = await Subject(Constants.CustomerRole).GetServiceType(id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Theory]
    [InlineData(nameof(ServicesController.Create), Constants.AdminPolicy)]
    [InlineData(nameof(ServicesController.Update), Constants.AdminPolicy)]
    [InlineData(nameof(ServicesController.Activate), Constants.AdminPolicy)]
    [InlineData(nameof(ServicesController.Deactivate), Constants.AdminPolicy)]
    [InlineData(nameof(ServicesController.GetServiceTypes), Constants.AllowedOriginsPolicy)]
    [InlineData(nameof(ServicesController.GetServiceType), Constants.AllowedOriginsPolicy)]
    public void Each_Action_Requires_Its_Policy(string action, string policy)
    {
        var attribute = typeof(ServicesController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(policy, attribute.Policy);
    }

    private ServicesController Subject(string? role = null)
    {
        var controller = new ServicesController(_sender.Object);
        if (role is not null)
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Constants.RoleClaimType, role)], "Test"))
                }
            };
        }
        return controller;
    }
}
