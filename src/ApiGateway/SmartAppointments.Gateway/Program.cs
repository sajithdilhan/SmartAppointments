using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using SmartAppointments.BuildingBlocks.Web.Authentication;
using SmartAppointments.BuildingBlocks.Web.Middlewares;
using SmartAppointments.Gateway.Configuration;
using SmartAppointments.Gateway.Cors;
using SmartAppointments.Gateway.Docs;
using SmartAppointments.Gateway.Health;
using SmartAppointments.Gateway.RateLimiting;
using SmartAppointments.Gateway.Proxy;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// A blank or malformed downstream address stops startup rather than proxying nowhere.
ReverseProxyValidator.Validate(builder.Configuration);
// No origin configured means no CORS at all: the gateway behaves exactly as without it.
var corsOrigins = CorsOriginsValidator.Validate(builder.Configuration);

builder.Services.AddJwtAuthentication(builder.Configuration);
// The gateway makes no role decisions (AddAuthorizationWithRoles is deliberately not called); roles
// stay with the services. The fallback makes a route that forgets its policy safe: it needs a user.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddGatewayRateLimiting();
var reverseProxy = builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
if (corsOrigins.Count > 0)
{
    builder.Services.AddGatewayCors(corsOrigins);
    reverseProxy.AddTransforms(transforms => transforms.AddResponseTransform(context =>
    {
        DownstreamCorsHeaderTransform.Apply(
            context.HttpContext,
            context.ProxyResponse,
            context.HttpContext.RequestServices.GetRequiredService<ICorsService>(),
            context.HttpContext.RequestServices.GetRequiredService<CorsPolicy>());
        return ValueTask.CompletedTask;
    }));
}

builder.Services.AddGatewayHealthChecks(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<LoggingMiddleware>();
// Before everything that can answer (unmatched 404, authentication 401, rate limiter 429), so a preflight
// ends here with no token and no permit and every later error keeps the headers; after correlation id and
// exception handling, so a failure in CORS itself is still a problem body with a correlation id.
if (corsOrigins.Count > 0)
{
    app.UseGatewayCors();
}

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
