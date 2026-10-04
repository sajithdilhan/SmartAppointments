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
        Assert.IsType<UnauthorizedObjectResult>(await controller.GetMine(null, null, null, null, CancellationToken.None));
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

    [Fact]
    public async Task GetMine_Successful_Returns_Ok_With_The_Page()
    {
        var page = new PagedResponse<AppointmentResponse>([Response()], 1, 20, 1);
        _sender.Setup(s => s.Send(It.IsAny<ListMyAppointmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResponse<AppointmentResponse>>.Success(page));

        var result = await Controller().GetMine(null, null, null, null, CancellationToken.None);

        Assert.Same(page, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task GetMine_Sends_The_Sub_And_The_Raw_Query_Strings_Unchanged()
    {
        _sender.Setup(s => s.Send(It.IsAny<ListMyAppointmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResponse<AppointmentResponse>>.Success(new([], 1, 20, 0)));

        await Controller().GetMine("booked", "", "abc", "0", CancellationToken.None);

        _sender.Verify(s => s.Send(
            new ListMyAppointmentsQuery(UserId, "booked", "", "abc", "0"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("?status=", "", null, null, null)]
    [InlineData("?when=&page=", null, "", "", null)]
    [InlineData("?pageSize=", null, null, null, "")]
    [InlineData("", null, null, null, null)]
    public async Task GetMine_Keeps_An_Empty_Query_Value_Empty_And_An_Omitted_One_Null(
        string queryString, string? status, string? when, string? page, string? pageSize)
    {
        _sender.Setup(s => s.Send(It.IsAny<ListMyAppointmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResponse<AppointmentResponse>>.Success(new([], 1, 20, 0)));
        var controller = Controller();
        controller.HttpContext.Request.QueryString = new QueryString(queryString);

        // MVC binds an empty value to null, so the action is called with null for it as well.
        await controller.GetMine(null, null, null, null, CancellationToken.None);

        _sender.Verify(s => s.Send(
            new ListMyAppointmentsQuery(UserId, status, when, page, pageSize),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMine_Failure_400_Maps_To_400()
    {
        _sender.Setup(s => s.Send(It.IsAny<ListMyAppointmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResponse<AppointmentResponse>>.Failure(new Error(400, "bad")));

        var result = await Controller().GetMine("x", null, null, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void GetMine_Is_Routed_As_My_And_GetById_Is_Unchanged()
    {
        Assert.Equal("my", typeof(AppointmentsController).GetMethod(nameof(AppointmentsController.GetMine))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{id:guid}", typeof(AppointmentsController).GetMethod(nameof(AppointmentsController.GetById))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void GetMine_Takes_No_Customer_Id()
    {
        var names = typeof(AppointmentsController).GetMethod(nameof(AppointmentsController.GetMine))!
            .GetParameters().Select(p => p.Name);

        Assert.DoesNotContain(names, n => string.Equals(n, "customerId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(nameof(AppointmentsController.Create), Constants.CustomerPolicy)]
    [InlineData(nameof(AppointmentsController.GetMine), Constants.CustomerPolicy)]
    [InlineData(nameof(AppointmentsController.GetById), Constants.AllowedOriginsPolicy)]
    [InlineData(nameof(AppointmentsController.Cancel), Constants.AllowedOriginsPolicy)]
    public void Each_Action_Carries_Its_Policy(string action, string policy)
    {
        var attribute = typeof(AppointmentsController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(policy, attribute!.Policy);
    }
}
