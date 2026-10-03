using Microsoft.AspNetCore.Authorization;
using SmartAppointments.BuildingBlocks.Web.Authentication;
using SmartAppointments.BuildingBlocks.Web.Middlewares;
using SmartAppointments.Gateway.Configuration;
using SmartAppointments.Gateway.Docs;
using SmartAppointments.Gateway.Health;
using SmartAppointments.Gateway.RateLimiting;
using SmartAppointments.Gateway.Proxy;

var builder = WebApplication.CreateBuilder(args);

// A blank or malformed downstream address stops startup rather than proxying nowhere.
ReverseProxyValidator.Validate(builder.Configuration);

builder.Services.AddJwtAuthentication(builder.Configuration);
// The gateway makes no role decisions (AddAuthorizationWithRoles is deliberately not called); roles
// stay with the services. The fallback makes a route that forgets its policy safe: it needs a user.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddGatewayRateLimiting();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddGatewayHealthChecks(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<LoggingMiddleware>();

// TLS terminates here; the services behind the gateway speak plain http.
app.UseHttpsRedirection();
app.UseMiddleware<UnmatchedRequestMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
// After authentication, so the validated `sub` is available to the per-user partitions.
app.UseRateLimiter();

app.MapHealthChecks("/healthz", HealthExtensions.GatewayHealthOptions()).AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapGatewayDocs();
}

app.MapReverseProxy(proxy => proxy.UseMiddleware<ProxyErrorMiddleware>());

app.Run();
