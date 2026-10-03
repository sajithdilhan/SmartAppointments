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

public class BranchesControllerTests
{
    private static readonly CreateBranchRequest Request =
        new("PG", "Pettah", null, "12 Main Street", "+94112345678");

    [Fact]
    public async Task Create_Successful_Returns_Created_With_Location()
    {
        var response = new BranchResponse(
            Guid.CreateVersion7(), "PG", "Pettah", null, "12 Main Street", "+94112345678", true, DateTime.UtcNow, null);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<CreateBranchCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchResponse>.Success(response));

        var result = await new BranchesController(sender.Object).Create(Request, CancellationToken.None);

        // The Location header is generated from GetBranch's route, so pointing at that action with
        // the new id is what makes it /api/Branches/{id}.
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(nameof(BranchesController.GetBranch), created.ActionName);
        Assert.Equal(response.Id, created.RouteValues!["id"]);
        Assert.Same(response, created.Value);
    }

    [Fact]
    public async Task Create_Passes_Every_Request_Field_To_The_Command()
    {
        var request = new CreateBranchRequest("PG", "Pettah", "Walk-ins welcome", "12 Main Street", "+94112345678");
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<CreateBranchCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchResponse>.Failure(new Error(400, "irrelevant")));

        await new BranchesController(sender.Object).Create(request, CancellationToken.None);

        sender.Verify(s => s.Send(
            new CreateBranchCommand("PG", "Pettah", "Walk-ins welcome", "12 Main Street", "+94112345678"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestObjectResult))]
    [InlineData(404, typeof(NotFoundObjectResult))]
    [InlineData(409, typeof(ConflictObjectResult))]
    public async Task Create_Failure_Maps_To_The_Handler_Status(int status, Type expectedResult)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<CreateBranchCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchResponse>.Failure(new Error(status, "failure")));

        var result = await new BranchesController(sender.Object).Create(Request, CancellationToken.None);

        Assert.IsType(expectedResult, result);
        Assert.Equal(status, ((ObjectResult)result).StatusCode);
    }

    [Theory]
    [InlineData(nameof(BranchesController.Create), Constants.AdminPolicy)]
    [InlineData(nameof(BranchesController.Update), Constants.AdminPolicy)]
    [InlineData(nameof(BranchesController.Activate), Constants.AdminPolicy)]
    [InlineData(nameof(BranchesController.Deactivate), Constants.AdminPolicy)]
    [InlineData(nameof(BranchesController.GetBranches), Constants.AllowedOriginsPolicy)]
    [InlineData(nameof(BranchesController.GetBranch), Constants.AllowedOriginsPolicy)]
    public void Each_Action_Requires_Its_Policy(string action, string policy)
    {
        // The unit tests construct the controller directly, so the authorization middleware never
        // runs here. Asserting on the attribute is what stops a refactor silently opening an admin
        // endpoint to every signed-in customer, or a read endpoint to anonymous callers.
        var attribute = typeof(BranchesController)
            .GetMethod(action)!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(policy, attribute.Policy);
    }

    [Fact]
    public async Task Update_Successful_Returns_Ok_With_The_Branch()
    {
        var id = Guid.CreateVersion7();
        var response = new BranchResponse(
            id, "PG", "Pettah North", null, "14 Main Street", "+94112345678", true, DateTime.UtcNow, DateTime.UtcNow);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<UpdateBranchCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchResponse>.Success(response));

        var result = await new BranchesController(sender.Object)
            .Update(id, new UpdateBranchRequest("Pettah North", null, "14 Main Street", "+94112345678"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
        sender.Verify(s => s.Send(
            new UpdateBranchCommand(id, "Pettah North", null, "14 Main Street", "+94112345678"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestObjectResult))]
    [InlineData(404, typeof(NotFoundObjectResult))]
    public async Task Update_Failure_Maps_To_The_Handler_Status(int status, Type expectedResult)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<UpdateBranchCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchResponse>.Failure(new Error(status, "failure")));

        var result = await new BranchesController(sender.Object)
            .Update(Guid.CreateVersion7(), new UpdateBranchRequest("Pettah", null, "12 Main Street", "+94112345678"), CancellationToken.None);

        Assert.IsType(expectedResult, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Activate_And_Deactivate_Return_204_Whether_Or_Not_Anything_Changed(bool changed)
    {
        var id = Guid.CreateVersion7();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<SetBranchActiveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(changed));
        var controller = new BranchesController(sender.Object);

        Assert.IsType<NoContentResult>(await controller.Activate(id, CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.Deactivate(id, CancellationToken.None));

        sender.Verify(s => s.Send(new SetBranchActiveCommand(id, true), It.IsAny<CancellationToken>()), Times.Once);
        sender.Verify(s => s.Send(new SetBranchActiveCommand(id, false), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Activate_An_Unknown_Branch_Returns_404()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<SetBranchActiveCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(404, "not found")));

        var result = await new BranchesController(sender.Object).Activate(Guid.CreateVersion7(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetBranches_Passes_IncludeInactive_And_The_Callers_Role_To_The_Query()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<BranchResponse>>.Success([]));
        var controller = new BranchesController(sender.Object) { ControllerContext = WithRole(Constants.CustomerRole) };

        var result = await controller.GetBranches(includeInactive: true, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        sender.Verify(s => s.Send(new GetBranchesQuery(true, Constants.CustomerRole), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBranch_Passes_The_Callers_Role_And_Returns_Ok()
    {
        var id = Guid.CreateVersion7();
        var response = new BranchResponse(
            id, "PG", "Pettah", null, "12 Main Street", "+94112345678", false, DateTime.UtcNow, null);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(new GetBranchQuery(id, Constants.AdminRole), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchResponse>.Success(response));
        var controller = new BranchesController(sender.Object) { ControllerContext = WithRole(Constants.AdminRole) };

        var result = await controller.GetBranch(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public async Task GetBranch_Not_Found_Returns_404()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetBranchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchResponse>.Failure(new Error(404, "not found")));
        var controller = new BranchesController(sender.Object) { ControllerContext = WithRole(Constants.CustomerRole) };

        var result = await controller.GetBranch(Guid.CreateVersion7(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    private static ControllerContext WithRole(string role) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Constants.RoleClaimType, role)], "Test"))
        }
    };
}
