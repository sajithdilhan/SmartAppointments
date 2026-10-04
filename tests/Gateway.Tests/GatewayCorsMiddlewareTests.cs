using System.Text.Json;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartAppointments.BuildingBlocks.Web.Middlewares;
using SmartAppointments.Gateway.Cors;

namespace Gateway.Tests;

// The framework CorsMiddleware with the gateway policy, run in-process. The order relative to the other
// gateway middlewares and the real YARP pipeline is checked by hand (see the spec, task 8).
public class GatewayCorsMiddlewareTests
{
    private const string Allowed = "http://localhost:4200";

    private static readonly ServiceProvider Provider = Build();

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGatewayCors([Allowed, "http://localhost:8081"]);
        return services.BuildServiceProvider();
    }

    // Cors(next).InvokeAsync(context) is the middleware followed by next.
    private static CorsRunner Cors(RequestDelegate next) => new(new CorsMiddleware(
        next,
        Provider.GetRequiredService<ICorsService>(),
        Provider.GetRequiredService<ILoggerFactory>(),
        GatewayCors.PolicyName));

    // The framework adds the headers of an actual request when the response starts (OnStarting), which a
    // DefaultHttpContext never fires on its own; the runner fires it once the pipeline has returned.
    private sealed class CorsRunner(CorsMiddleware middleware)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            await middleware.Invoke(context, Provider.GetRequiredService<ICorsPolicyProvider>());
            await StartResponseAsync(context);
        }
    }

    private static Task StartResponseAsync(HttpContext context) =>
        ((StartingCallbacks)context.Features.Get<IHttpResponseFeature>()!).FireAsync();

    private sealed class StartingCallbacks : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));

        public async Task FireAsync()
        {
            for (var i = _callbacks.Count - 1; i >= 0; i--)
            {
                await _callbacks[i].Callback(_callbacks[i].State);
            }
        }
    }

    private static DefaultHttpContext Request(string method, string path, string? origin, string? requestMethod = null, string? requestHeaders = null)
    {
        var context = new DefaultHttpContext { RequestServices = Provider };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Features.Set<IHttpResponseFeature>(new StartingCallbacks());
        context.Response.Body = new MemoryStream();
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        if (requestMethod is not null)
        {
            context.Request.Headers.AccessControlRequestMethod = requestMethod;
        }

        if (requestHeaders is not null)
        {
            context.Request.Headers.AccessControlRequestHeaders = requestHeaders;
        }

        return context;
    }

    private static DefaultHttpContext Preflight(string path, string? origin, string method = "POST", string? headers = null) =>
        Request("OPTIONS", path, origin, method, headers);

    private sealed class Recorder
    {
        public bool Called { get; private set; }

        public RequestDelegate Next(int? status = null) => context =>
        {
            Called = true;
            if (status is not null)
            {
                context.Response.StatusCode = status.Value;
            }

            return Task.CompletedTask;
        };
    }

    private static bool HasAnyCorsHeader(HttpContext context) =>
        context.Response.Headers.Any(h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData("/api/appointments")]
    [InlineData("/nothing")]
    [InlineData("/internal/slots/1")]
    public async Task A_Preflight_From_An_Allowed_Origin_Is_Answered_Without_Calling_The_Rest_Of_The_Pipeline(string path)
    {
        var recorder = new Recorder();
        var context = Preflight(path, Allowed, "POST", "authorization,idempotency-key,content-type");

        await Cors(recorder.Next()).InvokeAsync(context);

        var headers = context.Response.Headers;
        Assert.False(recorder.Called);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Equal(Allowed, headers.AccessControlAllowOrigin.ToString());
        Assert.Equal("GET,POST,PUT,DELETE", headers.AccessControlAllowMethods.ToString());
        Assert.Equal("Authorization,Content-Type,Idempotency-Key,X-Correlation-ID", headers.AccessControlAllowHeaders.ToString());
        Assert.Equal("600", headers.AccessControlMaxAge.ToString());
        Assert.Equal("Origin", headers.Vary.ToString());
        Assert.False(headers.ContainsKey("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("PATCH", null)]
    [InlineData("POST", "x-evil")]
    public async Task A_Preflight_Asking_For_More_Than_Is_Allowed_Is_Answered_With_Lists_That_Omit_It(string method, string? header)
    {
        var recorder = new Recorder();
        var context = Preflight("/api/appointments", Allowed, method, header);

        await Cors(recorder.Next()).InvokeAsync(context);

        var headers = context.Response.Headers;
        Assert.False(recorder.Called);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.DoesNotContain("PATCH", headers.AccessControlAllowMethods.ToString());
        Assert.DoesNotContain("evil", headers.AccessControlAllowHeaders.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("null")]
    public async Task A_Preflight_From_Another_Origin_Is_A_Bare_204(string origin)
    {
        var recorder = new Recorder();
        var context = Preflight("/api/appointments", origin);

        await Cors(recorder.Next()).InvokeAsync(context);

        Assert.False(recorder.Called);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.False(HasAnyCorsHeader(context));
        Assert.False(context.Response.Headers.ContainsKey("Vary"));
    }

    [Fact]
    public async Task An_Options_Request_Without_An_Origin_Is_Not_A_Preflight()
    {
        var recorder = new Recorder();
        var context = Request("OPTIONS", "/api/appointments", origin: null, requestMethod: "POST");

        await Cors(recorder.Next()).InvokeAsync(context);

        Assert.True(recorder.Called);
        Assert.False(HasAnyCorsHeader(context));
    }

    [Fact]
    public async Task An_Options_Request_Without_A_Requested_Method_Is_Not_A_Preflight()
    {
        var recorder = new Recorder();
        var context = Request("OPTIONS", "/api/appointments", Allowed);

        await Cors(recorder.Next()).InvokeAsync(context);

        Assert.True(recorder.Called);
    }

    [Fact]
    public async Task An_Actual_Request_From_An_Allowed_Origin_Gets_The_Headers_And_Continues()
    {
        var recorder = new Recorder();
        var context = Request("GET", "/api/branches", Allowed);

        await Cors(recorder.Next()).InvokeAsync(context);

        var headers = context.Response.Headers;
        Assert.True(recorder.Called);
        Assert.Equal(Allowed, headers.AccessControlAllowOrigin.ToString());
        Assert.Equal("X-Correlation-ID,Retry-After,Location", headers.AccessControlExposeHeaders.ToString());
        Assert.Equal("Origin", headers.Vary.ToString());
        Assert.False(headers.ContainsKey("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task An_Actual_Request_Without_An_Origin_Gets_No_Header_And_No_Vary()
    {
        var recorder = new Recorder();
        var context = Request("GET", "/api/branches", origin: null);

        await Cors(recorder.Next()).InvokeAsync(context);

        Assert.True(recorder.Called);
        Assert.False(HasAnyCorsHeader(context));
        Assert.False(context.Response.Headers.ContainsKey("Vary"));
    }

    [Fact]
    public async Task An_Actual_Request_From_Another_Origin_Gets_No_Header_And_Is_Not_Rejected()
    {
        var recorder = new Recorder();
        var context = Request("GET", "/api/branches", "https://evil.example");

        await Cors(recorder.Next()).InvokeAsync(context);

        Assert.True(recorder.Called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(HasAnyCorsHeader(context));
    }

    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status429TooManyRequests)]
    public async Task A_Later_Failure_Keeps_The_Headers(int status)
    {
        var recorder = new Recorder();
        var context = Request("GET", "/api/branches", Allowed);

        await Cors(recorder.Next(status)).InvokeAsync(context);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(Allowed, context.Response.Headers.AccessControlAllowOrigin.ToString());
        Assert.Equal("X-Correlation-ID,Retry-After,Location", context.Response.Headers.AccessControlExposeHeaders.ToString());
    }

    [Fact]
    public async Task An_Exception_Below_Cors_Becomes_A_500_Problem_Body_That_Keeps_The_Headers()
    {
        var cors = Cors(_ => throw new InvalidOperationException("boom"));
        var exception = new ExceptionMiddleware(cors.InvokeAsync, NullLogger<ExceptionMiddleware>.Instance);
        var context = Request("GET", "/api/branches", Allowed);

        await exception.InvokeAsync(context);
        await StartResponseAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(Allowed, context.Response.Headers.AccessControlAllowOrigin.ToString());
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(ExceptionMiddleware.UnexpectedErrorMessage, body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_Service_Sending_Cors_Headers_Is_Overridden_By_The_Policy_With_The_Transform_In_Place()
    {
        var proxy = new HttpResponseMessage();
        proxy.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", "*");
        proxy.Headers.TryAddWithoutValidation("Access-Control-Allow-Credentials", "true");
        var cors = Cors(next: context =>
        {
            // What YARP does: copy the service headers over the response, then run the transform.
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            DownstreamCorsHeaderTransform.Apply(
                context, proxy,
                Provider.GetRequiredService<ICorsService>(),
                Provider.GetRequiredService<CorsPolicy>());
            return Task.CompletedTask;
        });
        var context = Request("GET", "/api/branches", Allowed);

        await cors.InvokeAsync(context);

        var headers = context.Response.Headers;
        Assert.Equal(Allowed, headers.AccessControlAllowOrigin.ToString());
        Assert.Single(headers.AccessControlAllowOrigin);
        Assert.False(headers.ContainsKey("Access-Control-Allow-Credentials"));
        Assert.Equal("Origin", headers.Vary.ToString());
        Assert.Single(headers.Vary);
    }
}
