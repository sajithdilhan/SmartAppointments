using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartAppointments.Gateway.Proxy;

namespace Gateway.Tests;

public class UnmatchedRequestMiddlewareTests
{
    [Fact]
    public async Task A_Request_With_No_Endpoint_Gets_404_Without_Calling_The_Rest_Of_The_Pipeline()
    {
        var called = false;
        var subject = new UnmatchedRequestMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        await subject.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_Request_That_Matched_An_Endpoint_Continues()
    {
        var called = false;
        var subject = new UnmatchedRequestMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "route"));

        await subject.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }
}
