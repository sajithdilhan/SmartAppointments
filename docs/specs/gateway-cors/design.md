# Gateway CORS — Design

> References like _(Req 3.2)_ point at [`requirements.md`](requirements.md).

## Overview

The gateway answers CORS with the ASP.NET Core CORS middleware (`AddCors` / `UseCors`) and one named policy built at startup from `Cors:AllowedOrigins`. No custom CORS engine: the framework already short-circuits the preflight, adds the headers to every response of an allowed origin, and adds nothing for any other origin. The gateway adds only what the framework does not do: fail-fast validation of the origin list, a fixed policy, the middleware position, and a guard against downstream `Access-Control-*` headers.

The decisions below were taken by the project owner, or verified against the framework's behaviour (see "Verified behaviour of the framework middleware"), and are recorded here.

1. **Built-in CORS middleware, one policy for the whole gateway** _(Req 2, 3, 4, 5)_. Policy `gateway-cors`, built from the validated list; methods `GET, POST, PUT, DELETE`; headers `Authorization, Content-Type, Idempotency-Key, X-Correlation-ID`; exposed `X-Correlation-ID, Retry-After, Location`; max-age 10 minutes; no credentials. The values are named constants, not configuration _(Req 2.7)_.
2. **Origin matching is a delegate, not `WithOrigins`** _(Req 1.4, 2.1)_: `SetIsOriginAllowed(origin => allowed.Contains(origin))` over an ordinal `HashSet<string>`. Two reasons. It is an exact ordinal match with no URL normalisation, so `null`, a different port or `http://localhost:4200.evil.example` never match. And the framework adds `Vary: Origin` only when the policy has more than one origin or an origin delegate: with `WithOrigins("one origin")` it would omit it, which breaks Req 2.1.
3. **A blank entry is "no origin configured"** (owner decision on Req 1.5). Entries that are null, empty or whitespace are dropped before validation; a non-blank malformed entry still fails startup. This supersedes the word "blank" in the rejected list of Req 1.3, which would contradict Req 1.5 (compose passes an empty `Cors__AllowedOrigins__0` when `WEB_ORIGIN` is unset). Duplicates after filtering are collapsed, not an error (see "Configuration").
4. **`DELETE` stays in the allowed methods** as the requirements say _(Req 2.3)_.
5. **CORS is registered only when at least one origin remains** _(Req 1.2)_: no services, no middleware, no transform. An empty list leaves the gateway exactly as it is today.
6. **The CORS headers come from the framework; the gateway guarantees "once and only the gateway's"** with a small YARP response transform that removes any `Access-Control-*` header a service sent and lets the policy re-apply its own _(Req 4.2, 2.2, 5.1)_. The three services emit none _(Req 6.1)_, so this is defence in depth, not a feature.
7. **The pipeline stays a unit-test and manual-check affair** _(Req 7.4)_: validator, policy and configuration tests, plus in-process tests that construct the framework `CorsMiddleware` and the gateway's own middlewares over a `DefaultHttpContext`, in the style of `UnmatchedRequestMiddlewareTests`. No `WebApplicationFactory`.

### Verified behaviour of the framework middleware

Probed on this repository's SDK (10.0.401) with an in-process Kestrel server and the policy of this design. These facts drive the decisions above and the tests.

| Request | Observed |
|---|---|
| Preflight (`OPTIONS` + `Origin` + `Access-Control-Request-Method`), allowed origin, allowed method and headers | `204`; `Access-Control-Allow-Origin: <origin>`, `Allow-Methods: GET,POST,PUT,DELETE`, `Allow-Headers: Authorization,Content-Type,Idempotency-Key,X-Correlation-ID`, `Max-Age: 600`, `Vary: Origin`; no `Allow-Credentials`. The request never reaches the next middleware, whatever the path (routed, unrouted). |
| Same, but method `PATCH` or header `x-evil` requested | Still `204` with the same headers as above, the lists simply lack `PATCH` / `x-evil`. The browser compares the lists with what it asked and blocks the real request. (Not an error, not forwarded, not `5xx`.) |
| Preflight, origin not allowed or `null` | `204` with **no** `Access-Control-*` header and no `Vary`. The request never reaches the next middleware. |
| `OPTIONS` with `Origin` but no `Access-Control-Request-Method` | Not a preflight: passed on to the next middleware, with the actual-request headers added for an allowed origin. |
| Actual request, allowed origin | `Access-Control-Allow-Origin`, `Access-Control-Expose-Headers`, `Vary: Origin`; status, body and other headers untouched. |
| Actual request, other origin or no `Origin` | No CORS header, no `Vary`. Nothing is rejected. |
| Headers survive later failures | The framework writes its headers to `Response.Headers` before calling the next middleware. A `404` set by a later middleware, a `500` written after an exception, a status-only `401`: all kept `Access-Control-Allow-Origin`. |
| A downstream (endpoint) sets `Access-Control-Allow-Origin` / `Allow-Credentials` itself | For an allowed origin the framework overwrites its own names but a different name (`Allow-Credentials: true`) **survives**; for other origins everything the service sent passes through. Hence decision 6. |

`ExceptionMiddleware` and `ProblemDetailsWriter` only set `StatusCode`, `ContentType` and the body; neither calls `Response.Clear()` nor removes headers, so the CORS headers set earlier in the pipeline stay on the `500`, the `429` (`RateLimitPolicies.RejectAsync`), the `502`/`504` (`ProxyErrorMiddleware`) and the unmatched `404`.

## Architecture

```
                         browser (allowed Origin)
                                  |
   +--------------------------------------------------------------------+
   | CorrelationId -> Exception -> Logging -> CORS -> HttpsRedirection  |
   |   -> UnmatchedRequest -> Authentication -> Authorization           |
   |   -> RateLimiter -> /healthz | docs | YARP (+ ProxyError)           |
   +--------|--------------------------------------------------|--------+
            | preflight: 204 here, nothing below runs          |
            v                                                  v
        (no service is called)             services (send no Access-Control-*)
```

### Pipeline order and why

The existing order in `Program.cs` is kept; one line is inserted _(Req 6.4)_.

| # | Middleware | Why it is here |
|---|---|---|
| 1 | `CorrelationIdMiddleware` | Unchanged. First, so the preflight answer and every error carry `X-Correlation-ID` _(Req 3.6)_. |
| 2 | `ExceptionMiddleware` | Unchanged. Outside CORS so that an exception thrown by anything below, including the CORS middleware or its policy provider, still becomes a `500` problem body with a correlation id _(Req 6.4)_. The CORS headers are already on the response by then and are not cleared _(Req 4.1, `500`)_. |
| 3 | `LoggingMiddleware` | Unchanged. Outside CORS so that a preflight, which ends at step 4, is still logged _(Req 3.6)_. |
| 4 | **`UseCors("gateway-cors")`** (new, only when origins are configured) | Before everything that can answer: it must end the preflight before `UnmatchedRequestMiddleware` (`404` for any non-routed path), authentication (`401`) and the rate limiter (`429`, and a spent permit) _(Req 3.2, 3.3, 6.2)_; and it must put the headers on the response before any of those produce a `401`, `404`, `429`, `502`, `504` _(Req 4.1)_. |
| 5 | `UseHttpsRedirection` | Unchanged, but now **after** CORS: a browser does not follow a redirect on a preflight, so an `http` preflight must be answered, not redirected to `https` (relevant when running locally with both `5290` and `7176`). A redirect for an allowed origin also keeps its CORS headers. In Docker there is no HTTPS port and the redirect does nothing. |
| 6 | `UnmatchedRequestMiddleware`, `UseAuthentication`, `UseAuthorization`, `UseRateLimiter`, endpoints | Unchanged _(Req 4.4, 5.2)_. A non-preflight request is processed by exactly the same rules whatever its `Origin`. |

CORS is not placed before `ExceptionMiddleware`: that would leave a failure inside CORS without a correlation id or a problem body, against Req 6.4. It is not placed after `UseHttpsRedirection`, `UnmatchedRequestMiddleware`, authentication or the rate limiter, which would break Req 3.2, 3.3 and 4.1.

`WebApplication` adds routing at the start of the pipeline, so by the time CORS runs the endpoint is already selected (`null` for an unmatched path). The framework CORS middleware does not need it: no route, route policy or metadata is touched, so no route changes _(Req 6.3)_ and no YARP `CorsPolicy` route property is used.

### Preflight and the other origins

- **Allowed origin, preflight**: `204`, ends at step 4 for every path including `/internal/**` and unrouted paths; no token, no rate-limit permit, no forwarding _(Req 3.1 to 3.3, 6.2)_.
- **Allowed origin, disallowed method or header** _(Req 3.4)_: the framework answers `204` with the policy's own lists (without the asked method or header) and the allowed origin. Req 3.4 asks that the response not be "usable"; it is unusable for the browser, which blocks the real request because its method or header is missing from `Allow-Methods` / `Allow-Headers`. Nothing is forwarded and it is not a `5xx`. See open question 1.
- **Disallowed or `null` origin, preflight** _(Req 5.1, 5.3, 5.4)_: the framework still ends the preflight with a bare `204` and no `Access-Control-*` header, so the browser fails the preflight. It is not forwarded and costs no permit. This is a plain `204` rather than "the ordinary pipeline's" answer (`401` or `404`); see open question 2 for why that is the right behaviour.
- **Disallowed origin or no origin, actual request** _(Req 5.2)_: goes through the whole pipeline with no CORS header and nothing rejected.
- **`OPTIONS` without `Origin` or without `Access-Control-Request-Method`** _(Req 3.5)_: not a preflight; the framework passes it on (with the actual-request headers if the origin is allowed), so it is answered by the existing rules (`401` on a routed path without a token).

### Downstream `Access-Control-*` headers _(Req 4.2, 6.1)_

Auth, Availability and Booking register no CORS and send none; nothing here changes a service. Should one ever send one, YARP copies it into the response after the CORS middleware has written its own, which would duplicate a name or leak a name the policy never sends (`Allow-Credentials`, Req 2.2). A global YARP response transform, `DownstreamCorsHeaderTransform`, closes that: for each response header of the proxied response whose name starts with `Access-Control-` (ordinal ignore case), it removes that name from the outgoing response, then, if the request's `Origin` is allowed, asks the policy to re-apply its own headers (`ICorsService.EvaluatePolicy` + `ApplyResult` with the singleton `CorsPolicy`). A response with no such header is untouched, so successful responses stay unchanged _(Req 4.3)_. The transform runs after YARP's header copy and before the body, so the headers are not yet sent. It is registered only when origins are configured.

## Components and interfaces

All new code is in the gateway project, folder `Cors/`; nothing is added to `BuildingBlocks`, `BuildingBlocks.Web` or a service. `Constants.AllowedOriginsPolicy` (an authorization role policy) is not touched and not reused. The header name `X-Correlation-ID` comes from `Constants.CorrelationIdHeaderName`.

### Configuration _(Req 1)_

`appsettings.json` (new section, empty):

```json
"Cors": { "AllowedOrigins": [] }
```

`appsettings.Development.json`:

```json
"Cors": { "AllowedOrigins": [ "http://localhost:4200", "http://localhost:8081" ] }
```

Environment form: `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, ... Arrays are merged by index, so in Docker (the containers run as Development) `Cors__AllowedOrigins__0=${WEB_ORIGIN}` replaces entry 0 (`4200`) and entry 1 (`8081`) stays from the development file. The result is `[WEB_ORIGIN, 8081]`: with the default `WEB_ORIGIN=http://localhost:8081` that is a duplicate (collapsed); with `WEB_ORIGIN` unset it is `["", 8081]` (blank dropped, `8081` stays). Harmless, and the reason blank filtering and de-duplication both exist.

`docker-compose.yml`, `gateway.environment`:

```yaml
      Cors__AllowedOrigins__0: ${WEB_ORIGIN:-}
```

`.env.example` gains `WEB_ORIGIN=http://localhost:8081` with a one-line comment (the origin of the web container; an `http://host:port` with no trailing slash).

### `CorsOriginsValidator` _(Req 1.1 to 1.3, 1.5, 1.6)_

Same style as `ReverseProxyValidator`: a static class in `Configuration/`, called first thing from `Program.cs`, throwing `InvalidOperationException` with a message naming the key and its environment form.

```csharp
public static class CorsOriginsValidator
{
    public const string Key = "Cors:AllowedOrigins";

    /// Returns the validated origins: blank entries dropped, duplicates collapsed (ordinal), order kept.
    public static IReadOnlyList<string> Validate(IConfiguration configuration);
}
```

For each child of `Cors:AllowedOrigins` (the child's own key is the index used in the message), skipping `IsNullOrWhiteSpace`:

| Check, in this order | Rejects |
|---|---|
| contains `*` | `*`, `https://*.example.com`, any pattern |
| `Uri.TryCreate(entry, UriKind.Absolute, ...)` | not an absolute URL (`localhost:4200`, `/path`, `not a url`) |
| scheme is `http` or `https` | `ftp://`, `file://` |
| `uri.UserInfo` empty | `http://user:pw@host` |
| `uri.Query` and `uri.Fragment` empty | `?x=1`, `#frag` |
| `entry` equals `uri.GetLeftPart(UriPartial.Authority)` ordinally | any path (including the trailing slash of `https://app.example.com/`), and also an upper-case scheme or host or an explicit default port (`http://host:80`), which would never equal the `Origin` a browser sends and so would silently never match |

Message shape (as `ReverseProxyValidator`): `'Cors:AllowedOrigins:{index}' must be an origin, scheme://host[:port] in lower case with no path, trailing slash, query, fragment, user information or wildcard, but is '{entry}'. Set it in appsettings.Development.json or with the environment variable 'Cors__AllowedOrigins__{index}'.` One specific reason is named per rule. The entry is configuration, not a secret, so echoing it is fine. An empty or absent section returns an empty list and is not an error _(Req 1.2)_. The list is computed once in `Program.cs` and captured by the policy and the transform; there is no `IOptionsMonitor` and no reload _(Req 1.6)_.

### `GatewayCors` _(Req 2, 3, 4, 5)_

```csharp
public static class GatewayCors
{
    public const string PolicyName = "gateway-cors";
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "DELETE"];
    public static readonly string[] AllowedHeaders =
        [HeaderNames.Authorization, HeaderNames.ContentType, IdempotencyKeyHeader, Constants.CorrelationIdHeaderName];
    public static readonly string[] ExposedHeaders =
        [Constants.CorrelationIdHeaderName, HeaderNames.RetryAfter, HeaderNames.Location];
    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    public static CorsPolicy BuildPolicy(IReadOnlyList<string> origins);

    // AddCors(o => o.AddPolicy(PolicyName, BuildPolicy(origins))) and the singleton CorsPolicy for the transform
    public static IServiceCollection AddGatewayCors(this IServiceCollection services, IReadOnlyList<string> origins);

    // app.UseCors(PolicyName)
    public static IApplicationBuilder UseGatewayCors(this IApplicationBuilder app);
}
```

`BuildPolicy` uses `CorsPolicyBuilder`: `SetIsOriginAllowed` over a `HashSet<string>(origins, StringComparer.Ordinal)`, `WithMethods`, `WithHeaders`, `WithExposedHeaders`, `SetPreflightMaxAge`, and **no** `AllowCredentials()` _(Req 2.2)_. `AllowAnyOrigin`, `AllowAnyHeader`, `AllowAnyMethod` and `SetIsOriginAllowedToAllowWildcardSubdomains` are never called. The policy is built from the already validated list, so it cannot contain `*`.

`Program.cs` changes (the only edits to existing code):

```csharp
var corsOrigins = CorsOriginsValidator.Validate(builder.Configuration);   // right after ReverseProxyValidator.Validate
...
if (corsOrigins.Count > 0) builder.Services.AddGatewayCors(corsOrigins);
...
app.UseMiddleware<LoggingMiddleware>();
if (corsOrigins.Count > 0) app.UseGatewayCors();                           // before UseHttpsRedirection
```

`AddReverseProxy()` gets `.AddTransforms(c => c.AddResponseTransform(...))` calling `DownstreamCorsHeaderTransform` when `corsOrigins.Count > 0`.

### `DownstreamCorsHeaderTransform` _(Req 4.2, 2.2, 5.1)_

```csharp
public static class DownstreamCorsHeaderTransform
{
    // Removes Access-Control-* names that the proxied response carried and, for an allowed origin, re-applies the policy.
    public static void Apply(HttpContext context, HttpResponseMessage? proxyResponse, ICorsService cors, CorsPolicy policy);
}
```

The YARP hook is a one-line lambda around `Apply`, so the logic is testable without the YARP types.

## Data model

None. No persistence and no new package: `Microsoft.AspNetCore.Cors` is part of the ASP.NET Core shared framework.

## Error handling

CORS adds no failure of its own; every response below keeps the status and body its producer gave it _(Req 4.3)_ and gains the headers when the request's `Origin` is allowed _(Req 4.1)_.

| Condition | Produced by | Status | CORS headers added by |
|---|---|---|---|
| Invalid origin list | `CorsOriginsValidator` at startup | the process does not start _(Req 1.3)_ | n/a |
| Preflight | CORS middleware | `204` | the framework |
| No / bad token | JWT challenge | `401`, empty body | the framework (headers set earlier, status-only response keeps them) |
| Service `4xx` / `5xx` | service, passed through | as sent | the framework, after `DownstreamCorsHeaderTransform` cleaned any service-sent `Access-Control-*` |
| Unrouted path, `/internal/**` | `UnmatchedRequestMiddleware` | `404` | the framework |
| Rate limit | `RateLimitPolicies.RejectAsync` | `429` + `Retry-After` | the framework (`Retry-After` is exposed) |
| Service down / slow | `ProxyErrorMiddleware` | `502` / `504` | the framework |
| Unhandled exception | `ExceptionMiddleware` | `500` | the framework (not cleared) |
| `/healthz` | health endpoint | `200` / `503` | the framework |

## Testing strategy

xUnit + Moq in `tests/Gateway.Tests`, no `WebApplicationFactory` _(Req 7)_. The in-process pipeline tests are cheap because `CorsMiddleware` has a public constructor (`RequestDelegate, ICorsService, ICorsPolicyProvider, ILoggerFactory, string policyName`): build `ICorsService` / `ICorsPolicyProvider` from a small `ServiceCollection` (`AddLogging`, `AddGatewayCors`), run the middleware on a `DefaultHttpContext` and a recording `next`.

| Test class | Cases | Req |
|---|---|---|
| `CorsOriginsValidatorTests` | Valid: `http://localhost:4200`, `https://app.example.com`, `https://app.example.com:8443`, IPv6 host. Empty/absent section returns an empty list. Blank and whitespace entries dropped (and a list of only blank entries is empty); blank beside a valid one returns the valid one. Duplicates collapsed. Rejected, each asserting that the message contains `Cors:AllowedOrigins:<index>` and `Cors__AllowedOrigins__<index>`: `*`, `https://*.example.com`, `localhost:4200`, `/path`, `not a url`, `ftp://host`, `https://app.example.com/`, `https://app.example.com/path`, `http://localhost:4200?x=1`, `http://localhost:4200#f`, `http://user:pw@localhost`, `HTTP://localhost:4200`, `http://localhost:80`. A bad entry after a good one still throws and names its own index. | 1.2, 1.3, 1.5 |
| `CorsConfigurationTests` (reads the linked `appsettings*.json` like `RoutingConfigurationTests`) | `Cors:AllowedOrigins` is empty in `appsettings.json`; exactly `http://localhost:4200` and `http://localhost:8081` in `appsettings.Development.json` (in that order); both pass the validator. The existing nine-routes test keeps asserting the routes _(Req 6.3)_; no test change needed there. | 1.1, 6.3, 7.2 |
| `GatewayCorsPolicyTests` (policy from `ICorsPolicyProvider.GetPolicyAsync(ctx, GatewayCors.PolicyName)`) | `Methods` is exactly `GET, POST, PUT, DELETE` (no `PATCH`, `HEAD`, `OPTIONS`); `Headers` exactly the four; `ExposedHeaders` exactly the three; `PreflightMaxAge` is 10 minutes; `SupportsCredentials` false; `AllowAnyOrigin`, `AllowAnyMethod`, `AllowAnyHeader` false; `IsOriginAllowed` true for each configured origin and false for `https://localhost:4200`, `http://localhost:4201`, `http://localhost:4200.evil.example`, `HTTP://LOCALHOST:4200`, `null`, `*`, empty. | 1.4, 2.2 to 2.7, 5.4 |
| `GatewayCorsMiddlewareTests` (framework middleware + policy over `DefaultHttpContext`) | Preflight from an allowed origin: `204`, the exact headers (`Allow-Origin` echoes the origin, methods, requested allowed headers, `Max-Age: 600`, `Vary: Origin`, no `Allow-Credentials`), `next` not called, whatever the path (`/api/appointments`, `/nothing`, `/internal/slots/1`). Preflight asking `PATCH` or `X-Evil`: `204`, `next` not called, the lists lack `PATCH` / `X-Evil` (documents Req 3.4's behaviour). Preflight from `https://evil.example` and from `null`: `204`, no `Access-Control-*`, no `Vary`, `next` not called. `OPTIONS` without `Origin`, and `OPTIONS` with `Origin` but no `Access-Control-Request-Method`: `next` called. Actual request, allowed origin: `next` called, `Allow-Origin`, `Expose-Headers` (the three), `Vary: Origin`, no `Allow-Credentials`. Actual request with no `Origin`: no CORS header and no `Vary`. Actual request, other origin: no CORS header, `next` called (nothing rejected). A chain `ExceptionMiddleware` -> CORS -> throwing `next` yields `500` and keeps `Allow-Origin` (Req 4.1 `500`). A `next` that sets `404` / `401` / `429` keeps the headers. | 2.1 to 2.6, 3.1 to 3.5, 4.1, 4.3, 5.1, 5.3, 5.4, 6.2, 6.4 |
| `DownstreamCorsHeaderTransformTests` (`Apply`) | A proxied response with `Access-Control-Allow-Origin: *`, `Allow-Credentials: true`: both gone, and for an allowed origin only the policy's `Allow-Origin` and `Expose-Headers` remain, each once; for another origin none remain. A proxied response without such headers leaves the outgoing response unchanged. | 4.2, 4.3, 2.2, 5.1 |

Not coverable in-process, so verified by hand against the running gateway in the last task, the requests recorded in `SmartAppointments.Gateway.http` _(Req 7.4)_:

- the real pipeline order (CORS ends a preflight before `404`/`401`/`429`; headers on `401`, `404`, `429`, `502`);
- the correlation id and logging on a preflight _(Req 3.6)_ (`OnStarting` does not run in a `DefaultHttpContext` test);
- startup failure for each bad value, via `Cors__AllowedOrigins__0`;
- YARP's real header copy (nothing a service sends can be observed since none sends any; the transform is covered by its unit test).

### Manual checks in `SmartAppointments.Gateway.http`

A new "CORS" section, with `@WebOrigin = http://localhost:4200`. Each request is annotated with the expected result.

| Request | Expected |
|---|---|
| `OPTIONS /api/appointments`, `Origin`, `Access-Control-Request-Method: POST`, `Access-Control-Request-Headers: authorization,idempotency-key,content-type`, no token | `204`; `Allow-Origin` = the origin, `Allow-Methods`, `Allow-Headers`, `Max-Age: 600`, `Vary: Origin`; `X-Correlation-ID`; no `Allow-Credentials` |
| Same on `/api/auth/login`, on `/nothing` and on `/internal/slots/{{SlotId}}` | `204` each, no `401`/`404`; nothing in Availability's log |
| Preflight asking `PATCH`, and asking `x-evil` | `204` whose `Allow-Methods` / `Allow-Headers` lack the asked value |
| Preflight with `Origin: https://evil.example` and with `Origin: null` | `204`, no `Access-Control-*` header |
| `OPTIONS /api/appointments` with `Origin` and no `Access-Control-Request-Method`, no token | `401` (not a preflight) |
| `GET /api/branches` with `Origin`, no token | `401` with `Allow-Origin` and `Expose-Headers` |
| `GET /api/queue/x` with `Origin` | `404` with `Allow-Origin` |
| `POST /api/auth/login` six times with `Origin` | the 6th is `429` with `Allow-Origin` and `Retry-After`; also, ten preflights to `/api/auth/login` first leave all five login attempts available |
| `GET /api/branches` with a token, `Origin`; and `GET /healthz` with `Origin` | `200` with `Allow-Origin` and unchanged body |
| `GET /api/branches` with `Origin: https://evil.example` and a token | `200`, no `Access-Control-*` |
| With Booking stopped, `GET /api/appointments/{{AppointmentId}}` with `Origin` and a token | `502` with `Allow-Origin` |
| Start with `Cors__AllowedOrigins__0=*`, `https://app.example.com/` and `localhost:4200` | start-up fails naming the key and `Cors__AllowedOrigins__0` |
| Start with `Cors:AllowedOrigins` empty (a non-Development environment) | no `Access-Control-*` on any of the above; the preflight is `401`/`404` as today |
| `docker compose up` with `WEB_ORIGIN` unset, then set | starts both times; origin `8081` allowed (it is also in the development file) |

Documentation updated in the final task (not here): the gateway paragraph of `CLAUDE.md`, the "Out of scope" CORS line of `docs/specs/api-gateway/requirements.md`, and the `.env.example` and compose files above.

## Requirement traceability

| Requirement | Design |
|---|---|
| 1.1 | `Cors:AllowedOrigins` in both appsettings files; `CorsConfigurationTests` |
| 1.2 | validator returns an empty list; CORS services, middleware and transform registered only when non-empty |
| 1.3 | `CorsOriginsValidator` rules and message; called before `builder.Build()` like `ReverseProxyValidator`; blank handling per decision 3 |
| 1.4 | `SetIsOriginAllowed` over an ordinal `HashSet`; canonical-form rule in the validator; no wildcard API called |
| 1.5 | compose `Cors__AllowedOrigins__0: ${WEB_ORIGIN:-}`, `.env.example`; blank dropped (decision 3); Development-file interplay explained under Configuration |
| 1.6 | list computed once in `Program.cs`; no options monitor |
| 2.1 | origin delegate (gives `Vary: Origin`); allowed origin echoed, never `*` |
| 2.2 | no `AllowCredentials()`; transform removes a service's `Allow-Credentials` |
| 2.3 to 2.6 | `GatewayCors` constants (`AllowedMethods`, `AllowedHeaders`, `ExposedHeaders`, `PreflightMaxAge`) |
| 2.7 | constants in code; `GatewayCorsPolicyTests` |
| 3.1, 3.2 | CORS middleware at position 4 ends the preflight with `204` before unmatched/auth/rate limiter, any path |
| 3.3 | same position; no forwarding, no permit |
| 3.4 | framework answers with the policy's lists; browser blocks (open question 1) |
| 3.5 | framework treats it as a non-preflight and calls the next middleware |
| 3.6 | CorrelationId and Logging are positions 1 and 3, outside CORS |
| 4.1 | position 4 sets headers before every later producer; `ExceptionMiddleware`/`ProblemDetailsWriter` do not clear them |
| 4.2 | `DownstreamCorsHeaderTransform` |
| 4.3 | headers added only; no `Vary` without an `Origin`; transform no-op without service-sent headers |
| 4.4, 5.2 | no route or pipeline rule depends on `Origin`; nothing rejected |
| 5.1, 5.3 | framework adds nothing for another origin; preflight ends as a bare `204` (open question 2) |
| 5.4 | `null` fails the ordinal match |
| 6.1 | no service project changed |
| 6.2 | `/internal/**` stays unrouted; preflight `204` at position 4, other requests `404` with CORS headers |
| 6.3 | no route edit; existing nine-route test unchanged |
| 6.4 | pipeline order table |
| 7.1 to 7.3 | `CorsOriginsValidatorTests`, `CorsConfigurationTests`, `GatewayCorsPolicyTests` (plus the in-process middleware and transform tests) |
| 7.4 | manual `.http` section above |

## Open questions

1. **Req 3.4 wording.** Req 3.4 says a preflight asking for a disallowed method or header is answered "without `Access-Control-Allow-Origin` being usable". The framework (verified) still sends `Access-Control-Allow-Origin` for an allowed origin, with `Allow-Methods` / `Allow-Headers` that omit the asked value, and the browser blocks the real request. The observable outcome the requirement wants holds (blocked, not forwarded, not `5xx`), but the response does carry `Allow-Origin`. Suggest rewording 3.4 to "answered with Allow-Methods and Allow-Headers that do not list it". Making the gateway withhold `Allow-Origin` instead would need a custom preflight middleware; not recommended.
2. **Req 5.3 wording.** Req 5.3 says a preflight from a disallowed origin "SHALL be handled by the ordinary pipeline". The framework ends it with a bare `204` and no `Access-Control-*` header. Sending it down the ordinary pipeline instead would forward an `OPTIONS` carrying a valid bearer token to a service on any route without a method restriction (`/api/branches/**`, ...) and spend a rate-limit permit on routes that have one, which Req 5.3 itself forbids. Suggest rewording to "SHALL be answered by the gateway with `204` and no `Access-Control-*` header". The outcome (browser blocks it, not forwarded, no permit) is the one the requirement lists.
3. **Req 1.3 "blank".** Per the owner's decision a blank entry is dropped, not rejected; the requirement's rejected list should lose the word "blank" (or say "non-blank").
4. **`Vary: Origin` on non-CORS responses.** A response to a disallowed origin or to no `Origin` carries no `Vary: Origin` (Req 4.3 forbids it for no `Origin`). A shared cache could in theory store such a response and serve it to an allowed origin; the API's authenticated, per-user responses are not cacheable by shared caches, so this is accepted.
