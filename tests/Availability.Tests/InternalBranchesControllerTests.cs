using Availability.Api.Controllers;
using Availability.Application.Models;
using Availability.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;
using System.Reflection;

namespace Availability.Tests;

public class InternalBranchesControllerTests
{
    private static readonly Guid BranchId = Guid.CreateVersion7();

    [Fact]
    public async Task Get_Successful_Returns_Ok_With_The_Branch()
    {
        var response = new InternalBranchResponse(BranchId, false, "Europe/London");
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(new GetInternalBranchQuery(BranchId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<InternalBranchResponse>.Success(response));

        var result = await new InternalBranchesController(sender.Object).Get(BranchId, CancellationToken.None);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Get_Failure_Maps_To_The_Handler_Status()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetInternalBranchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<InternalBranchResponse>.Failure(new Error(404, "failure")));

        var result = await new InternalBranchesController(sender.Object).Get(BranchId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void The_Whole_Controller_Requires_The_Internal_Service_Policy()
    {
        var attribute = typeof(InternalBranchesController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(Constants.InternalServicePolicy, attribute.Policy);
    }

    [Fact]
    public void The_Action_Does_Not_Weaken_The_Class_Policy()
    {
        var method = typeof(InternalBranchesController).GetMethod(nameof(InternalBranchesController.Get))!;

        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
    }
}
