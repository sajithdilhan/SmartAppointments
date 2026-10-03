using Microsoft.AspNetCore.Http;
using Moq;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Web.Http;
using SmartAppointments.BuildingBlocks.Web.Middlewares;

namespace BuildingBlocks.Tests;

public class CorrelationIdHandlerTests
{
    private const string Header = Constants.CorrelationIdHeaderName;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    // Sends the same message twice, as a retrying handler would.
    private sealed class RetryingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await base.SendAsync(request, cancellationToken);
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private static HttpContext ContextWithId(string? id)
    {
        var context = new DefaultHttpContext();
        if (id is not null)
        {
            context.Items[CorrelationIdMiddleware.ItemKey] = id;
        }

        return context;
    }

    private static (HttpMessageInvoker Invoker, RecordingHandler Recorder) Create(HttpContext? context, DelegatingHandler? inner = null)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(context);
        var recorder = new RecordingHandler();
        var handler = new CorrelationIdHandler(accessor.Object);
        if (inner is null)
        {
            handler.InnerHandler = recorder;
        }
        else
        {
            inner.InnerHandler = recorder;
            handler.InnerHandler = inner;
        }

        return (new HttpMessageInvoker(handler), recorder);
    }

    [Fact]
    public async Task Adds_The_Id_Of_The_Current_Request()
    {
        var (invoker, recorder) = Create(ContextWithId("req-1"));

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://x.test/"), CancellationToken.None);

        Assert.Equal("req-1", recorder.Requests.Single().Headers.GetValues(Header).Single());
    }

    [Fact]
    public async Task Adds_Nothing_Without_An_Http_Context()
    {
        var (invoker, recorder) = Create(null);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://x.test/"), CancellationToken.None);

        Assert.False(recorder.Requests.Single().Headers.Contains(Header));
    }

    [Fact]
    public async Task Adds_Nothing_When_The_Request_Has_No_Id()
    {
        var (invoker, recorder) = Create(ContextWithId(null));

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://x.test/"), CancellationToken.None);

        Assert.False(recorder.Requests.Single().Headers.Contains(Header));
    }

    [Fact]
    public async Task Does_Not_Overwrite_An_Existing_Header()
    {
        var (invoker, recorder) = Create(ContextWithId("req-1"));
        var request = new HttpRequestMessage(HttpMethod.Get, "http://x.test/");
        request.Headers.Add(Header, "explicit");

        await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal("explicit", recorder.Requests.Single().Headers.GetValues(Header).Single());
    }

    [Fact]
    public async Task Every_Attempt_Of_A_Retried_Request_Carries_The_Same_Single_Id()
    {
        // Outside the retrying handler, so the message is stamped once before any attempt.
        var (invoker, recorder) = Create(ContextWithId("req-1"), new RetryingHandler());

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://x.test/"), CancellationToken.None);

        Assert.Equal(2, recorder.Requests.Count);
        Assert.All(recorder.Requests, r => Assert.Equal("req-1", r.Headers.GetValues(Header).Single()));
    }
}
