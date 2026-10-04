using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SmartAppointments.Gateway.Cors;

namespace Gateway.Tests;

public class GatewayCorsPolicyTests
{
    private static readonly string[] Origins = ["http://localhost:4200", "http://localhost:8081"];

    private static async Task<CorsPolicy> Policy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGatewayCors(Origins);
        var provider = services.BuildServiceProvider().GetRequiredService<ICorsPolicyProvider>();

        return (await provider.GetPolicyAsync(new DefaultHttpContext(), GatewayCors.PolicyName))!;
    }

    [Fact]
    public async Task Only_The_Four_Methods_Are_Allowed()
    {
        var policy = await Policy();

        Assert.Equal(["GET", "POST", "PUT", "DELETE"], policy.Methods);
    }

    [Fact]
    public async Task Only_The_Four_Request_Headers_Are_Allowed()
    {
        var policy = await Policy();

        Assert.Equal(["Authorization", "Content-Type", "Idempotency-Key", "X-Correlation-ID"], policy.Headers);
    }

    [Fact]
    public async Task The_Three_Response_Headers_Are_Exposed()
    {
        var policy = await Policy();

        Assert.Equal(["X-Correlation-ID", "Retry-After", "Location"], policy.ExposedHeaders);
    }

    [Fact]
    public async Task The_Preflight_Is_Cached_For_Ten_Minutes()
    {
        var policy = await Policy();

        Assert.Equal(TimeSpan.FromMinutes(10), policy.PreflightMaxAge);
    }

    [Fact]
    public async Task Nothing_Is_Allowed_By_Wildcard_And_No_Credentials_Are_Sent()
    {
        var policy = await Policy();

        Assert.False(policy.SupportsCredentials);
        Assert.False(policy.AllowAnyOrigin);
        Assert.False(policy.AllowAnyMethod);
        Assert.False(policy.AllowAnyHeader);
    }

    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("http://localhost:8081")]
    public async Task A_Configured_Origin_Is_Allowed(string origin)
    {
        var policy = await Policy();

        Assert.True(policy.IsOriginAllowed(origin));
    }

    [Theory]
    [InlineData("https://localhost:4200")]
    [InlineData("http://localhost:4201")]
    [InlineData("http://localhost:4200.evil.example")]
    [InlineData("HTTP://LOCALHOST:4200")]
    [InlineData("null")]
    [InlineData("*")]
    [InlineData("")]
    public async Task Any_Other_Origin_Is_Not_Allowed(string origin)
    {
        var policy = await Policy();

        Assert.False(policy.IsOriginAllowed(origin));
    }
}
