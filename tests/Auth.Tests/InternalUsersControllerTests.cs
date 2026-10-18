using System.Reflection;
using Auth.Api.Controllers;
using Auth.Application.Models;
using Auth.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Tests;

public class InternalUsersControllerTests
{
    private readonly Mock<ISender> _sender = new();

    [Fact]
    public async Task Lookup_Returns_200_With_The_Items_And_Dispatches_The_Ids()
    {
        var id = Guid.NewGuid();
        IReadOnlyList<UserLookupItem> items = [new UserLookupItem(id, "Cam", "Customer", "+15551234567")];
        _sender.Setup(s => s.Send(It.IsAny<LookupUsersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<UserLookupItem>>.Success(items));

        var result = await new InternalUsersController(_sender.Object).Lookup(new LookupUsersRequest([id]), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(items, ok.Value);
        _sender.Verify(s => s.Send(It.Is<LookupUsersQuery>(q => q.Ids!.SequenceEqual(new[] { id })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lookup_Maps_A_Failure_To_Its_Status()
    {
        _sender.Setup(s => s.Send(It.IsAny<LookupUsersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<UserLookupItem>>.Failure(new Error(400, "bad")));

        var result = await new InternalUsersController(_sender.Object).Lookup(new LookupUsersRequest(null), CancellationToken.None);

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
    }

    [Fact]
    public void The_Controller_Accepts_Only_The_Internal_Service_Policy()
    {
        var authorize = typeof(InternalUsersController).GetCustomAttribute<AuthorizeAttribute>();
        var route = typeof(InternalUsersController).GetCustomAttribute<RouteAttribute>();

        Assert.Equal(Constants.InternalServicePolicy, authorize!.Policy);
        Assert.Equal("internal/users", route!.Template);
        Assert.Equal("lookup", typeof(InternalUsersController).GetMethod(nameof(InternalUsersController.Lookup))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
    }
}
