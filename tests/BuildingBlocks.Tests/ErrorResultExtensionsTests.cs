using Microsoft.AspNetCore.Mvc;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.BuildingBlocks.Web.Results;

namespace BuildingBlocks.Tests;

public class ErrorResultExtensionsTests
{
    [Theory]
    [InlineData(400, typeof(BadRequestObjectResult), 400)]
    [InlineData(401, typeof(UnauthorizedObjectResult), 401)]
    [InlineData(403, typeof(ObjectResult), 403)]
    [InlineData(404, typeof(NotFoundObjectResult), 404)]
    [InlineData(409, typeof(ConflictObjectResult), 409)]
    [InlineData(418, typeof(ObjectResult), 500)]
    [InlineData(500, typeof(ObjectResult), 500)]
    public void Each_Status_Maps_To_Its_Result(int status, Type expectedType, int expectedStatus)
    {
        var error = new Error(status, "failure");

        var result = new TestController().ToActionResult(error);

        Assert.IsType(expectedType, result);
        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.Same(error, result.Value);
    }

    private sealed class TestController : ControllerBase;
}
