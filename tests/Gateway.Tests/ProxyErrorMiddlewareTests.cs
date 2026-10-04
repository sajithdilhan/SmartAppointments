using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using SmartAppointments.BuildingBlocks.Models;
using SmartAppointments.Gateway.Proxy;
using System.Text.Json;
using Yarp.ReverseProxy.Forwarder;

namespace Gateway.Tests;

public class ProxyErrorMiddlewareTests
{
    private sealed class FakeForwarderErrorFeature(ForwarderError error, Exception? exception = null) : IForwarderErrorFeature
    {
        public ForwarderError Error { get; } = error;
        public Exception? Exception { get; } = exception;
    }

    private sealed class TestHttpResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; private set; }

        public void MarkStarted() => HasStarted = true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> BodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    /// <summary>Runs the middleware with an inner step that records a forwarding failure, as YARP does.</summary>
    private static async Task<DefaultHttpContext> RunAsync(ForwarderError? error, Exception? exception = null, int forwarderStatus = 0)
    {
        var context = NewContext();
        var subject = new ProxyErrorMiddleware(ctx =>
        {
            if (forwarderStatus != 0)
            {
                ctx.Response.StatusCode = forwarderStatus;
            }
            if (error is not null)
            {
                ctx.Features.Set<IForwarderErrorFeature>(new FakeForwarderErrorFeature(error.Value, exception));
            }
            return Task.CompletedTask;
        });

        await subject.InvokeAsync(context);
        return context;
    }

    [Theory]
    [InlineData(ForwarderError.Request)]
    [InlineData(ForwarderError.RequestBodyClient)]
    [InlineData(ForwarderError.RequestBodyDestination)]
    [InlineData(ForwarderError.ResponseHeaders)]
    public async Task A_Failure_To_Reach_The_Service_Is_A_502_Problem(ForwarderError error)
    {
        var context = await RunAsync(error, forwarderStatus: StatusCodes.Status502BadGateway);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        var problem = JsonSerializer.Deserialize<ApiProblemDetails>(await BodyAsync(context), JsonSerializerOptions.Web);
        Assert.Equal(new ApiProblemDetails(502, ProxyErrorMiddleware.UnavailableMessage), problem);
    }

    [Fact]
    public async Task A_Timeout_Is_A_504_Problem()
    {
        var context = await RunAsync(ForwarderError.RequestTimedOut, forwarderStatus: StatusCodes.Status504GatewayTimeout);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        var problem = JsonSerializer.Deserialize<ApiProblemDetails>(await BodyAsync(context), JsonSerializerOptions.Web);
        Assert.Equal(new ApiProblemDetails(504, ProxyErrorMiddleware.TimeoutMessage), problem);
    }

    [Theory]
    [InlineData(ForwarderError.RequestCanceled)]
    [InlineData(ForwarderError.RequestBodyCanceled)]
    [InlineData(ForwarderError.ResponseBodyCanceled)]
    public async Task A_Client_Cancellation_Writes_Nothing(ForwarderError error)
    {
        var context = await RunAsync(error, forwarderStatus: 499);

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
        Assert.Equal(string.Empty, await BodyAsync(context));
    }

    [Fact]
    public async Task Without_An_Error_Feature_The_Response_Is_Left_Untouched()
    {
        var context = await RunAsync(error: null, forwarderStatus: StatusCodes.Status404NotFound);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
        Assert.Equal(string.Empty, await BodyAsync(context));
    }

    [Fact]
    public async Task A_Feature_Reporting_No_Error_Is_Ignored()
    {
        var context = await RunAsync(ForwarderError.None);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(string.Empty, await BodyAsync(context));
    }

    [Fact]
    public async Task A_Response_That_Has_Started_Is_Not_Written_Again()
    {
        var context = NewContext();
        var response = new TestHttpResponseFeature();
        context.Features.Set<IHttpResponseFeature>(response);
        var subject = new ProxyErrorMiddleware(ctx =>
        {
            ctx.Features.Set<IForwarderErrorFeature>(new FakeForwarderErrorFeature(ForwarderError.ResponseBodyDestination));
            response.MarkStarted();
            return Task.CompletedTask;
        });

        await subject.InvokeAsync(context);

        Assert.True(context.Response.HasStarted);
        Assert.Null(context.Response.ContentType);
        Assert.Equal(0, response.Body.Length);
    }

    [Fact]
    public async Task The_Body_Never_Contains_The_Exception_Text_Or_The_Destination()
    {
        var exception = new HttpRequestException("Connection refused (localhost:5202) secret-detail");

        var context = await RunAsync(ForwarderError.Request, exception, StatusCodes.Status502BadGateway);

        var body = await BodyAsync(context);
        Assert.DoesNotContain("secret-detail", body);
        Assert.DoesNotContain("localhost", body);
        Assert.DoesNotContain("5202", body);
    }
}
