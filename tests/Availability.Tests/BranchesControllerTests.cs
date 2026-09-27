using Availability.Api.Controllers;
using Availability.Application.Commands;
using Availability.Application.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;
using System.Reflection;

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

        var created = Assert.IsType<CreatedResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal($"/api/branches/{response.Id}", created.Location);
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

    [Fact]
    public void Create_Requires_The_Admin_Policy()
    {
        // The unit tests construct the controller directly, so the authorization middleware never
        // runs here. Asserting on the attribute is what stops a refactor silently opening the
        // endpoint to every signed-in customer.
        var attribute = typeof(BranchesController)
            .GetMethod(nameof(BranchesController.Create))!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(Constants.AdminPolicy, attribute.Policy);
    }
}
