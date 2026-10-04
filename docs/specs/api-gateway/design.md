# API gateway — Design

> References like _(Req 3.2)_ point at [`requirements.md`](requirements.md).

## Overview

The decisions below were taken by the project owner and are recorded here, not reopened.

1. **YARP, configured in `appsettings.json`.** `Yarp.ReverseProxy` with routes and clusters in the gateway's `ReverseProxy` section. The destination addresses are blank there and filled in `appsettings.Development.json` (`http://localhost:5008`, `5202`, `5310`), so Docker Compose can override them with environment variables later. The gateway keeps ports 5290 and 7176. Public paths: `/api/auth/**`, `/api/branches/**`, `/api/services/**`, `/api/slots/**`, `/api/appointments/**`. `/internal/**` is never routed, nor are the services' `/healthz`, `/openapi`, `/scalar` _(Req 1)_.
2. **JWT validation at the edge** with `AddJwtAuthentication` from `BuildingBlocks.Web`, same issuer, audience and secret as Auth, fail-fast. `POST /api/auth/register` and `/login` are anonymous; every other routed path requires an authenticated user, as a YARP route `AuthorizationPolicy`. Role checks stay in the services. `Authorization` is forwarded unchanged, so the services validate again _(Req 2)_.
3. **In-memory sliding-window rate limiting** (`Microsoft.AspNetCore.RateLimiting`, 1 minute, 6 segments, no queue): login 5/min per client IP, create appointment 10/min per user, slot search 30/min per user; per-user partitions use the validated `sub`; rejections are `429` problem+json with `Retry-After`; no global limit; join-queue out of scope; single-instance limitation accepted _(Req 3)_.
4. **One correlation id, `X-Correlation-ID`, through every hop.** A shared `CorrelationIdMiddleware` in `BuildingBlocks.Web`, used by the gateway and all three services; Booking's Availability client forwards it with a `DelegatingHandler` _(Req 5)_.
5. **Aggregated health.** `/healthz` on the gateway is `Healthy` itself, one check per downstream calling its `/healthz`, a failing downstream is `Degraded` (HTTP 200), JSON body listing the checks, `Unhealthy` is `503` _(Req 8)_.
6. **The gateway uses the shared `ExceptionMiddleware` and `LoggingMiddleware`.** A downstream that cannot be reached yields `502` problem+json _(Req 4)_.
7. **A `tests/Gateway.Tests` project**, xUnit and Moq, unit tests only; the integration level is verified by hand in the last task _(Req 7)_.

8. **TLS terminates at the gateway.** The services stop redirecting to HTTPS; the gateway keeps `UseHttpsRedirection()` _(Req 9)_.
9. **Development-only aggregated docs** at the gateway: proxied OpenAPI documents and one Scalar UI _(Req 10)_.

Everything else follows the existing services: the shared middlewares, `ApiProblemDetails` for every error body the gateway writes itself, and a startup that fails with an explanatory message rather than running half-configured.

## Architecture

```
                    client
                      |
        http://localhost:5290 / https://localhost:7176
                      v
   +-----------------------------------------------------------+
   | CorrelationId -> Exception -> Logging -> HttpsRedirection |
   | -> Authentication -> Authorization -> RateLimiter         |
   |        |                                   |              |
   |   /healthz (anonymous)             YARP reverse proxy     |
   +--------+-----------------------------------+--------------+
            | parallel GET /healthz             |
   +--------+---------+------------------------+--------------+
   v                  v                        v
 Auth :5008     Availability :5202        Booking :5310
```

| Layer | Contents |
|---|---|
| `SmartAppointments.BuildingBlocks` | `Constants.CorrelationIdHeaderName` (`"X-Correlation-ID"`) |
| `SmartAppointments.BuildingBlocks.Web` | `Middlewares/CorrelationIdMiddleware`, `Middlewares/CorrelationIdExtensions` (`GetCorrelationId`), `Http/CorrelationIdHandler`, `Results/ProblemDetailsWriter` (one place that writes an `ApiProblemDetails` body; `ExceptionMiddleware` calls it too) |
| `Auth.Api`, `Availability.Api`, `Booking.Api` | `app.UseMiddleware<CorrelationIdMiddleware>()` first; `UseHttpsRedirection()` removed; `Logging:Console:IncludeScopes` in `appsettings.json` |
| `Booking.Infrastructure` | the Availability `HttpClient` gets `CorrelationIdHandler`; project reference to `BuildingBlocks.Web` (the first Infrastructure-to-Web dependency, accepted) |
| `SmartAppointments.Gateway` | `Program.cs`; `Configuration/ReverseProxyValidator`; `Proxy/ProxyErrorMiddleware`; `RateLimiting/RateLimitPolicies`, `RateLimitOptions`; `Health/DownstreamHealthCheck`, `HealthResponseWriter`; `Docs/` (Development-only Scalar mapping); `appsettings*.json`; `SmartAppointments.Gateway.http` |
| `tests/Gateway.Tests` | xUnit + Moq |

The gateway project references `BuildingBlocks.Web` (and through it `BuildingBlocks`) and the `Yarp.ReverseProxy` package (the latest stable release that supports `net10.0`). `Microsoft.AspNetCore.RateLimiting` and the health-check types are part of the ASP.NET Core shared framework, so they need no package.

## Components and interfaces

### Route and cluster configuration _(Req 1, 2, 3, 6)_

`appsettings.json`:

```json
{
  "Jwt": { "Issuer": "https://localhost:7220", "Audience": "https://localhost:7220", "SecretKey": "" },
  "ReverseProxy": {
    "Routes": {
      "auth-login":         { "ClusterId": "auth",         "Order": 0,  "AuthorizationPolicy": "anonymous", "RateLimiterPolicy": "login",
                              "Match": { "Path": "/api/auth/login", "Methods": [ "POST" ] } },
      "auth-register":      { "ClusterId": "auth",         "Order": 0,  "AuthorizationPolicy": "anonymous",
                              "Match": { "Path": "/api/auth/register", "Methods": [ "POST" ] } },
      "auth":               { "ClusterId": "auth",         "Order": 10, "AuthorizationPolicy": "default",
                              "Match": { "Path": "/api/auth/{**catch-all}" } },
      "branches":           { "ClusterId": "availability", "Order": 10, "AuthorizationPolicy": "default",
                              "Match": { "Path": "/api/branches/{**catch-all}" } },
      "services":           { "ClusterId": "availability", "Order": 10, "AuthorizationPolicy": "default",
                              "Match": { "Path": "/api/services/{**catch-all}" } },
      "slots-search":       { "ClusterId": "availability", "Order": 0,  "AuthorizationPolicy": "default", "RateLimiterPolicy": "slot-search",
                              "Match": { "Path": "/api/slots/available", "Methods": [ "GET" ] } },
      "slots":              { "ClusterId": "availability", "Order": 10, "AuthorizationPolicy": "default",
                              "Match": { "Path": "/api/slots/{**catch-all}" } },
      "appointments-create":{ "ClusterId": "booking",      "Order": 0,  "AuthorizationPolicy": "default", "RateLimiterPolicy": "appointment-create",
                              "Match": { "Path": "/api/appointments", "Methods": [ "POST" ] } },
      "appointments":       { "ClusterId": "booking",      "Order": 10, "AuthorizationPolicy": "default",
                              "Match": { "Path": "/api/appointments/{**catch-all}" } }
    },
    "Clusters": {
      "auth":         { "HttpRequest": { "ActivityTimeout": "00:00:30" }, "Destinations": { "primary": { "Address": "" } } },
      "availability": { "HttpRequest": { "ActivityTimeout": "00:00:30" }, "Destinations": { "primary": { "Address": "" } } },
      "booking":      { "HttpRequest": { "ActivityTimeout": "00:00:30" }, "Destinations": { "primary": { "Address": "" } } }
    }
  }
}
```

`appsettings.Development.json` carries `Jwt:SecretKey` (Auth's development key, copied from `Auth.Api/appsettings.Development.json`) and the three `Destinations:primary:Address` values. The `Jwt` issuer and audience are Auth's (`https://localhost:7220`), as in Booking's `appsettings.json`.

Points that matter:

- **Specific before general.** The nine routes have two `Order` values: `0` for the exact-path, single-method routes and `10` for the prefix routes. YARP orders by `Order` first, so the choice does not depend on how ASP.NET Core resolves template precedence _(Req 1.5)_. A `GET /api/appointments` (not a route of the exact `POST` one) falls through to the catch-all `appointments` route, because an endpoint whose method constraint fails is not a candidate.
- **No `/internal` and no root route.** Nothing in the table can match `/internal/...`, `/healthz`, `/openapi/...` or `/scalar/...` **in `appsettings.json`**; the unmatched request gets the framework's `404` _(Req 1.3, 1.4)_. The Development-only documentation routes are added by `appsettings.Development.json` (see below).
- **`anonymous` and `default`** are YARP's built-in policy names (`anonymous` skips authorization; `default` is the ASP.NET Core default policy, "an authenticated user"). Each route names one explicitly; the fallback policy (below) makes an omitted name safe too _(Req 2.6)_.
- **Rate-limit policy names** are `login`, `appointment-create`, `slot-search`. YARP refuses to load a route naming a policy that is not registered, so the names are added to the JSON in the same task that registers the policies (task 5 omits them, task 8 adds them).
- **Every route carries `"Transforms": [ { "RequestHeaderOriginalHost": "true" } ]`**, not only the documentation routes. Without it YARP sends the destination host (`localhost:5310`) and a service building an absolute URL from `Host` (`CreatedAtAction`'s `Location` in Booking, Availability and Auth) leaks the downstream address to the client. With it the services see the gateway's host and `Location` names the gateway. Found in the end-to-end run; `RoutingConfigurationTests` asserts it for every route.
- **Empty addresses in `appsettings.json`.** A blank address fails `ReverseProxyValidator` at startup _(Req 6.3)_, so a misconfigured environment never silently proxies nowhere. The environment form is `ReverseProxy__Clusters__auth__Destinations__primary__Address`.
- **30 s `ActivityTimeout`** is longer than Booking's own 20 s budget for its call to Availability (booking-appointments design, decision 5), so a slow create answers with Booking's `503` rather than the gateway's `504`.

### `ReverseProxyValidator` _(Req 6.3)_

A static `Validate(IConfiguration)` called in `Program.cs` before `AddReverseProxy`. It reads `ReverseProxy:Clusters:{auth|availability|booking}:Destinations`, requires at least one destination and, for each, an absolute `http`/`https` URI (`Uri.TryCreate(..., UriKind.Absolute)` and scheme check), and throws `InvalidOperationException` naming the key and its `__` form. It does not validate routes: YARP does that when it loads them.

### `Program.cs` _(Req 2, 3, 4, 6, 8)_

```csharp
var builder = WebApplication.CreateBuilder(args);

ReverseProxyValidator.Validate(builder.Configuration);

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddGatewayRateLimiting();                 // chunk C
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddGatewayHealthChecks(builder.Configuration);   // chunk D

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<LoggingMiddleware>();
app.UseHttpsRedirection();                                 // the gateway only: TLS ends here
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();                                      // chunk C

app.MapHealthChecks("/healthz", gatewayHealthOptions).AllowAnonymous();   // chunk D
if (app.Environment.IsDevelopment())
    app.MapGatewayDocs();                                   // /scalar, Development only

app.MapReverseProxy(proxy => proxy.UseMiddleware<ProxyErrorMiddleware>());

app.Run();
```

`AddAuthorizationWithRoles()` is deliberately not called: the gateway makes no role decisions, and registering `AdminPolicy` and friends would suggest it does. `AddAuthorizationBuilder()` with the fallback policy is the only authorization registration. Because the fallback applies to every endpoint without its own policy, `/healthz` needs `.AllowAnonymous()` _(Req 2.7)_. The request that matches no endpoint never meets the fallback, so unknown paths, including `/internal/...`, get `404`, not `401`.

### Correlation id _(Req 5)_

```csharp
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string ItemKey = "SmartAppointments.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = Resolve(context.Request.Headers[Constants.CorrelationIdHeaderName]);

        context.Items[ItemKey] = id;
        context.Request.Headers[Constants.CorrelationIdHeaderName] = id;     // what YARP copies downstream
        context.Response.OnStarting(static state =>
        {
            var (ctx, value) = ((HttpContext, string))state;
            ctx.Response.Headers[Constants.CorrelationIdHeaderName] = value; // overwrite, never append
            return Task.CompletedTask;
        }, (context, id));

        using (logger.BeginScope("CorrelationId:{CorrelationId}", id))
            await next(context);
    }

    internal static string Resolve(StringValues header) => /* one value, 1..64 chars of [A-Za-z0-9._-], else Guid.NewGuid().ToString("N") */;
}

public static string? GetCorrelationId(this HttpContext context) => context.Items[CorrelationIdMiddleware.ItemKey] as string;
```

Decisions inside it:

- **The id is validated by a hand-written character loop**, not a `Regex`: it runs on every request and the rule is trivial. Anything else (empty, more than one value, over 64 characters, a character outside the set) is replaced, not rejected _(Req 5.2, 5.3)_.
- **The request header is overwritten with the decided value.** YARP builds the downstream request from `HttpContext.Request.Headers`, so what the middleware writes there is what the services receive. A hostile or malformed original never travels past the gateway _(Req 5.4)_.
- **The response header is set in `OnStarting`, with the indexer.** Setting it only up front would be appended to when YARP copies the service's response headers (which include the same header, because the service's own middleware echoes it), giving `id, id`. `OnStarting` runs after the proxy has copied everything and just before the headers are sent; assignment replaces whatever is there. It also covers `ExceptionMiddleware`'s `500`, the authentication `401`, the `429` and the `502`, since all of them are written inside the `next` the middleware wraps and the callback is registered before it _(Req 5.4)_.
- **`TraceIdentifier` is left alone.** Kestrel's own request id stays available to host logs; the correlation id has its own accessor _(Req 5.5)_.
- **The scope is a message template, not a dictionary.** The simple console formatter prints a dictionary scope as its type name; `"CorrelationId:{CorrelationId}"` prints `CorrelationId:<id>` and still exposes a structured `CorrelationId` property. Found in the end-to-end run.
- **The scope surrounds `next`.** `ExceptionMiddleware` is inside it, so the `LogError` of an unhandled exception carries the id; `LoggingMiddleware`'s "Incoming request" line does too. The scope is only visible if the console logger prints scopes, hence `"Logging": { "Console": { "IncludeScopes": true } }` in all four `appsettings.json` files _(Req 5.8)_.

**Ordering** _(Req 5.6)_: correlation id, then `ExceptionMiddleware`, then `LoggingMiddleware`. In the three services the line `app.UseMiddleware<CorrelationIdMiddleware>();` goes immediately above `UseMiddleware<ExceptionMiddleware>()`. Scalar and `MapOpenApi` are endpoints, not middleware, and are unaffected.

#### `CorrelationIdHandler` _(Req 5.7)_

A `DelegatingHandler` in `BuildingBlocks.Web` taking `IHttpContextAccessor`. In `SendAsync` it reads `accessor.HttpContext?.GetCorrelationId()` and, if non-null and the outgoing request has no such header, adds it. It is registered with `.AddHttpMessageHandler<CorrelationIdHandler>()` on the Availability client **before** `AddStandardResilienceHandler`, so the header is on the message when the resilience pipeline retries it: every attempt carries the same id (handlers added first are outermost). `AddAvailabilityClient` also calls `services.AddHttpContextAccessor()` and registers the handler as transient.

### Rate limiting _(Req 3)_

```csharp
public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string AppointmentCreate = "appointment-create";
    public const string SlotSearch = "slot-search";

    public const int LoginPermits = 5, AppointmentCreatePermits = 10, SlotSearchPermits = 30;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    public const int Segments = 6;

    public static RateLimitPartition<string> ByClientIp(HttpContext context);          // "login"
    public static RateLimitPartition<string> ByUser(HttpContext context, string policy, int permits);

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services);
}
```

- **`ByClientIp`**: key `ip:` + `RemoteIpAddress` (`MapToIPv4()` if `IsIPv4MappedToIPv6`), or `ip:unknown` when the address is null, so one shared partition rather than none _(Req 3.5)_. Nothing reads a forwarded header.
- **`ByUser`**: key `{policy}:{sub}` from `context.User.FindFirst("sub")` (`Constants.UserIdClaimType`; the JWT handler runs with `MapInboundClaims = false`, so the claim is named `sub`). The policy name is part of the key so the creation and search allowances of one user are separate counters _(Req 3.6)_ (each `AddPolicy` gets its own limiter state anyway; the prefix is explicit insurance and makes the key assertion in tests meaningful). No `sub` falls back to the IP key _(Req 3.4)_.
- **Limiter**: `RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions { PermitLimit = n, Window = 1 min, SegmentsPerWindow = 6, QueueLimit = 0, AutoReplenishment = true })` _(Req 3.2)_.
- **Rejection** (`options.OnRejected`): status `429`; `Retry-After` = `ceil(seconds)` of the lease's `MetadataName.RetryAfter` when present (the .NET 10 sliding-window limiter does not supply it on a rejection, so in practice the window length applies), else the window length (`60`), never less than 1; body via `ProblemDetailsWriter` with `ApiProblemDetails(429, "Too many requests. Try again later.")`; one `Warning` log line with the policy name (`context.HttpContext.GetEndpoint()` carries the YARP route's metadata; the policy name is read from the `EnableRateLimitingAttribute` metadata) and the correlation id from the logging scope _(Req 3.3, 3.7)_.
- **Pipeline position**: `UseRateLimiter()` after `UseAuthorization()`. YARP attaches the route's `RateLimiterPolicy` to its endpoint as `EnableRateLimiting` metadata, which `UseRateLimiter` honours. Because authentication and authorization have already run, an unauthenticated request to a user-partitioned route was already answered `401` and never reaches the limiter _(Req 3.4)_. The login route is anonymous, so its limiter sees every attempt, successful or not.
- **No `GlobalLimiter`.** Routes without a `RateLimiterPolicy` are not limited _(Req 3.1)_.
- **Limits are constants**, not configuration _(Req 3.8)_.
- **Single instance.** The `PartitionedRateLimiter` lives in the process. Two gateways would each allow the full limit; a restart forgets all counts. Documented in the code comment and in `CLAUDE.md` _(Req 3.9)_.

**Client IP.** The gateway is the first hop a client reaches, so `RemoteIpAddress` is the client's address. `UseForwardedHeaders` is not added and `X-Forwarded-For` is not read: with it a caller could name any partition, defeating the login limit. YARP still adds its own `X-Forwarded-*` headers to the downstream request (its default transform), which the services do not use. If a load balancer is ever put in front of the gateway, every client would share the balancer's address, and a trusted-proxy configuration would have to be added then.

### Proxy errors _(Req 4)_

When YARP cannot forward, `ForwarderMiddleware` sets `IForwarderErrorFeature` (with a `ForwarderError`) and a status on the response (`502` for connection failures, `504` for timeouts, `499` for a client abort), but writes no body. The mechanism proposed:

```csharp
public sealed class ProxyErrorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);
        var error = context.Features.Get<IForwarderErrorFeature>();
        if (error is null || context.Response.HasStarted) return;
        switch (error.Error)
        {
            case ForwarderError.RequestTimedOut:   await ProblemDetailsWriter.WriteAsync(context, 504, "A downstream service did not respond in time."); break;
            case ForwarderError.RequestCanceled:
            case ForwarderError.RequestBodyCanceled:
            case ForwarderError.ResponseBodyCanceled: break;           // the client left; nothing to write
            default:                               await ProblemDetailsWriter.WriteAsync(context, 502, "A downstream service is unavailable."); break;
        }
    }
}
```

It is added to the **proxy pipeline** with `MapReverseProxy(proxy => proxy.UseMiddleware<ProxyErrorMiddleware>())`: custom middleware registered there runs before YARP's terminal forwarder, so `await next` returns after the forwarding attempt and the error feature is readable. Considered alternatives:

| Option | Why not chosen |
|---|---|
| Catch exceptions in the global pipeline | YARP does not throw for transport errors; it records them in the feature and sets a status, so `ExceptionMiddleware` never sees them. |
| A response transform on every route | Transforms run per route and are configured in JSON; it would repeat on nine routes, and transforms do not run for failures before a response exists. |
| A custom `IForwarderHttpClientFactory` or a replaced `HttpTransformer` | More surface for the same effect. |

`ProblemDetailsWriter.WriteAsync(context, status, detail)` sets the status, `Content-Type: application/problem+json` and the JSON of `new ApiProblemDetails(status, detail)`, serialized exactly as `ExceptionMiddleware` does it today (since the shared-web-infrastructure Requirement 4 change, `JsonSerializerOptions.Web`, so `{"status":502,"detail":"..."}`); `ExceptionMiddleware` is changed to call it, with no behaviour change (its tests prove it). The details are fixed strings: no host, port or exception text, as in `ExceptionMiddleware` _(Req 4.1, 4.2)_. YARP has already logged the failure with the exception, so the middleware does not log it again. If the service is slow rather than down, the verification must show `504` after the 30 s timeout.

### Aggregated health _(Req 8)_

```csharp
public sealed class DownstreamHealthCheck(string name, Uri healthUrl, IHttpClientFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        // 2 s linked timeout; GET healthUrl using factory.CreateClient("health")
        // 2xx                -> Healthy
        // other status       -> new HealthCheckResult(context.Registration.FailureStatus, "HTTP 503")
        // timeout            -> ..., "timed out"
        // HttpRequestException -> ..., "unreachable"
    }
}
```

- **Registration**: `AddGatewayHealthChecks(configuration)` registers `gateway` (`AddCheck("gateway", () => HealthCheckResult.Healthy())`) and, for each of `auth`, `availability`, `booking`, `AddCheck(name, sp => new DownstreamHealthCheck(name, <cluster address> + "/healthz", factory), failureStatus: HealthStatus.Degraded)`. The address comes from the `ReverseProxy:Clusters:<id>:Destinations` configuration the proxy uses _(Req 8.9)_ (the first destination; `ReverseProxyValidator` has already guaranteed it is a valid URL).
- **Why `FailureStatus` matters.** `HealthCheckService` applies a registration's `failureStatus` only when a check *throws*; a check that returns `Unhealthy` stays `Unhealthy`. So the check builds its non-healthy results with `context.Registration.FailureStatus`, which is `Degraded` for the three downstreams. The aggregate is then `Degraded`, never `Unhealthy` _(Req 8.2, 8.3)_.
- **Timeout and parallelism.** A named `HttpClient` (`"health"`, registered with `AddHttpClient`) with `Timeout = 2 s` is the backstop; the check also links a `CancellationTokenSource` of 2 s to the caller's token and tells the two cancellations apart. `HealthCheckService` runs registrations concurrently, so the endpoint answers in about 2 s with three stopped services, not 6 s _(Req 8.7)_. This client is separate from the proxy's and carries no `Authorization` header _(Req 8.6)_.
- **Endpoint**: `app.MapHealthChecks("/healthz", new HealthCheckOptions { ResultStatusCodes = { [Healthy] = 200, [Degraded] = 200, [Unhealthy] = 503 }, ResponseWriter = HealthResponseWriter.WriteAsync }).AllowAnonymous()`. `UseRateLimiter` has nothing to apply (no policy on the endpoint) _(Req 8.4, 8.6)_.
- **`HealthResponseWriter`**: `application/json`, built from the `HealthReport` as `{ status, totalDurationMs, checks: [{ name, status, description, durationMs }] }`, enums as strings (the `HealthStatus` name via `ToString()`; no serializer-converter global state), `description` null when healthy, never an exception message _(Req 8.5)_.

### Aggregated API documentation, Development only _(Req 10)_

**Where the routes live: `appsettings.Development.json`.** YARP's configuration is read through `IConfiguration`, which merges `ReverseProxy:Routes` across providers by key, so the Development file adds routes to those of `appsettings.json` without touching them. This keeps all routing in configuration, makes "not available outside Development" structural (the shipped file has no such route, and another environment never loads the Development file), and lets the sanity test assert it (Req 10.5, 10.6). Adding the routes in code under `IsDevelopment()` was considered and rejected: it hides them from the configuration test.

```json
"docs-auth":         { "ClusterId": "auth",         "AuthorizationPolicy": "anonymous", "Order": 0,
                       "Match": { "Path": "/openapi/auth/v1.json", "Methods": [ "GET" ] },
                       "Transforms": [ { "PathSet": "/openapi/v1.json" }, { "RequestHeaderOriginalHost": "true" } ] },
"docs-availability": { ... "/openapi/availability/v1.json" ... },
"docs-booking":      { ... "/openapi/booking/v1.json" ... }
```

- `PathSet` rewrites `/openapi/auth/v1.json` to the service's real `/openapi/v1.json`. Nothing else of a service's `/openapi` or `/scalar` becomes reachable, and `/internal` is untouched.
- **Making "Try it" go through the gateway.** `Microsoft.AspNetCore.OpenApi` (.NET 10) fills each document's `servers` from the **incoming request** (scheme, host, port, path base), not from configuration. By default YARP sends the *destination's* host (`localhost:5008`) to the service, so the document would name the service and Scalar would send "Try it" straight to it, bypassing the gateway. The chosen fix is the YARP transform `RequestHeaderOriginalHost: true` on the three documentation routes: the service then sees the gateway's `Host` (`localhost:5290`) and its document lists the gateway. The scheme is the one the service sees (`http`, because the gateway's call is plain http), so the server reads `http://localhost:5290`, which is the gateway's plain-http port; acceptable for development. Reading `X-Forwarded-Host` instead would need `UseForwardedHeaders` in every service, which this spec does not add. **Task 7.1 verifies this against the real documents**; if `servers` is not the gateway's address, the fallback is a Scalar-side override naming `http://localhost:5290` in the Development-only mapping (deterministic, but not derived from the request).
- **Scalar mapping** (`MapGatewayDocs`, called only under `IsDevelopment()`): `MapScalarApiReference` with one document per service (`auth`, `availability`, `booking`, at `/openapi/{service}/v1.json`; the exact call names of the installed `Scalar.AspNetCore` version are checked in task 7.1), the services' title, theme and default client, and `AddPreferredSecuritySchemes(["Bearer"])`. The returned endpoint convention gets `.AllowAnonymous()`, since the fallback policy would otherwise demand a token. The package version equals the services' (`Scalar.AspNetCore` in `Auth.Api.csproj`).
- **Bearer.** The three documents carry the `Bearer` scheme from `AddOpenApiWithBearerAuth`; the UI sends the pasted token as `Authorization: Bearer`, which the gateway validates like any client's.
- **Pipeline.** The correlation id, exception and logging middlewares apply to these requests as to any; no rate limit applies (no policy on the routes).

### The gateway `.http` file

`src/ApiGateway/SmartAppointments.Gateway/SmartAppointments.Gateway.http` does not exist yet. The last task creates it, with `@Gateway_HostAddress = http://localhost:5290` and the requests of the manual verification: register, login, a protected call without a token (401), branches, slot search, create and get appointment, an `/internal/slots/{id}` call (404), `/healthz`.

### `launchSettings.json`

Ports unchanged. Both profiles keep `launchBrowser: true` and gain `"launchUrl": "scalar"`, so a development run lands on the aggregated docs (the profiles only ever run in Development).

## Testing strategy

| Test class | Project | Covers |
|---|---|---|
| `CorrelationIdMiddlewareTests` | BuildingBlocks.Tests | a well-formed id is kept; absent, empty, 65-character, bad-character, and two-value headers each get a fresh 32-hex id; boundary 1 and 64 characters accepted; the id is in `Items`, in the **request** header (what a proxy would copy), and in the response header; the response header holds exactly one value even if the downstream already set it (simulated by the inner delegate); an exception thrown by the inner delegate, with `ExceptionMiddleware` behind it, still yields a `500` carrying the header; a recording `ILogger` sees a `CorrelationId` scope while `next` runs; `TraceIdentifier` unchanged (Req 5.1-5.6, 5.8) |
| `CorrelationIdHandlerTests` | BuildingBlocks.Tests | adds the header from the current `HttpContext`; adds nothing without a context; does not overwrite an existing header; the same value on two sends through a retrying handler (5.7) |
| `ProblemDetailsWriterTests` | BuildingBlocks.Tests | status, content type and body of the problem; `ExceptionMiddlewareTests` stay green unchanged (4.1, 4.6) |
| `AvailabilityClientTests` (addition) | Booking.Tests | the registered client forwards the correlation id of the current request (5.7) |
| `RoutingConfigurationTests` | Gateway.Tests | loads the real `appsettings.json` and `appsettings.Development.json`: the nine routes, their paths, methods and clusters; no path starting `/internal`, none equal to `/{**catch-all}`; every route names `AuthorizationPolicy`, only `auth-login` and `auth-register` are `anonymous`; the three limited routes name the right `RateLimiterPolicy` and nothing else does; three clusters, one destination each, empty address in `appsettings.json`, the development addresses 5008/5202/5310 in Development (1.1, 1.4, 1.6, 2.1, 2.6, 3.1, 6.3, 7.2) |
| `DocsRoutingTests` | Gateway.Tests | `appsettings.json` has no route whose path contains `openapi` or `scalar`; `appsettings.Development.json` adds exactly `docs-auth`, `docs-availability`, `docs-booking`, each `GET`, `anonymous`, mapped to its cluster, with a `PathSet` of `/openapi/v1.json` and `RequestHeaderOriginalHost`; merged with `appsettings.json` the route set is the nine plus three (10.1, 10.5, 10.6) |
| `ReverseProxyValidatorTests` | Gateway.Tests | missing cluster, no destination, blank, relative and non-http address each throw with the key in the message; valid configuration passes (6.3) |
| `ProxyErrorMiddlewareTests` | Gateway.Tests | with a fake `IForwarderErrorFeature` on the context: connection failure → 502 problem+json, timeout → 504, cancellations write nothing, no feature leaves the response untouched, started response is not written again, the body contains no exception text (4.1-4.5) |
| `RateLimitPoliciesTests` | Gateway.Tests | `ByClientIp`: key from `RemoteIpAddress`, IPv4-mapped IPv6 equals the IPv4 key, a spoofed `X-Forwarded-For` is ignored, null address shares one partition; `ByUser`: key from `sub`, different users and different policies get different keys, no `sub` falls back to the IP key; the limiter obtained for a partition permits exactly 5/10/30 `AttemptAcquire` calls and rejects the next with a `RetryAfter` metadata of at least 1 s; the constants equal the BRD table (3.1-3.6) |
| `RateLimitRejectionTests` | Gateway.Tests | `OnRejected` writes `429`, `application/problem+json`, `Retry-After` an integer ≥ 1 (also when the lease has no metadata), the body is an `ApiProblemDetails` (3.3) |
| `DownstreamHealthCheckTests` | Gateway.Tests | with a fake `HttpMessageHandler`: 200 → Healthy; 503, 404 → Degraded "HTTP n"; `HttpRequestException` → Degraded "unreachable"; a handler that never returns → Degraded "timed out" within about 2 s; the right URL (`<address>/healthz`) is called; no `Authorization` header sent; the description never contains the exception message or host (8.1, 8.2, 8.6) |
| `HealthEndpointTests` | Gateway.Tests | `HealthCheckService` built from `AddGatewayHealthChecks` over fake handlers: all up → Healthy; one down → Degraded; the status-code mapping 200/200/503 of the options; `HealthResponseWriter` JSON shape, statuses as strings, one entry per check (8.3-8.5, 8.7) |

Pure unit tests, following the existing style (xUnit and Moq; `DefaultHttpContext` and fakes rather than a test server). There is no test of the assembled pipeline, of YARP actually proxying, of the ordering of the middleware in `Program.cs`, of the `RateLimiterPolicy` metadata reaching `UseRateLimiter`, or of the header de-duplication against a real YARP response, because those need the gateway running. They are verified by hand against four running processes in the last task. That list is the price of leaving out integration tests (Req 7.3).

## Error handling

| Condition | Response | Produced by | Req |
|---|---|---|---|
| Path matches no route (`/internal/...`, `/healthz` of a service, unknown) | `404` (framework, empty body) | routing | 1.3 |
| Protected route, no/invalid/expired token | `401` (empty body, `WWW-Authenticate: Bearer`) | JWT bearer challenge, as in the services | 2.2 |
| Valid token, wrong role | the service's `403`, passed through | the service | 2.3 |
| Over a rate limit | `429` problem+json, `Retry-After` | limiter `OnRejected` | 3.3 |
| Service unreachable | `502` problem+json | `ProxyErrorMiddleware` | 4.1 |
| Service too slow (30 s) | `504` problem+json | `ProxyErrorMiddleware` | 4.2 |
| Service answers any status | passed through unchanged | YARP | 4.5 |
| Unexpected exception in the gateway | `500` problem+json | `ExceptionMiddleware` | 4.6 |
| Downstream `/healthz` fails | `200`, overall `Degraded` | `/healthz` | 8.2-8.4 |

Every row carries `X-Correlation-ID` _(Req 5.4)_.

## Decisions taken

The project owner settled these before the spec was written; they are recorded, not open.

1. **YARP**, configuration in the `ReverseProxy` section of `appsettings.json`, development destination addresses in `appsettings.Development.json`, ports 5290/7176 kept, the five public prefixes routed, `/internal/**` and the services' health, OpenAPI and Scalar paths never routed.
2. **Edge JWT validation** with `AddJwtAuthentication`; register and login anonymous, everything else needs an authenticated user via the route `AuthorizationPolicy`; roles stay in the services; the `Authorization` header is forwarded unchanged.
3. **Rate limiting**: in-memory sliding window, 1 minute, 6 segments, no queue; login 5/min per IP, create appointment 10/min per user, slot search 30/min per user; `sub` as the user key; `429` problem+json with `Retry-After`; no join-queue limit, no global limit; single-instance limitation noted. Client IP is `RemoteIpAddress`, no forwarded headers (stated above).
4. **Correlation id** `X-Correlation-ID`, shared `CorrelationIdMiddleware` in `BuildingBlocks.Web`, tested in `BuildingBlocks.Tests`, used by the gateway and all three services; kept when 1-64 characters of `[A-Za-z0-9-_.]`, else generated; in `HttpContext`, in the logger scope, echoed on the response; forwarded downstream; Booking's Availability client forwards it with a `DelegatingHandler`; placed ahead of the exception and logging middlewares so error responses carry it.
5. **Aggregated health**: `/healthz` of the gateway is `Healthy` itself, one check per downstream with a short timeout, a failing downstream is `Degraded` (HTTP 200), a JSON body lists the checks, `Healthy` 200, `Degraded` 200, `Unhealthy` 503.
6. **Shared middlewares** at the gateway; proxy failures become `502` problem+json.
7. **`tests/Gateway.Tests`**: xUnit and Moq, unit tests only (configuration sanity, limiter policies, health checks over a fake `HttpMessageHandler`); integration tests out of scope; the last task is a manual end-to-end run against all four processes.

## Decisions taken after review

The project owner settled the open points of the first draft. They are recorded here, not reopened.

8. **Proxy errors.** An unreachable or refusing service gives `502`; a service that connects but does not answer within the 30 s cluster `ActivityTimeout` gives `504`; both are PascalCase `ApiProblemDetails` problem+json. `ProxyErrorMiddleware` in the proxy pipeline stays the mechanism; task 6 first verifies the YARP API names (`IForwarderErrorFeature`, the pipeline hook) and adjusts the code, not the behaviour, if they differ _(Req 4.1, 4.2)_.
9. **`CorrelationIdHandler`**, a `DelegatingHandler` using `IHttpContextAccessor`, lives in `BuildingBlocks.Web` so any service can reuse it. `Booking.Infrastructure` therefore references `BuildingBlocks.Web`: the first Infrastructure-to-Web dependency, accepted _(Req 5.7)_.
10. **No HTTPS redirection in the services.** `UseHttpsRedirection()` is removed from Auth, Availability and Booking; the gateway keeps it. Gateway-to-service and Booking-to-Availability calls stay `http`. The manual end-to-end no longer depends on launch profiles _(Req 9)_.
11. **Console log scopes.** `Logging:Console:IncludeScopes` is enabled in all four `appsettings.json` files _(Req 5.8)_.
12. **Aggregated API docs, Development only**, as designed above: routes in `appsettings.Development.json`, `RequestHeaderOriginalHost` so the documents' `servers` is the gateway, one Scalar UI at `/scalar` _(Req 10)_.
13. **Accepted as drafted:** the fallback authenticated-user policy with `/healthz` anonymous; PascalCase problem bodies; the 30 s cluster timeout; path casing unchanged (`/api/Auth` and `/api/auth` both work, nothing is rewritten); the `Response.OnStarting` assignment for the response header. **`launchBrowser`** stays `true`, now with `launchUrl: "scalar"`, because the gateway has a page to open in Development (a judgement call, trivially reversible).

### Things in the existing code that bear on these decisions

- **There is no gateway `.http` file** (the task list said "update"); one is created.
- **`ApiProblemDetails` serializes with `JsonSerializerOptions.Web`** (`{"status":429,"detail":"..."}`, camelCase), the same body controllers return for a failed `Result` (shared-web-infrastructure Requirement 4; it was PascalCase before).
- **`LoggingMiddleware` logs only the method and path**, so it cannot leak a token.
- **`Constants.ApiKeyHeaderName` and `AddApiKeyAuthentication` already exist**; nothing in the gateway touches them.
- **Auth's token `Issuer` and `Audience`** are `https://localhost:7220`; the gateway copies them, as Booking does.
