using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.Gateway.RateLimiting;

namespace Gateway.Tests;

public class RateLimitRejectionTests
{
    private sealed class FakeLease(TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;
        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = retryAfter;
            return retryAfter is not null && metadataName == MetadataName.RetryAfter.Name;
        }
    }

    private static async Task<DefaultHttpContext> RejectAsync(TimeSpan? retryAfter)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        await RateLimitPolicies.RejectAsync(new OnRejectedContext { HttpContext = context, Lease = new FakeLease(retryAfter) }, CancellationToken.None);
        return context;
    }

    [Fact]
    public async Task Writes_A_429_Problem_With_The_Lease_Retry_After_Rounded_Up()
    {
        var context = await RejectAsync(TimeSpan.FromSeconds(12.2));

        Assert.Equal(429, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        Assert.Equal("13", context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Retry_After_Is_Never_Less_Than_One_Second()
    {
        var context = await RejectAsync(TimeSpan.FromMilliseconds(10));

        Assert.Equal("1", context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Retry_After_Is_The_Window_Length_Without_Lease_Metadata()
    {
        var context = await RejectAsync(null);

        Assert.Equal("60", context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task The_Body_Is_An_ApiProblemDetails()
    {
        var context = await RejectAsync(TimeSpan.FromSeconds(5));
        context.Response.Body.Position = 0;

        var problem = JsonSerializer.Deserialize<ApiProblemDetails>(await new StreamReader(context.Response.Body).ReadToEndAsync());

        Assert.NotNull(problem);
        Assert.Equal(429, problem.Status);
        Assert.Equal("Too many requests. Try again later.", problem.Detail);
    }
}
