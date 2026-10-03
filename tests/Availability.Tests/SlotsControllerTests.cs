using Availability.Api.Controllers;
using Availability.Application.Commands;
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

public class SlotsControllerTests
{
    private static readonly GenerateSlotsRequest Request =
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2030, 1, 7), new DateOnly(2030, 1, 13), 5);

    [Fact]
    public async Task Generate_Successful_Returns_Ok_And_Passes_Every_Field_To_The_Command()
    {
        var response = new GenerateSlotsResponse(12, 3);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GenerateSlotsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GenerateSlotsResponse>.Success(response));

        var result = await new SlotsController(sender.Object).Generate(Request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
        sender.Verify(s => s.Send(
            new GenerateSlotsCommand(Request.BranchId, Request.ServiceTypeId, Request.FromDate, Request.ToDate, 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestObjectResult))]
    [InlineData(404, typeof(NotFoundObjectResult))]
    [InlineData(409, typeof(ConflictObjectResult))]
    public async Task Generate_Failure_Maps_To_The_Handler_Status(int status, Type expectedResult)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GenerateSlotsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GenerateSlotsResponse>.Failure(new Error(status, "failure")));

        var result = await new SlotsController(sender.Object).Generate(Request, CancellationToken.None);

        Assert.IsType(expectedResult, result);
        Assert.Equal(status, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task GetAvailable_Successful_Returns_Ok_And_Maps_ServiceId_To_ServiceTypeId()
    {
        var branchId = Guid.CreateVersion7();
        var serviceId = Guid.CreateVersion7();
        var date = new DateOnly(2030, 1, 7);
        List<SlotResponse> slots = [];
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<SearchAvailableSlotsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<SlotResponse>>.Success(slots));

        var result = await new SlotsController(sender.Object).GetAvailable(branchId, serviceId, date, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(slots, ok.Value);
        sender.Verify(s => s.Send(
            new SearchAvailableSlotsQuery(branchId, serviceId, date), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestObjectResult))]
    [InlineData(404, typeof(NotFoundObjectResult))]
    public async Task GetAvailable_Failure_Maps_To_The_Handler_Status(int status, Type expectedResult)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<SearchAvailableSlotsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<SlotResponse>>.Failure(new Error(status, "failure")));

        var result = await new SlotsController(sender.Object)
            .GetAvailable(Guid.CreateVersion7(), Guid.CreateVersion7(), null, CancellationToken.None);

        Assert.IsType(expectedResult, result);
        Assert.Equal(status, ((ObjectResult)result).StatusCode);
    }

    [Theory]
    [InlineData(nameof(SlotsController.Generate), Constants.AdminPolicy)]
    [InlineData(nameof(SlotsController.GetAvailable), Constants.AllowedOriginsPolicy)]
    public void Each_Action_Requires_Its_Policy(string action, string policy)
    {
        var attribute = typeof(SlotsController)
            .GetMethod(action)!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(policy, attribute.Policy);
    }
}
