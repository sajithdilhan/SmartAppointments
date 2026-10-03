using System.Net;
using System.Net.Http.Json;
using Booking.Infrastructure.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Booking.Tests;

public class AvailabilityClientTests
{
    private static readonly Guid SlotId = Guid.CreateVersion7();
    private static readonly Guid AppointmentId = Guid.CreateVersion7();

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
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

    [Fact]
    public async Task GetSlot_Ok_Returns_The_Slot()
    {
        var start = new DateTime(2030, 1, 7, 3, 30, 0, DateTimeKind.Utc);
        var body = new { id = SlotId, branchId = Guid.CreateVersion7(), serviceTypeId = Guid.CreateVersion7(), startUtc = start, endUtc = start.AddMinutes(30), capacity = 2, reservedCount = 1, availableCapacity = 1 };
        var (client, handler) = Create(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });

        var result = await client.GetSlotAsync(SlotId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SlotId, result.Value!.Id);
        Assert.Equal(start, result.Value.StartUtc);
        Assert.Equal(1, result.Value.AvailableCapacity);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal($"/internal/slots/{SlotId}", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetSlot_NotFound_Maps_To_404()
    {
        var (client, _) = Create(HttpStatusCode.NotFound);

        var result = await client.GetSlotAsync(SlotId, CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
    }

    [Fact]
    public async Task Reserve_NoContent_Succeeds_And_Posts_The_Appointment_Id()
    {
        var (client, handler) = Create(HttpStatusCode.NoContent);

        var result = await client.ReserveAsync(SlotId, AppointmentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal($"/internal/slots/{SlotId}/reserve", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains(AppointmentId.ToString(), handler.Bodies[0]);
        Assert.Contains("appointmentId", handler.Bodies[0]);
    }

    [Fact]
    public async Task Reserve_Conflict_Carries_Availabilitys_Reason()
    {
        var (client, _) = Create(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new { status = 409, details = "Slot is full." })
        });

        var result = await client.ReserveAsync(SlotId, AppointmentId, CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
        Assert.Equal("Slot is full.", result.Error.Details);
    }

    [Fact]
    public async Task Reserve_Conflict_With_An_Unreadable_Body_Is_Still_409()
    {
        var (client, _) = Create(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("nope") });

        var result = await client.ReserveAsync(SlotId, AppointmentId, CancellationToken.None);

        Assert.Equal(409, result.Error!.Status);
    }

    [Fact]
    public async Task Reserve_NotFound_Maps_To_404()
    {
        var (client, _) = Create(HttpStatusCode.NotFound);

        var result = await client.ReserveAsync(SlotId, AppointmentId, CancellationToken.None);

        Assert.Equal(404, result.Error!.Status);
    }

    [Fact]
    public async Task Release_NoContent_Succeeds_And_NotFound_Maps_To_404()
    {
        var (ok, handler) = Create(HttpStatusCode.NoContent);
        Assert.True((await ok.ReleaseAsync(SlotId, AppointmentId, CancellationToken.None)).IsSuccess);
        Assert.Equal($"/internal/slots/{SlotId}/release", handler.Requests[0].RequestUri!.AbsolutePath);

        var (missing, _) = Create(HttpStatusCode.NotFound);
        Assert.Equal(404, (await missing.ReleaseAsync(SlotId, AppointmentId, CancellationToken.None)).Error!.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Rejected_Key_And_Server_Errors_Map_To_503_On_Every_Call(HttpStatusCode status)
    {
        var (client, _) = Create(status);

        Assert.Equal(503, (await client.GetSlotAsync(SlotId, CancellationToken.None)).Error!.Status);
        Assert.Equal(503, (await client.ReserveAsync(SlotId, AppointmentId, CancellationToken.None)).Error!.Status);
        Assert.Equal(503, (await client.ReleaseAsync(SlotId, AppointmentId, CancellationToken.None)).Error!.Status);
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

        Assert.Equal(503, (await client.GetSlotAsync(SlotId, CancellationToken.None)).Error!.Status);
        Assert.Equal(503, (await client.ReserveAsync(SlotId, AppointmentId, CancellationToken.None)).Error!.Status);
        Assert.Equal(503, (await client.ReleaseAsync(SlotId, AppointmentId, CancellationToken.None)).Error!.Status);
    }

    [Fact]
    public async Task A_Cancelled_Caller_Is_Not_Reported_As_An_Outage()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (client, _) = Create(_ => throw new TaskCanceledException());

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetSlotAsync(SlotId, cts.Token));
    }

    [Fact]
    public async Task The_Registered_Client_Sends_The_Api_Key_Header()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Availability:BaseUrl"] = "http://availability.test",
            ["InternalApi:Key"] = "a-very-long-internal-api-key-for-tests-0123456789"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAvailabilityClient(configuration);
        string? sentKey = null;
        services.AddHttpClient<Booking.Application.Abstractions.IAvailabilityClient, AvailabilityClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHandler(request =>
            {
                sentKey = request.Headers.GetValues("X-API-Key").Single();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<Booking.Application.Abstractions.IAvailabilityClient>();
        var result = await client.ReleaseAsync(SlotId, AppointmentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("a-very-long-internal-api-key-for-tests-0123456789", sentKey);
    }

    [Fact]
    public async Task The_Registered_Client_Forwards_The_Correlation_Id_Of_The_Current_Request()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Availability:BaseUrl"] = "http://availability.test",
            ["InternalApi:Key"] = "a-very-long-internal-api-key-for-tests-0123456789"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAvailabilityClient(configuration);
        string? sentId = null;
        services.AddHttpClient<Booking.Application.Abstractions.IAvailabilityClient, AvailabilityClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHandler(request =>
            {
                sentId = request.Headers.GetValues("X-Correlation-ID").Single();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }));
        using var provider = services.BuildServiceProvider();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Items[SmartAppointments.BuildingBlocks.Web.Middlewares.CorrelationIdMiddleware.ItemKey] = "req-42";
        provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext = httpContext;

        var client = provider.GetRequiredService<Booking.Application.Abstractions.IAvailabilityClient>();
        var result = await client.ReleaseAsync(SlotId, AppointmentId, CancellationToken.None);

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
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Availability:BaseUrl"] = baseUrl,
            ["InternalApi:Key"] = key
        }).Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAvailabilityClient(configuration));
    }
}
