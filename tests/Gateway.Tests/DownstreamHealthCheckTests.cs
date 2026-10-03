using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using SmartAppointments.Gateway.Health;

namespace Gateway.Tests;

public class DownstreamHealthCheckTests
{
    internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request);
            return respond(request, cancellationToken);
        }
    }

    private static IHttpClientFactory Factory(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(DownstreamHealthCheck.ClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false) { Timeout = DownstreamHealthCheck.Timeout });
        return factory.Object;
    }

    private static readonly Uri Url = new("http://auth.internal:5008/healthz");

    private static HealthCheckContext Context() => new()
    {
        Registration = new HealthCheckRegistration("auth", _ => Mock.Of<IHealthCheck>(), HealthStatus.Degraded, null)
    };

    private static Task<HealthCheckResult> RunAsync(FakeHandler handler)
        => new DownstreamHealthCheck(Url, Factory(handler)).CheckHealthAsync(Context());

    private static FakeHandler Returns(HttpStatusCode code)
        => new((_, _) => Task.FromResult(new HttpResponseMessage(code)));

    private static FakeHandler Hangs() => new(async (_, ct) =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        return new HttpResponseMessage();
    });

    [Fact]
    public async Task A_200_Is_Healthy()
    {
        var result = await RunAsync(Returns(HttpStatusCode.OK));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "HTTP 503")]
    [InlineData(HttpStatusCode.NotFound, "HTTP 404")]
    public async Task A_Non_2xx_Is_Degraded_With_The_Status_Code(HttpStatusCode code, string description)
    {
        var result = await RunAsync(Returns(code));

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(description, result.Description);
    }

    [Fact]
    public async Task An_Unreachable_Service_Is_Degraded_Without_The_Exception_Message_Or_Host()
    {
        var result = await RunAsync(new FakeHandler((_, _) => throw new HttpRequestException("No such host is known: auth.internal")));

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("unreachable", result.Description);
    }

    [Fact]
    public async Task A_Handler_That_Never_Returns_Times_Out_In_About_Two_Seconds()
    {
        var started = DateTime.UtcNow;

        var result = await RunAsync(Hangs());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("timed out", result.Description);
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Calls_The_Healthz_Url_Without_An_Authorization_Header()
    {
        var handler = Returns(HttpStatusCode.OK);

        await RunAsync(handler);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(Url, request.RequestUri);
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task A_Cancelled_Caller_Is_Not_Reported_As_A_Timeout()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DownstreamHealthCheck(Url, Factory(Hangs())).CheckHealthAsync(Context(), cts.Token));
    }
}
