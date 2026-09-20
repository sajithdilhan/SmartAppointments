using Auth.Api.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SmartAppointments.BuildingBlocks.Models;
using System.Text.Json;

namespace Auth.Tests;

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

    [Theory]
    [InlineData(typeof(ArgumentNullException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(InvalidOperationException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(UnauthorizedAccessException), StatusCodes.Status401Unauthorized)]
    [InlineData(typeof(NotSupportedException), StatusCodes.Status500InternalServerError)]
    public async Task Exceptions_Map_To_Their_Status_Codes(Type exceptionType, int expectedStatus)
    {
        var context = await InvokeWith((Exception)Activator.CreateInstance(exceptionType)!);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task A_Bad_Request_Reports_The_Exception_Message()
    {
        var context = await InvokeWith(new InvalidOperationException("Email is invalid."));

        var problem = await ReadProblemAsync(context);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Email is invalid.", problem.Detail);
    }

    [Fact]
    public async Task An_Unexpected_Failure_Does_Not_Leak_Its_Message()
    {
        // A 500 body reaches the caller, so it must not carry connection strings, SQL or
        // stack detail that happens to be in the exception message.
        var context = await InvokeWith(new Exception("Npgsql: password authentication failed for user 'postgres'"));

        var problem = await ReadProblemAsync(context);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("An unexpected error occurred! Please try again later.", problem.Detail);
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
