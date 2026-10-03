using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartAppointments.Gateway.Health;

namespace Gateway.Tests;

public class HealthEndpointTests
{
    private static HealthCheckService Build(Func<HttpRequestMessage, HttpStatusCode> status)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:Clusters:auth:Destinations:primary:Address"] = "http://localhost:5008",
            ["ReverseProxy:Clusters:availability:Destinations:primary:Address"] = "http://localhost:5202/",
            ["ReverseProxy:Clusters:booking:Destinations:primary:Address"] = "http://localhost:5310",
        }).Build();

        var handler = new DownstreamHealthCheckTests.FakeHandler((r, _) => Task.FromResult(new HttpResponseMessage(status(r))));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGatewayHealthChecks(configuration);
        // Swap the named client's primary handler for the fake.
        services.AddHttpClient(DownstreamHealthCheck.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<HealthCheckService>();
    }

    [Fact]
    public async Task All_Up_Is_Healthy_With_Four_Entries()
    {
        var report = await Build(_ => HttpStatusCode.OK).CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Equal(["auth", "availability", "booking", "gateway"], report.Entries.Keys.Order().ToArray());
    }

    [Fact]
    public async Task One_Down_Is_Degraded_And_Names_It()
    {
        var report = await Build(r => r.RequestUri!.Port == 5202 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK).CheckHealthAsync();

        Assert.Equal(HealthStatus.Degraded, report.Status);
        Assert.Equal(HealthStatus.Degraded, report.Entries["availability"].Status);
        Assert.Equal(HealthStatus.Healthy, report.Entries["gateway"].Status);
    }

    [Fact]
    public async Task The_Health_Urls_Come_From_The_Cluster_Addresses()
    {
        var urls = new List<string>();
        var report = await Build(r => { lock (urls) urls.Add(r.RequestUri!.ToString()); return HttpStatusCode.OK; }).CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Equal(
            ["http://localhost:5008/healthz", "http://localhost:5202/healthz", "http://localhost:5310/healthz"],
            urls.Order().ToArray());
    }

    [Fact]
    public void Status_Codes_Map_Healthy_And_Degraded_To_200_And_Unhealthy_To_503()
    {
        var map = HealthExtensions.GatewayHealthOptions().ResultStatusCodes;

        Assert.Equal(200, map[HealthStatus.Healthy]);
        Assert.Equal(200, map[HealthStatus.Degraded]);
        Assert.Equal(503, map[HealthStatus.Unhealthy]);
    }

    [Fact]
    public async Task The_Writer_Lists_Each_Check_With_Statuses_As_Strings()
    {
        var report = await Build(r => r.RequestUri!.Port == 5310 ? HttpStatusCode.BadGateway : HttpStatusCode.OK).CheckHealthAsync();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponseWriter.WriteAsync(context, report);
        context.Response.Body.Position = 0;
        using var json = JsonDocument.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync());

        Assert.Equal("application/json", context.Response.ContentType);
        Assert.Equal("Degraded", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.TryGetProperty("totalDurationMs", out _));
        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!);
        Assert.Equal(4, checks.Count);
        Assert.Equal("Healthy", checks["auth"].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, checks["auth"].GetProperty("description").ValueKind);
        Assert.Equal("Degraded", checks["booking"].GetProperty("status").GetString());
        Assert.Equal("HTTP 502", checks["booking"].GetProperty("description").GetString());
    }
}
