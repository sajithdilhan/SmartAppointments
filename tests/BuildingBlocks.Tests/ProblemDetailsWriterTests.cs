using Microsoft.AspNetCore.Http;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.BuildingBlocks.Web.Results;
using System.Text.Json;

namespace BuildingBlocks.Tests;

public class ProblemDetailsWriterTests
{
    private static async Task<(DefaultHttpContext Context, string Body)> WriteAsync(int status, string detail)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await ProblemDetailsWriter.WriteAsync(context, status, detail);

        context.Response.Body.Position = 0;
        return (context, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task Sets_The_Status_And_The_Problem_Content_Type()
    {
        var (context, _) = await WriteAsync(502, "A downstream service is unavailable.");

        Assert.Equal(502, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task Writes_An_ApiProblemDetails_Body_With_Camel_Case_Keys()
    {
        var (_, body) = await WriteAsync(429, "Too many requests.");

        Assert.Equal("{\"status\":429,\"detail\":\"Too many requests.\"}", body);
        var problem = JsonSerializer.Deserialize<ApiProblemDetails>(body, JsonSerializerOptions.Web);
        Assert.Equal(new ApiProblemDetails(429, "Too many requests."), problem);
    }
}
