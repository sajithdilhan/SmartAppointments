using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.BuildingBlocks.Web.Middlewares;
using System.Text.Json;

namespace BuildingBlocks.Tests;

public class ExceptionMiddlewareTests
{
    [Fact]
    public async Task A_Request_That_Does_Not_Throw_Is_Passed_Through_Untouched()
    {
        var context = new DefaultHttpContext();
        var subject = new ExceptionMiddleware(_ => Task.CompletedTask, NullLogger<ExceptionMiddleware>.Instance);

        await subject.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    public static TheoryData<Exception, int, string> Mappings => new()
    {
        { new UnauthorizedAccessException("token for user 42 expired"), StatusCodes.Status401Unauthorized, ExceptionMiddleware.UnauthorizedMessage },
        { new BadHttpRequestException("body too large: 99MB", StatusCodes.Status413PayloadTooLarge), StatusCodes.Status413PayloadTooLarge, ExceptionMiddleware.BadRequestMessage },
        { new BadHttpRequestException("unexpected end of request content"), StatusCodes.Status400BadRequest, ExceptionMiddleware.BadRequestMessage },
        // These two used to be 400 with the exception message echoed back to the caller.
        { new InvalidOperationException("Sequence contains no elements"), StatusCodes.Status500InternalServerError, ExceptionMiddleware.UnexpectedErrorMessage },
        { new ArgumentNullException("connectionString"), StatusCodes.Status500InternalServerError, ExceptionMiddleware.UnexpectedErrorMessage },
        { new NotSupportedException("nope"), StatusCodes.Status500InternalServerError, ExceptionMiddleware.UnexpectedErrorMessage },
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task Exceptions_Map_To_A_Status_And_A_Fixed_Message(Exception exception, int expectedStatus, string expectedDetail)
    {
        var context = await InvokeWith(exception);

        var problem = await ReadProblemAsync(context);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal(expectedDetail, problem.Detail);
        Assert.NotEqual(exception.Message, problem.Detail);
    }

    [Fact]
    public async Task An_Unexpected_Failure_Does_Not_Leak_Its_Message()
    {
        // A 500 body reaches the caller, so it must not carry connection strings, SQL or
        // stack detail that happens to be in the exception message.
        var context = await InvokeWith(new Exception("Npgsql: password authentication failed for user 'postgres'"));

        var problem = await ReadProblemAsync(context);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.DoesNotContain("postgres", problem.Detail);
    }

    private static async Task<DefaultHttpContext> InvokeWith(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var subject = new ExceptionMiddleware(_ => throw exception, NullLogger<ExceptionMiddleware>.Instance);
        await subject.InvokeAsync(context);

        return context;
    }

    private static async Task<ApiProblemDetails> ReadProblemAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return JsonSerializer.Deserialize<ApiProblemDetails>(body)!;
    }
}
