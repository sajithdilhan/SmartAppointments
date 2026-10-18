using System.Net;
using System.Net.Http.Json;
using Booking.Application.Abstractions;
using Booking.Infrastructure.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Booking.Tests;

public class AuthClientTests
{
    private static readonly Guid CustomerId = Guid.CreateVersion7();

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

    private static (AuthClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://auth.test/") };
        return (new AuthClient(http, NullLogger<AuthClient>.Instance), handler);
    }

    private static (AuthClient Client, FakeHandler Handler) Create(HttpStatusCode status) =>
        Create(_ => Task.FromResult(new HttpResponseMessage(status)));

    [Fact]
    public async Task Ok_Maps_The_Items_By_Id_And_Posts_The_Ids()
    {
        var other = Guid.CreateVersion7();
        var (client, handler) = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new { id = CustomerId, firstName = "Ada", lastName = "Lovelace", phoneNumber = "0412" }
            })
        }));

        var result = await client.LookupAsync([CustomerId, other], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new CustomerInfo("Ada", "Lovelace", "0412"), result.Value![CustomerId]);
        Assert.False(result.Value.ContainsKey(other));
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/internal/users/lookup", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("\"ids\"", handler.Bodies[0]);
        Assert.Contains(CustomerId.ToString(), handler.Bodies[0]);
        Assert.Contains(other.ToString(), handler.Bodies[0]);
    }

    [Fact]
    public async Task An_Empty_Array_Is_A_Success_With_No_Customers()
    {
        var (client, _) = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Array.Empty<object>())
        }));

        var result = await client.LookupAsync([CustomerId], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task An_Unreadable_Body_Is_503()
    {
        var (client, _) = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("nope", System.Text.Encoding.UTF8, "application/json")
        }));

        Assert.Equal(503, (await client.LookupAsync([CustomerId], CancellationToken.None)).Error!.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Any_Non_Ok_Answer_Maps_To_503(HttpStatusCode status)
    {
        var (client, _) = Create(status);

        Assert.Equal(503, (await client.LookupAsync([CustomerId], CancellationToken.None)).Error!.Status);
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

        Assert.Equal(503, (await client.LookupAsync([CustomerId], CancellationToken.None)).Error!.Status);
    }

    [Fact]
    public async Task A_Cancelled_Caller_Is_Not_Reported_As_An_Outage()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (client, _) = Create(_ => throw new TaskCanceledException());

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.LookupAsync([CustomerId], cts.Token));
    }

    private static ServiceProvider Register(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Auth:BaseUrl"] = "http://auth.test",
            ["InternalApi:Key"] = "a-very-long-internal-api-key-for-tests-0123456789"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthClient(configuration);
        services.AddHttpClient<IAuthClient, AuthClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHandler(respond));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_Registered_Client_Sends_The_Api_Key_Header()
    {
        string? sentKey = null;
        using var provider = Register(request =>
        {
            sentKey = request.Headers.GetValues("X-API-Key").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) });
        });

        var result = await provider.GetRequiredService<IAuthClient>().LookupAsync([CustomerId], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("a-very-long-internal-api-key-for-tests-0123456789", sentKey);
    }

    [Fact]
    public async Task The_Registered_Client_Forwards_The_Correlation_Id_Of_The_Current_Request()
    {
        string? sentId = null;
        using var provider = Register(request =>
        {
            sentId = request.Headers.GetValues("X-Correlation-ID").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) });
        });
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Items[SmartAppointments.BuildingBlocks.Web.Middlewares.CorrelationIdMiddleware.ItemKey] = "req-7";
        provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext = httpContext;

        var result = await provider.GetRequiredService<IAuthClient>().LookupAsync([CustomerId], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("req-7", sentId);
    }

    [Theory]
    [InlineData(null, "key")]
    [InlineData("", "key")]
    [InlineData("not a url", "key")]
    [InlineData("http://auth.test", null)]
    [InlineData("http://auth.test", "  ")]
    public void Missing_Or_Invalid_Settings_Stop_Startup(string? baseUrl, string? key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:Auth:BaseUrl"] = baseUrl,
            ["InternalApi:Key"] = key
        }).Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAuthClient(configuration));
    }
}
