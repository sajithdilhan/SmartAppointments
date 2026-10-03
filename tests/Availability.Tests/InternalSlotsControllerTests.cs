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

public class InternalSlotsControllerTests
{
    private static readonly Guid SlotId = Guid.CreateVersion7();
    private static readonly SlotReservationRequest Request = new(Guid.CreateVersion7());

    [Fact]
    public async Task Get_Successful_Returns_Ok_With_The_Slot()
    {
        var response = new InternalSlotResponse(SlotId, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30), 5, 1, 4);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(new GetInternalSlotQuery(SlotId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<InternalSlotResponse>.Success(response));

        var result = await new InternalSlotsController(sender.Object).Get(SlotId, CancellationToken.None);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Get_Failure_Maps_To_The_Handler_Status()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetInternalSlotQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<InternalSlotResponse>.Failure(new Error(404, "failure")));

        var result = await new InternalSlotsController(sender.Object).Get(SlotId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Reserve_Successful_Returns_NoContent_And_Passes_Both_Ids_To_The_Command()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ReserveSlotCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await new InternalSlotsController(sender.Object).Reserve(SlotId, Request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        sender.Verify(s => s.Send(new ReserveSlotCommand(SlotId, Request.AppointmentId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestObjectResult))]
    [InlineData(404, typeof(NotFoundObjectResult))]
    [InlineData(409, typeof(ConflictObjectResult))]
    public async Task Reserve_Failure_Maps_To_The_Handler_Status(int status, Type expectedResult)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ReserveSlotCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(status, "failure")));

        var result = await new InternalSlotsController(sender.Object).Reserve(SlotId, Request, CancellationToken.None);

        Assert.IsType(expectedResult, result);
        Assert.Equal(status, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task Release_Successful_Returns_NoContent_And_Passes_Both_Ids_To_The_Command()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ReleaseSlotCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await new InternalSlotsController(sender.Object).Release(SlotId, Request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        sender.Verify(s => s.Send(new ReleaseSlotCommand(SlotId, Request.AppointmentId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestObjectResult))]
    [InlineData(404, typeof(NotFoundObjectResult))]
    public async Task Release_Failure_Maps_To_The_Handler_Status(int status, Type expectedResult)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ReleaseSlotCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(status, "failure")));

        var result = await new InternalSlotsController(sender.Object).Release(SlotId, Request, CancellationToken.None);

        Assert.IsType(expectedResult, result);
        Assert.Equal(status, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public void The_Whole_Controller_Requires_The_Internal_Service_Policy()
    {
        var attribute = typeof(InternalSlotsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(Constants.InternalServicePolicy, attribute.Policy);
    }

    [Theory]
    [InlineData(nameof(InternalSlotsController.Get))]
    [InlineData(nameof(InternalSlotsController.Reserve))]
    [InlineData(nameof(InternalSlotsController.Release))]
    public void No_Action_Weakens_The_Class_Policy(string action)
    {
        var method = typeof(InternalSlotsController).GetMethod(action)!;

        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
    }
}
