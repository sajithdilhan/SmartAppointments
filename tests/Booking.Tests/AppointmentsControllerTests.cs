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
    private static readonly Guid BranchId = Guid.CreateVersion7();
    private static readonly DateTime Start = new(2030, 1, 7, 3, 30, 0, DateTimeKind.Utc);

    private readonly Mock<ISender> _sender = new();

    private static AppointmentResponse Response() => new(
        Guid.CreateVersion7(), UserId, SlotId, Guid.CreateVersion7(), Guid.CreateVersion7(),
        Start, Start.AddMinutes(30), AppointmentStatus.Booked, Start, Start, null, null, null);

    private AppointmentsController Controller(
        string? sub = null, string role = Constants.CustomerRole, bool withSub = true, string? branchClaim = null)
    {
        var claims = new List<Claim> { new(Constants.RoleClaimType, role) };
        if (branchClaim is not null)
        {
            claims.Add(new Claim(Constants.BranchIdClaimType, branchClaim));
        }

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
        _sender.Setup(s => s.Send(new GetAppointmentQuery(response.Id, UserId, Constants.StaffRole, BranchId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Success(response));

        var result = await Controller(role: Constants.StaffRole, branchClaim: BranchId.ToString()).GetById(response.Id, CancellationToken.None);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task A_Missing_Malformed_Or_Empty_Branch_Claim_Reaches_The_Query_As_Null(string? claim)
    {
        var id = Guid.CreateVersion7();
        _sender.Setup(s => s.Send(It.IsAny<GetAppointmentQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Failure(new Error(404, "nope")));

        await Controller(role: Constants.StaffRole, branchClaim: claim).GetById(id, CancellationToken.None);

        _sender.Verify(s => s.Send(new GetAppointmentQuery(id, UserId, Constants.StaffRole, null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(AppointmentOutcome.Completed)]
    [InlineData(AppointmentOutcome.NoShow)]
    public async Task Mark_Successful_Returns_Ok_With_The_Appointment_And_Passes_Caller_And_Branch(AppointmentOutcome outcome)
    {
        var response = Response();
        _sender.Setup(s => s.Send(
                new MarkAppointmentCommand(response.Id, outcome, UserId, Constants.StaffRole, BranchId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Success(response));
        var controller = Controller(role: Constants.StaffRole, branchClaim: BranchId.ToString());

        var result = outcome == AppointmentOutcome.Completed
            ? await controller.Complete(response.Id, CancellationToken.None)
            : await controller.NoShow(response.Id, CancellationToken.None);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Mark_Without_A_Branch_Claim_Sends_A_Null_Branch()
    {
        var id = Guid.CreateVersion7();
        _sender.Setup(s => s.Send(It.IsAny<MarkAppointmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Failure(new Error(404, "nope")));

        await Controller(role: Constants.AdminRole).Complete(id, CancellationToken.None);

        _sender.Verify(s => s.Send(
            new MarkAppointmentCommand(id, AppointmentOutcome.Completed, UserId, Constants.AdminRole, null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(404, 404)]
    [InlineData(409, 409)]
    public async Task Mark_Failure_Maps_To_The_Handler_Status(int status, int expected)
    {
        _sender.Setup(s => s.Send(It.IsAny<MarkAppointmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AppointmentResponse>.Failure(new Error(status, "failure")));
        var controller = Controller(role: Constants.StaffRole);

        var complete = await controller.Complete(Guid.CreateVersion7(), CancellationToken.None);
        var noShow = await controller.NoShow(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(complete).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(noShow).StatusCode);
    }

    [Fact]
    public async Task Mark_With_A_Missing_Sub_Is_401()
    {
        var controller = Controller(withSub: false, role: Constants.StaffRole);

        Assert.IsType<UnauthorizedObjectResult>(await controller.Complete(Guid.CreateVersion7(), CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await controller.NoShow(Guid.CreateVersion7(), CancellationToken.None));
        _sender.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(AppointmentsController.Complete), "{id:guid}/complete")]
    [InlineData(nameof(AppointmentsController.NoShow), "{id:guid}/no-show")]
    public void Mark_Actions_Are_Posts_On_Their_Routes(string action, string template)
    {
        var attribute = typeof(AppointmentsController).GetMethod(action)!.GetCustomAttribute<HttpPostAttribute>();

        Assert.Equal(template, attribute!.Template);
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

    private static BranchAppointmentsPage EmptyBranchPage() => new([], 1, 20, 0, false);

    [Fact]
    public async Task GetByBranch_Sends_The_Role_Branch_Claim_And_Raw_Query_Strings()
    {
        var claimBranch = Guid.CreateVersion7();
        _sender.Setup(s => s.Send(It.IsAny<ListBranchAppointmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchAppointmentsPage>.Success(EmptyBranchPage()));

        var result = await Controller(role: Constants.StaffRole, branchClaim: claimBranch.ToString())
            .GetByBranch(BranchId, "2030-03-10", "noshow", "2", "5", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _sender.Verify(s => s.Send(
            new ListBranchAppointmentsQuery(BranchId, "2030-03-10", "noshow", "2", "5", Constants.StaffRole, claimBranch),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetByBranch_Treats_A_Missing_Or_Bad_Branch_Claim_As_Null(string? claim)
    {
        _sender.Setup(s => s.Send(It.IsAny<ListBranchAppointmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchAppointmentsPage>.Success(EmptyBranchPage()));

        await Controller(role: Constants.StaffRole, branchClaim: claim)
            .GetByBranch(BranchId, null, null, null, null, CancellationToken.None);

        _sender.Verify(s => s.Send(
            new ListBranchAppointmentsQuery(BranchId, null, null, null, null, Constants.StaffRole, null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByBranch_Keeps_An_Empty_Query_Value_Empty()
    {
        _sender.Setup(s => s.Send(It.IsAny<ListBranchAppointmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchAppointmentsPage>.Success(EmptyBranchPage()));
        var controller = Controller(role: Constants.AdminRole);
        controller.HttpContext.Request.QueryString = new QueryString("?date=&status=");

        await controller.GetByBranch(BranchId, null, null, null, null, CancellationToken.None);

        _sender.Verify(s => s.Send(
            new ListBranchAppointmentsQuery(BranchId, "", "", null, null, Constants.AdminRole, null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task GetByBranch_Failure_Maps_To_The_Handler_Status(int status)
    {
        _sender.Setup(s => s.Send(It.IsAny<ListBranchAppointmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BranchAppointmentsPage>.Failure(new Error(status, "x")));

        var result = await Controller(role: Constants.AdminRole)
            .GetByBranch(BranchId, null, null, null, null, CancellationToken.None);

        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task GetByBranch_Without_A_Valid_Sub_Is_401()
    {
        var result = await Controller(withSub: false, role: Constants.AdminRole)
            .GetByBranch(BranchId, null, null, null, null, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public void GetByBranch_Is_Routed_As_Branch_With_A_Guid()
    {
        Assert.Equal("branch/{branchId:guid}", typeof(AppointmentsController)
            .GetMethod(nameof(AppointmentsController.GetByBranch))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
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
    [InlineData(nameof(AppointmentsController.Complete), Constants.AdminOrStaffPolicy)]
    [InlineData(nameof(AppointmentsController.NoShow), Constants.AdminOrStaffPolicy)]
    [InlineData(nameof(AppointmentsController.GetByBranch), Constants.AdminOrStaffPolicy)]
    public void Each_Action_Carries_Its_Policy(string action, string policy)
    {
        var attribute = typeof(AppointmentsController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(policy, attribute!.Policy);
    }
}
