using System.Net;
using System.Net.Http.Json;
using Auth.Application.Abstractions;
using Auth.Infrastructure.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Auth.Tests;

public class AvailabilityClientTests
{
    private const string Key = "a-very-long-internal-api-key-for-tests-0123456789";

    private static readonly Guid BranchId = Guid.CreateVersion7();

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await respond(request);
        }
    }

    private static (AvailabilityClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://availability.test/") };
        return (new AvailabilityClient(http, NullLogger<AvailabilityClient>.Instance), handler);
    }

    private static (AvailabilityClient Client, FakeHandler Handler) Create(HttpResponseMessage response) =>
        Create(_ => Task.FromResult(response));

    private static (AvailabilityClient Client, FakeHandler Handler) Create(HttpStatusCode status) =>
        Create(_ => Task.FromResult(new HttpResponseMessage(status)));

    private static IConfiguration BuildConfiguration(string? baseUrl, string? key) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Availability:BaseUrl"] = baseUrl,
            ["InternalApi:Key"] = key
        }).Build();

    [Fact]
    public async Task GetBranch_Ok_Returns_The_Branch_From_The_Right_Path()
    {
        var (client, handler) = Create(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id = BranchId, isActive = true, timeZoneId = "Pacific/Auckland" })
        });

        var result = await client.GetBranchAsync(BranchId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new BranchInfo(BranchId, true, "Pacific/Auckland"), result.Value);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal($"/internal/branches/{BranchId}", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetBranch_Ok_With_An_Inactive_Branch_And_No_Zone()
    {
        var (client, _) = Create(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id = BranchId, isActive = false, timeZoneId = (string?)null })
        });

        var result = await client.GetBranchAsync(BranchId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
        Assert.Null(result.Value.TimeZoneId);
    }

    [Fact]
    public async Task GetBranch_NotFound_Is_Success_With_Null()
    {
        var (client, _) = Create(HttpStatusCode.NotFound);

        var result = await client.GetBranchAsync(BranchId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetBranch_Unreadable_Body_Maps_To_503()
    {
        var (client, _) = Create(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("nope") });

        var result = await client.GetBranchAsync(BranchId, CancellationToken.None);

        Assert.Equal(503, result.Error!.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Rejected_Key_And_Server_Errors_Map_To_503(HttpStatusCode status)
    {
        var (client, _) = Create(status);

        var result = await client.GetBranchAsync(BranchId, CancellationToken.None);

        Assert.Equal(503, result.Error!.Status);
        Assert.Equal(AvailabilityClient.UnavailableMessage, result.Error.Details);
    }

    public static TheoryData<Exception> Outages => new()
    {
        new HttpRequestException("connection refused"),
        new TimeoutRejectedException("timed out"),
        new BrokenCircuitException("open"),
        new TaskCanceledException("HttpClient timeout")
    };

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task Network_Timeout_And_Open_Circuit_Map_To_503(Exception failure)
    {
        var (client, _) = Create(_ => throw failure);

        Assert.Equal(503, (await client.GetBranchAsync(BranchId, CancellationToken.None)).Error!.Status);
    }

    [Fact]
    public async Task A_Cancelled_Caller_Is_Not_Reported_As_An_Outage()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (client, _) = Create(_ => throw new TaskCanceledException());

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetBranchAsync(BranchId, cts.Token));
    }

    [Fact]
    public async Task The_Registered_Client_Sends_The_Api_Key_Header()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAvailabilityClient(BuildConfiguration("http://availability.test", Key));
        string? sentKey = null;
        services.AddHttpClient<IAvailabilityClient, AvailabilityClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHandler(request =>
            {
                sentKey = request.Headers.GetValues("X-API-Key").Single();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }));
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IAvailabilityClient>().GetBranchAsync(BranchId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Key, sentKey);
    }

    [Fact]
    public async Task The_Registered_Client_Forwards_The_Correlation_Id_Of_The_Current_Request()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAvailabilityClient(BuildConfiguration("http://availability.test", Key));
        string? sentId = null;
        services.AddHttpClient<IAvailabilityClient, AvailabilityClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHandler(request =>
            {
                sentId = request.Headers.GetValues("X-Correlation-ID").Single();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }));
        using var provider = services.BuildServiceProvider();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Items[SmartAppointments.BuildingBlocks.Web.Middlewares.CorrelationIdMiddleware.ItemKey] = "req-42";
        provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext = httpContext;

        var result = await provider.GetRequiredService<IAvailabilityClient>().GetBranchAsync(BranchId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("req-42", sentId);
    }

    [Theory]
    [InlineData(null, "key")]
    [InlineData("", "key")]
    [InlineData("not a url", "key")]
    [InlineData("http://availability.test", null)]
    [InlineData("http://availability.test", "  ")]
    public void Missing_Or_Invalid_Settings_Stop_Startup(string? baseUrl, string? key)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAvailabilityClient(BuildConfiguration(baseUrl, key)));
    }
}
