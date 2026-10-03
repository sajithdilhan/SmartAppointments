using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Web.Middlewares;

namespace BuildingBlocks.Tests;

public class CorrelationIdMiddlewareTests
{
    private const string Header = Constants.CorrelationIdHeaderName;

    private sealed class RecordingLogger : ILogger<CorrelationIdMiddleware>
    {
        public List<object?> Scopes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Scopes.Add(state);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    // DefaultHttpContext never starts a response, so its OnStarting callbacks never run; this feature keeps them.
    private sealed class StartingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));

        public async Task StartAsync()
        {
            foreach (var (callback, state) in Enumerable.Reverse(_callbacks))
            {
                await callback(state);
            }
        }
    }

    private static CorrelationIdMiddleware Create(RequestDelegate next, ILogger<CorrelationIdMiddleware>? logger = null) =>
        new(next, logger ?? NullLogger<CorrelationIdMiddleware>.Instance);

    private static DefaultHttpContext ContextWith(params string[] values)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartingResponseFeature());
        if (values.Length > 0)
        {
            context.Request.Headers[Header] = new StringValues(values);
        }

        return context;
    }

    private static Task StartResponseAsync(HttpContext context) =>
        ((StartingResponseFeature)context.Features.Get<IHttpResponseFeature>()!).StartAsync();

    [Theory]
    [InlineData("abc-123_X.y")]
    [InlineData("a")]
    public async Task A_Well_Formed_Id_Is_Kept(string id)
    {
        var context = ContextWith(id);

        await Create(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal(id, context.GetCorrelationId());
    }

    [Fact]
    public async Task An_Id_Of_Exactly_64_Characters_Is_Kept()
    {
        var id = new string('a', 64);
        var context = ContextWith(id);

        await Create(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal(id, context.GetCorrelationId());
    }

    public static TheoryData<string[]> Rejected => new()
    {
        Array.Empty<string>(),
        new[] { "" },
        new[] { new string('a', 65) },
        new[] { "has space" },
        new[] { "semi;colon" },
        new[] { "café" },
        new[] { "one", "two" },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public async Task A_Missing_Or_Malformed_Id_Is_Replaced_By_A_Generated_One(string[] values)
    {
        var context = ContextWith(values);

        await Create(_ => Task.CompletedTask).InvokeAsync(context);

        var id = context.GetCorrelationId();
        Assert.NotNull(id);
        Assert.Equal(32, id.Length);
        Assert.True(Guid.TryParseExact(id, "N", out _));
    }

    [Fact]
    public async Task The_Id_Is_In_Items_The_Request_Header_And_The_Response_Header()
    {
        var context = ContextWith("req-1");

        await Create(_ => Task.CompletedTask).InvokeAsync(context);
        await StartResponseAsync(context);

        Assert.Equal("req-1", context.Items[CorrelationIdMiddleware.ItemKey]);
        Assert.Equal("req-1", context.Request.Headers[Header].Single());
        Assert.Equal("req-1", context.Response.Headers[Header].Single());
    }

    [Fact]
    public async Task A_Malformed_Request_Header_Is_Rewritten_So_It_Is_Not_Forwarded()
    {
        var context = ContextWith("bad value");

        await Create(_ => Task.CompletedTask).InvokeAsync(context);

        var forwarded = context.Request.Headers[Header].Single();
        Assert.NotEqual("bad value", forwarded);
        Assert.Equal(context.GetCorrelationId(), forwarded);
    }

    [Fact]
    public async Task The_Response_Header_Holds_One_Value_Even_If_The_Downstream_Already_Set_It()
    {
        var context = ContextWith("req-1");

        await Create(ctx =>
        {
            ctx.Response.Headers.Append(Header, "from-the-service");
            return Task.CompletedTask;
        }).InvokeAsync(context);
        await StartResponseAsync(context);

        Assert.Equal("req-1", context.Response.Headers[Header].Single());
    }

    [Fact]
    public async Task An_Unhandled_Exception_Behind_The_Exception_Middleware_Still_Yields_A_500_With_The_Id()
    {
        var context = ContextWith("req-1");
        context.Response.Body = new MemoryStream();
        var exception = new ExceptionMiddleware(_ => throw new InvalidOperationException("boom"), NullLogger<ExceptionMiddleware>.Instance);

        await Create(exception.InvokeAsync).InvokeAsync(context);
        await StartResponseAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("req-1", context.Response.Headers[Header].Single());
    }

    [Fact]
    public async Task The_Logger_Scope_Carries_The_Id_While_Next_Runs()
    {
        var logger = new RecordingLogger();
        var context = ContextWith("req-1");
        List<object?>? seenDuringNext = null;

        await Create(_ =>
        {
            seenDuringNext = [.. logger.Scopes];
            return Task.CompletedTask;
        }, logger).InvokeAsync(context);

        var scope = Assert.IsAssignableFrom<IDictionary<string, object>>(Assert.Single(seenDuringNext!));
        Assert.Equal("req-1", scope["CorrelationId"]);
    }

    [Fact]
    public async Task The_Trace_Identifier_Is_Left_Alone()
    {
        var context = ContextWith("req-1");
        context.TraceIdentifier = "kestrel-id";

        await Create(_ => Task.CompletedTask).InvokeAsync(context);

        Assert.Equal("kestrel-id", context.TraceIdentifier);
    }

    [Fact]
    public void GetCorrelationId_Is_Null_When_The_Middleware_Did_Not_Run()
    {
        Assert.Null(new DefaultHttpContext().GetCorrelationId());
    }
}
