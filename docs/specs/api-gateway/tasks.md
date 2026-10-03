# API gateway — Tasks

> Each task leaves `dotnet build SmartAppointments.slnx` at 0 warnings and `dotnet test SmartAppointments.slnx` green. Tasks are grouped into chunks (A-E) that can be handed to an implementer one at a time; do them in order. The design's *Decisions taken* section is final. The open points were settled by the project owner and are folded into the design.
>
> Check a box only when the code exists and the tests pass, in the same commit as the code.
>
> All eleven tasks are done. Besides the unit tests (BuildingBlocks 71, Availability 297, Booking 96, Auth 81, Gateway 71, all green), the gateway was verified end to end with Auth, Availability, Booking and the gateway running in Development (the `http` launch profiles; the services no longer redirect to HTTPS).
>
> Routing: the whole flow went through `http://localhost:5290` only: admin login, create branch (`201`, `Location` `http://localhost:5290/api/Branches/{id}`), set schedule `200`, create service type, generate slots, customer slot search `200`, customer booking `201` with `Location` `http://localhost:5290/api/appointments/{id}`, get `200`, cancel `204`. Correlation id: a supplied `X-Correlation-ID` was echoed once on the response and appeared as `CorrelationId:<id>` in the console logs of the gateway, Booking and Availability (Availability through the forwarded header on the internal reserve call); a malformed value (`bad value!`) was replaced with a generated id; `401`, `404` and `502` responses carried the header too. Not exercised: `429` and a forced `500` responses carrying it, and the 65-character boundary.
>
> Edge auth: no token on `/api/branches` returned `401` from the gateway with `WWW-Authenticate: Bearer`; a tampered token `401`; register and login anonymous succeeded; a customer on `POST /api/branches` got the service's `403` passed through. Not exercised: an expired token, a token signed with another key, and checking the services' logs for the absence of the request. Not routed: `/internal/slots/{id}` (even with the API key), a service's `/healthz`, `/openapi/v1.json` and `/api/queue/x` all returned `404` with no token needed; this is `UnmatchedRequestMiddleware`, because in .NET 10 the fallback authorization policy also applies to requests with no endpoint. A service's own `/scalar` and the redirect check on the services' `http` ports under both launch profiles were not run separately.
>
> Rate limits: the 6th login within a minute from one address returned `429` `application/problem+json` (`{"Status":429,...}`) with `Retry-After: 60`; forged `X-Forwarded-For` values did not create new allowances; the 11th `POST /api/appointments` by one user returned `429` and a second user was unaffected; the 31st `GET /api/slots/available` returned `429` and a second user was unaffected. `Retry-After` is always 60: the .NET 10 sliding-window limiter supplies no `RetryAfter` metadata, so the handler uses the window length. Not re-tested: the window freeing up again.
>
> Health: with all services up `/healthz` returned `200` `Healthy` with `gateway`, `auth`, `availability` and `booking` entries; with Availability stopped `200` `Degraded`, availability reported as "timed out" after about 2 s (a killed local service on Windows shows as a timeout rather than "unreachable"), the others `Healthy`. Proxy errors: with Availability stopped a routed `GET /api/branches` returned `502` problem+json "A downstream service is unavailable." Not exercised live: the `504` path (a service that accepts but does not answer within 30 s), covered by a unit test; stopping Auth and Booking one at a time.
>
> Docs: `/openapi/{auth,availability,booking}/v1.json` through the gateway were served anonymously, each with `servers` `[http://localhost:5290/]` (no Scalar-side override was needed), and `/scalar/` returned `200`. The Scalar UI was not driven in a browser, so "Try it" through the UI is unverified (the `servers` value is what makes it target the gateway). The Production-environment `404` for the docs routes was not run live; it is covered by the route-config tests (`appsettings.json` has no docs routes, `MapGatewayDocs` is called only under `IsDevelopment()`).

## Chunk A — Correlation id in the shared library and the services

- [x] 1. `CorrelationIdMiddleware` in `BuildingBlocks.Web`
  - `Constants.CorrelationIdHeaderName = "X-Correlation-ID"` in `BuildingBlocks`
  - `Middlewares/CorrelationIdMiddleware` (validate or generate, `Items`, request header overwrite, `OnStarting` response header with assignment, logger scope) and `GetCorrelationId(this HttpContext)`; hand-written character check, no `Regex`
  - `CorrelationIdMiddlewareTests` in `tests/BuildingBlocks.Tests`: kept/regenerated cases and the 1/64/65 boundaries, request header rewritten, response header exactly once, `500` from a throwing delegate behind `ExceptionMiddleware` still carries it, scope seen by a recording logger, `TraceIdentifier` untouched
  - _Requirements: 5.1–5.6_

- [x] 2. Wire it into Auth, Availability and Booking
  - `app.UseMiddleware<CorrelationIdMiddleware>();` directly above `ExceptionMiddleware` in the three `Program.cs`
  - `"Logging": { "Console": { "IncludeScopes": true } }` in the three services' `appsettings.json`
  - Existing tests stay green; nothing else changes
  - _Requirements: 5.1, 5.6, 5.8_

- [x] 3. Booking's Availability client forwards the id
  - `CorrelationIdHandler` in `BuildingBlocks.Web` (adds the header from `IHttpContextAccessor`'s request when missing, adds nothing without a context)
  - `Booking.Infrastructure` references `BuildingBlocks.Web`; `AddAvailabilityClient` calls `AddHttpContextAccessor()` and `.AddHttpMessageHandler<CorrelationIdHandler>()` **before** `AddStandardResilienceHandler`
  - `CorrelationIdHandlerTests` in `BuildingBlocks.Tests` (present, absent, not overwritten, same value on two attempts); an addition to `AvailabilityClientTests` in `Booking.Tests`
  - _Requirements: 5.7_

- [x] 3.1 Remove HTTPS redirection from the services
  - Delete `app.UseHttpsRedirection();` from the `Program.cs` of Auth, Availability and Booking, with a comment that TLS terminates at the gateway; launch settings and ports unchanged; Booking's `Services:Availability:BaseUrl` stays `http://localhost:5202`
  - _Requirements: 9.1–9.3_

## Chunk B — The gateway: YARP, JWT, pipeline, 502, tests project

- [x] 4. `ProblemDetailsWriter`
  - `Results/ProblemDetailsWriter.WriteAsync(HttpContext, int status, string detail)` in `BuildingBlocks.Web`, serializing `ApiProblemDetails` exactly as `ExceptionMiddleware` does; `ExceptionMiddleware` calls it (no behaviour change)
  - `ProblemDetailsWriterTests`; `ExceptionMiddlewareTests` pass unchanged
  - _Requirements: 4.1, 4.6_

- [x] 5. Gateway project: YARP, JWT and pipeline
  - `SmartAppointments.Gateway.csproj`: `Yarp.ReverseProxy` (latest stable for `net10.0`), project reference to `BuildingBlocks.Web`
  - `appsettings.json`: `Jwt` (issuer, audience, empty secret), the nine routes and three clusters of the design **without** any `RateLimiterPolicy` yet, empty addresses, `Logging:Console:IncludeScopes`; `appsettings.Development.json`: Auth's `Jwt:SecretKey` and the three addresses `http://localhost:5008`, `5202`, `5310`
  - `Configuration/ReverseProxyValidator` and its call before `AddReverseProxy`
  - `Program.cs` as in the design: `AddJwtAuthentication`, fallback policy requiring an authenticated user (no `AddAuthorizationWithRoles`), `AddReverseProxy().LoadFromConfig`, the middleware order correlation id, exception, logging, HTTPS redirection, authentication, authorization, `MapReverseProxy`; remove the template `MapGet("/")`
  - `launchSettings.json` ports unchanged
  - _Requirements: 1.1–1.9, 2.1–2.6, 2.8, 5.1, 5.6, 6.1–6.4, 6.6_

- [x] 6. `ProxyErrorMiddleware`: `502` and `504`
  - First confirm that `IForwarderErrorFeature` and a custom step in the `MapReverseProxy` pipeline behave as designed; adjust the mechanism, not the requirement, if the API differs
  - `Proxy/ProxyErrorMiddleware` using `ProblemDetailsWriter`; registered in `MapReverseProxy(...)`; the cluster `HttpRequest.ActivityTimeout` of 30 s is already in the configuration
  - _Requirements: 4.1–4.5_

- [x] 7. `tests/Gateway.Tests`
  - New xUnit + Moq project mirroring `BuildingBlocks.Tests` (same package versions), referencing the Gateway project; the gateway's `appsettings.json` and `appsettings.Development.json` copied to the test output as linked content; added to `SmartAppointments.slnx`
  - `RoutingConfigurationTests` (nine routes, no `/internal`, no root catch-all, every route names a policy, anonymous only on login and register, clusters and addresses), `ReverseProxyValidatorTests`, `ProxyErrorMiddlewareTests`
  - _Requirements: 1.1, 1.4, 1.6, 2.1, 2.6, 4.1–4.5, 6.3, 7.1, 7.2_

- [x] 7.1 Development-only aggregated API docs
  - `Scalar.AspNetCore` in the gateway csproj at the same version as the services; `Docs/GatewayDocsExtensions.MapGatewayDocs` (documents `auth`, `availability`, `booking` at `/openapi/{service}/v1.json`, the services' title and theme, bearer preferred, `.AllowAnonymous()`), called from `Program.cs` only under `IsDevelopment()`; confirm the multi-document call names in the installed package
  - `appsettings.Development.json`: routes `docs-auth`, `docs-availability`, `docs-booking` (anonymous, `GET`, `PathSet` `/openapi/v1.json`, `RequestHeaderOriginalHost: true`); nothing in `appsettings.json`
  - Run the gateway and the three services in Development and check the `servers` each served document carries. If it is not the gateway's address, apply the Scalar-side server override of the design and note it in the header of this file
  - `launchSettings.json`: `launchUrl` `scalar` in both profiles (`launchBrowser` stays `true`)
  - `DocsRoutingTests` in `Gateway.Tests`; adjust `RoutingConfigurationTests` so that `appsettings.json` alone has the nine routes and no `openapi`/`scalar` route, and the merged Development configuration has twelve
  - _Requirements: 1.3, 6.5, 10.1–10.7_

## Chunk C — Rate limiting

- [x] 8. Limiter policies and rejection
  - `RateLimiting/RateLimitPolicies` (constants, `ByClientIp`, `ByUser`, `AddGatewayRateLimiting`: three named sliding-window policies of 1 minute and 6 segments, `QueueLimit = 0`, `OnRejected` with `429`, `Retry-After`, `ProblemDetailsWriter` body and a `Warning` log of the policy name only); no global limiter
  - `Program.cs`: `AddGatewayRateLimiting()` and `UseRateLimiter()` after `UseAuthorization()`
  - `appsettings.json`: `RateLimiterPolicy` of `login`, `appointment-create` and `slot-search` on `auth-login`, `appointments-create` and `slots-search`
  - `RateLimitPoliciesTests`, `RateLimitRejectionTests`; extend `RoutingConfigurationTests` (the three limited routes name the right policy, no other does)
  - _Requirements: 3.1–3.9, 6.2_

## Chunk D — Aggregated health

- [x] 9. Downstream health checks and `/healthz`
  - `Health/DownstreamHealthCheck` (2 s timeout, non-2xx, timeout and unreachable → `context.Registration.FailureStatus`, descriptions "HTTP n", "timed out", "unreachable"), `AddGatewayHealthChecks(configuration)` (the `gateway` check plus `auth`, `availability`, `booking` with `failureStatus: Degraded`, addresses read from the cluster configuration, a `"health"` named `HttpClient` with a 2 s timeout), `Health/HealthResponseWriter`
  - `Program.cs`: `MapHealthChecks("/healthz", ...)` with `ResultStatusCodes` 200/200/503, the writer, and `.AllowAnonymous()`
  - `DownstreamHealthCheckTests` with a fake `HttpMessageHandler`, `HealthEndpointTests` (aggregate status, status-code mapping, JSON shape, timing)
  - _Requirements: 2.7, 8.1–8.9_

## Chunk E — End-to-end check and documentation

- [x] 10. Manual end-to-end verification
  - Start local PostgreSQL, then Auth, Availability and Booking (any launch profile: the services no longer redirect) and the gateway on 5290. Use the gateway's `.http` file (task 11) or curl. Record the outcome in this file's header, as `booking-appointments/tasks.md` does
  - Routing: register, login, then branches, services, slot search, create, get and cancel an appointment, all through `:5290`, with the same results as direct calls
  - `401` at the edge: no token, an expired or tampered token and a token signed with another key, on a protected path; the service's logs show **no** request arrived. Register and login anonymous succeed
  - Roles: a customer token on `POST /api/branches` returns the service's `403`, proving roles are still the service's decision
  - `/internal/slots/{id}` (with and without a valid token and an `X-API-Key`), a service's `/healthz`, a service's own `/openapi/v1.json` and `/scalar`, and `/api/queue/x` all return `404` from the gateway and reach no service. With the gateway started as `Production` (secret and addresses from environment variables), `/scalar` and `/openapi/auth/v1.json` are `404` too. Calling Auth, Availability and Booking on their `http` ports never returns a `307`/`308`, under either launch profile
  - Rate limits: 6th `POST /api/auth/login` within a minute returns `429` with `Retry-After` (always 60: the .NET 10 sliding-window limiter gives no `RetryAfter` metadata) and an `ApiProblemDetails` body, the first five do not; 11th `POST /api/appointments` for one user returns `429` (use distinct `Idempotency-Key`s); 31st `GET /api/slots/available` returns `429`; a second user is unaffected; the window frees up again; login attempts with a forged `X-Forwarded-For` still share one allowance
  - Correlation id: a request without the header returns one; a good value is echoed unchanged; a malformed one (65 characters, a space) is replaced; the response header holds one value; the same id appears in the console of the gateway, the owning service and, for a create appointment, in Availability's log lines for the reserve call (Booking forwards it); `401`, `404`, `429` and a forced `500` responses carry it
  - Docs (Development): `/scalar` at the gateway lists Auth, Availability and Booking; the three `/openapi/{service}/v1.json` return the services' documents without a token and their `servers` is the gateway; in the UI, enter a token from login and call `GET /api/branches` with "Try it": the request appears in the gateway's log with a correlation id, and a call with no token returns the gateway's `401`
  - Health: `/healthz` is `Healthy` (200) with all up; stop one service at a time and see `Degraded` (200) naming it, answering in about 2 s (a killed local service shows as "timed out" on Windows, not "unreachable"); with a service stopped, a routed call returns `502` problem+json; hold a service (a breakpoint or a sleeping endpoint) beyond 30 s and see `504`; the gateway's own check never fails
  - _Requirements: 1–5, 8 (all behaviour not covered by unit tests, Requirement 7.3)_

- [x] 11. Documentation and the `.http` file
  - Create `src/ApiGateway/SmartAppointments.Gateway/SmartAppointments.Gateway.http` (none exists): the requests of task 10 against `@Gateway_HostAddress = http://localhost:5290`
  - `CLAUDE.md`: replace the "API Gateway — stub only" bullet with the real description (YARP, edge JWT, rate limits, correlation id, aggregated health, ports); mention `CorrelationIdMiddleware` and `CorrelationIdHandler` in the `BuildingBlocks.Web` bullet; say that the services no longer redirect to HTTPS and that TLS terminates at the gateway; describe the Development-only Scalar page at `http://localhost:5290/scalar`; add the gateway settings to *Local settings and secrets* (`Jwt:SecretKey`, `ReverseProxy` destination addresses and their environment-variable form) and `tests/Gateway.Tests` to the test conventions; note the single-instance rate-limit limitation
  - `docs/specs/README.md`: set the *API gateway* row to `Implemented`; strike "Rate limiting, correlation ids" from the *Out of scope* line of `booking-appointments/requirements.md` only if the project owner wants that spec edited (otherwise leave it)
  - Tick every box in this file and add the verification record to its header
  - _Requirements: 6.5, 7.1, 9.4, 10_
