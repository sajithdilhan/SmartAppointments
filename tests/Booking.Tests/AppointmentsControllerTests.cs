using System.Reflection;
using System.Security.Claims;
using Booking.Api.Controllers;
using Booking.Application.Commands;
using Booking.Application.Models;
using Booking.Application.Queries;
using Booking.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Tests;

public class AppointmentsControllerTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid SlotId = Guid.CreateVersion7();
    private static readonly DateTime Start = new(2030, 1, 7, 3, 30, 0, DateTimeKind.Utc);

    private readonly Mock<ISender> _sender = new();

    private static AppointmentResponse Response() => new(
        Guid.CreateVersion7(), UserId, SlotId, Guid.CreateVersion7(), Guid.CreateVersion7(),
        Start, Start.AddMinutes(30), AppointmentStatus.Booked, Start, Start, null);

    private AppointmentsController Controller(string? sub = null, string role = Constants.CustomerRole, bool withSub = true)
    {
        var claims = new List<Claim> { new(Constants.RoleClaimType, role) };
        if (withSub)
        {
            claims.Add(new Claim(Constants.UserIdClaimType, sub ?? UserId.ToString()));
        }

        return new AppointmentsController(_sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
            }
        };
    }

    [Fact]
    public async Task Create_Successful_Returns_201_With_Location_Of_GetById()
    {
        var response = Response();
        _sender.Setup(s => s.Send(new CreateAppointmentCommand(UserId, "key-1", SlotId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Success(response));

        var result = await Controller().Create("key-1", new CreateAppointmentRequest(SlotId), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(AppointmentsController.GetById), created.ActionName);
        Assert.Equal(response.Id, created.RouteValues!["id"]);
        Assert.Same(response, created.Value);
    }

    [Fact]
    public async Task A_Replayed_Success_Renders_The_Same_201_And_Location_As_The_First()
    {
        var stored = Response();
        _sender.SetupSequence(s => s.Send(It.IsAny<CreateAppointmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Success(stored))
            .ReturnsAsync(Result<AppointmentResponse>.Success(stored with { }));
        var controller = Controller();

        var first = Assert.IsType<CreatedAtActionResult>(await controller.Create("k", new CreateAppointmentRequest(SlotId), CancellationToken.None));
        var replay = Assert.IsType<CreatedAtActionResult>(await controller.Create("k", new CreateAppointmentRequest(SlotId), CancellationToken.None));

        Assert.Equal(first.RouteValues!["id"], replay.RouteValues!["id"]);
        Assert.Equal(first.Value, replay.Value);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestObjectResult))]
    [InlineData(404, typeof(NotFoundObjectResult))]
    [InlineData(409, typeof(ConflictObjectResult))]
    [InlineData(422, typeof(UnprocessableEntityObjectResult))]
    public async Task Create_Failure_Maps_To_The_Handler_Status(int status, Type expected)
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateAppointmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Failure(new Error(status, "failure")));

        var result = await Controller().Create("k", new CreateAppointmentRequest(SlotId), CancellationToken.None);

        Assert.IsType(expected, result);
    }

    [Fact]
    public async Task Create_Failure_503_Maps_To_503()
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateAppointmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Failure(new Error(503, "down")));

        var result = await Controller().Create("k", new CreateAppointmentRequest(SlotId), CancellationToken.None);

        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task A_Missing_Header_Reaches_The_Command_As_Null()
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateAppointmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Failure(new Error(400, "required")));

        await Controller().Create(null, new CreateAppointmentRequest(SlotId), CancellationToken.None);

        _sender.Verify(s => s.Send(It.Is<CreateAppointmentCommand>(c => c.IdempotencyKey == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "not-a-guid")]
    public async Task A_Missing_Or_Unparseable_Sub_Is_401_On_Every_Action(bool withSub, string? sub)
    {
        var controller = Controller(sub, withSub: withSub);

        Assert.IsType<UnauthorizedObjectResult>(await controller.Create("k", new CreateAppointmentRequest(SlotId), CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await controller.GetById(Guid.CreateVersion7(), CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Cancel(Guid.CreateVersion7(), CancellationToken.None));
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetById_Successful_Passes_Caller_And_Role_And_Returns_Ok()
    {
        var response = Response();
        _sender.Setup(s => s.Send(new GetAppointmentQuery(response.Id, UserId, Constants.StaffRole), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Success(response));

        var result = await Controller(role: Constants.StaffRole).GetById(response.Id, CancellationToken.None);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task GetById_NotFound_Maps_To_404()
    {
        _sender.Setup(s => s.Send(It.IsAny<GetAppointmentQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Failure(new Error(404, "nope")));

        Assert.IsType<NotFoundObjectResult>(await Controller().GetById(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_Successful_Returns_NoContent()
    {
        var id = Guid.CreateVersion7();
        _sender.Setup(s => s.Send(new CancelAppointmentCommand(id, UserId, Constants.CustomerRole), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        Assert.IsType<NoContentResult>(await Controller().Cancel(id, CancellationToken.None));
    }

    [Theory]
    [InlineData(404, 404)]
    [InlineData(409, 409)]
    [InlineData(503, 503)]
    public async Task Cancel_Failure_Maps_To_The_Handler_Status(int status, int expected)
    {
        _sender.Setup(s => s.Send(It.IsAny<CancelAppointmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new Error(status, "failure")));

        var result = await Controller().Cancel(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(nameof(AppointmentsController.Create), Constants.CustomerPolicy)]
    [InlineData(nameof(AppointmentsController.GetById), Constants.AllowedOriginsPolicy)]
    [InlineData(nameof(AppointmentsController.Cancel), Constants.AllowedOriginsPolicy)]
    public void Each_Action_Carries_Its_Policy(string action, string policy)
    {
        var attribute = typeof(AppointmentsController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(policy, attribute!.Policy);
    }
}
