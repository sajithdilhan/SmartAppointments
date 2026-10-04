# API gateway — Requirements

## Introduction

Clients should talk to one address. The API Gateway is that address: it routes the public `/api/` paths to Auth, Availability and Booking, rejects requests that carry no valid token before they reach a service, limits how fast one caller can hit the expensive endpoints, gives every request a correlation id that follows it through every service, and reports whether the platform is up. It is a reverse proxy built on YARP, not a place for business logic: role checks, validation and data rules stay in the services.

Today `src/ApiGateway/SmartAppointments.Gateway/` is the bare minimal-API template. This spec turns it into the edge of the system. It also adds one small shared piece to `SmartAppointments.BuildingBlocks.Web`, the correlation-id middleware, and wires it into Auth, Availability and Booking, because a correlation id is only useful if every hop honours it.

The spec refines the "API Gateway" row of section 6 of [`docs/requirements.md`](../../requirements.md) ("single entry point, routing, auth validation, rate limiting, correlation ID forwarding"), the architecture diagram in section 5, the rate-limiting table in section 11.5 and the correlation-ID line of the observability non-functional requirements (sections 10 and 15). It has **no FR-ID of its own**: the BRD lists the gateway as a component, not as a functional requirement. It builds on [`shared-web-infrastructure`](../shared-web-infrastructure/requirements.md) (token validation, `ExceptionMiddleware`, `LoggingMiddleware`, the problem body) and on [`booking-appointments`](../booking-appointments/requirements.md), whose `/internal/` endpoints and API-key scheme this spec must keep unreachable from outside.

Services keep validating the tokens they receive. The gateway's check is an early, cheap filter, not a replacement for theirs (defence in depth).

## Requirements

### Requirement 1: Routing the public API (no FR-ID)

**User Story:** As a client developer, I want to call one host for the whole API, so that I do not need to know which service owns which path.

#### Acceptance Criteria

1. WHEN a request arrives at the gateway for a path under one of the following prefixes, THEN the system SHALL forward it, with its method, path, query string, body and headers, to the owning service and return the service's response unchanged:

   | Public path prefix | Service |
   |---|---|
   | `/api/auth/**` | Auth |
   | `/api/branches/**` | Availability |
   | `/api/services/**` | Availability |
   | `/api/slots/**` | Availability |
   | `/api/appointments/**` | Booking |

   Path matching SHALL be case-insensitive, so `/api/auth/login` and `/api/Auth/login` reach the same endpoint. The path SHALL be forwarded exactly as the client sent it; the gateway SHALL NOT rewrite it.
2. WHEN a request is forwarded, THEN the `Authorization` header SHALL be forwarded unchanged so that the service validates the token again.
3. IF a request's path matches none of the routes in criterion 1, THEN the system SHALL answer `404 Not Found` itself and SHALL NOT forward it. This applies in particular to every path under `/internal/`, to a service's own `/healthz`, `/openapi/**` and `/scalar/**`, and to the paths of services that do not exist yet (for example `/api/queue/**`). The one exception is the Development-only documentation of Requirement 10, which the gateway itself serves under `/openapi/{service}/v1.json` and `/scalar`; outside Development those paths are `404` too.
4. The gateway SHALL NOT contain any route whose path pattern begins with `/internal`, and no route SHALL be a catch-all for `/{**}`. A test SHALL assert both against the shipped configuration.
5. WHEN a request would be forwarded and the route's method list or path is more specific than another route on the same prefix (the cases of Requirements 2.1 and 3), THEN the more specific route SHALL win, whatever the order in the configuration file.
6. The routes (path, methods, authorization, rate-limit policy) and the clusters (destination addresses) SHALL be declared in the `ReverseProxy` section of `appsettings.json`, in YARP's own configuration schema. The destination addresses SHALL be empty in `appsettings.json` and filled in `appsettings.Development.json` with `http://localhost:5008` (Auth), `http://localhost:5202` (Availability) and `http://localhost:5310` (Booking), so that a later Docker Compose file can override them with environment variables (`ReverseProxy__Clusters__<cluster>__Destinations__<destination>__Address`) without touching code.
7. The gateway SHALL keep listening on `http://localhost:5290` and `https://localhost:7176`.
8. The gateway SHALL NOT retry a forwarded request, SHALL NOT load-balance (each cluster has exactly one destination) and SHALL NOT cache responses.
9. WHEN the gateway answers an unmatched request with `404`, THEN it SHALL still apply Requirement 5 (correlation id) and Requirement 6.1 (logging); it SHALL NOT require a token to say `404`.

### Requirement 2: Authentication at the edge (no FR-ID)

**User Story:** As the platform owner, I want requests without a valid token to be turned away at the edge, so that anonymous traffic never reaches the services.

#### Acceptance Criteria

1. WHEN a request to `POST /api/auth/register` or `POST /api/auth/login` arrives, THEN the system SHALL forward it without requiring a token. These two routes SHALL be the only anonymous routes.
2. WHEN a request to any other routed path carries no token, an expired token, a token with a wrong signature, issuer or audience, or a malformed token, THEN the system SHALL answer `401 Unauthorized` and SHALL NOT forward it.
3. WHEN a request to any other routed path carries a valid token (any role, including a role no service recognises), THEN the system SHALL forward it. The gateway SHALL NOT make any role decision: `403` for a wrong role is produced by the service that owns the endpoint.
4. The gateway SHALL validate tokens with `AddJwtAuthentication(configuration)` from `BuildingBlocks.Web`, with the same `Jwt:Issuer`, `Jwt:Audience` and `Jwt:SecretKey` as Auth. As in the services, `ClockSkew` is zero and `MapInboundClaims` is off.
5. WHEN the gateway starts, THEN it SHALL fail with an explanatory message IF `Jwt:SecretKey` is missing, blank or shorter than 32 bytes, or `Jwt:Issuer` or `Jwt:Audience` is blank (the behaviour `AddJwtAuthentication` already has). The key SHALL be empty in `appsettings.json`, Auth's local development key SHALL be in `appsettings.Development.json`, and every other environment SHALL supply it as `Jwt__SecretKey`.
6. Authorization SHALL be declared per route in the `ReverseProxy` configuration: every route SHALL name its policy explicitly (`anonymous` for the two routes of criterion 1, `default` for all the others). In addition, the gateway's fallback authorization policy SHALL require an authenticated user, so a route added later without a policy is closed, not open. A test SHALL assert that every route names a policy and that only `auth-login` and `auth-register` name `anonymous`.
7. The `/healthz` endpoint of Requirement 8 SHALL be reachable without a token.
8. A request to a service through the gateway SHALL carry nothing the client could use to impersonate the gateway: the gateway SHALL NOT add an `X-API-Key` header or any identity header, and a client-supplied `X-API-Key` has no effect because `/internal/` is unreachable (Requirement 1.3) and `/api/` routes ignore it (booking-appointments, Requirement 1.4).

### Requirement 3: Rate limiting (BRD 11.5, no FR-ID)

**User Story:** As the platform owner, I want brute-force and bursty traffic on the sensitive endpoints to be limited at the edge, so that one caller cannot exhaust Auth's password hashing or Availability's slot search.

#### Acceptance Criteria

1. The system SHALL apply these limits, and no others:

   | Endpoint | Limit | Partition |
   |---|---|---|
   | `POST /api/auth/login` | 5 requests per minute | client IP address |
   | `POST /api/appointments` | 10 requests per minute | the authenticated user (`sub` claim) |
   | `GET /api/slots/available` | 30 requests per minute | the authenticated user (`sub` claim) |

   There SHALL be no global limit, and no limit on the join-queue endpoint (the Queue service does not exist yet; BRD 11.5 lists 5 per minute per user for it and that row is out of scope here).
2. WHEN a limit is applied, THEN it SHALL be a sliding window of one minute divided into six segments, enforced in memory by `Microsoft.AspNetCore.RateLimiting`, with no queueing: a request over the limit is rejected at once rather than held.
3. IF a request is over its limit, THEN the system SHALL answer `429 Too Many Requests` with `Content-Type: application/problem+json` and an `ApiProblemDetails` body (`Status` 429, a fixed `Detail`), and a `Retry-After` header holding a whole number of seconds (at least 1). The request SHALL NOT be forwarded.
4. WHEN the partition is a user, THEN its key SHALL be the `sub` claim of the token the gateway validated. Because the routes of the second and third rows of the table in criterion 1 require authentication (Requirement 2.2), a request without a valid token SHALL get `401` before it is counted, and SHALL NOT consume anyone's allowance. IF a validated token has no `sub` claim, THEN the request SHALL be partitioned by client IP address instead.
5. WHEN the partition is the client IP address, THEN the system SHALL use `HttpContext.Connection.RemoteIpAddress` (an IPv4-mapped IPv6 address SHALL be treated as the IPv4 address, so the same client cannot hold two allowances) and SHALL NOT read `X-Forwarded-For`, `Forwarded` or any other client-supplied header. The gateway is the edge; trusting such headers would let a caller choose their own partition. IF the address is unavailable, THEN the request SHALL fall into one shared partition rather than go unlimited.
6. Partitions SHALL be independent: user A reaching the appointment limit SHALL NOT affect user B, and the login limit SHALL NOT affect any other endpoint; the appointment-creation allowance and the slot-search allowance of one user SHALL be separate counters.
7. A request that is rejected by a limit SHALL be logged at `Warning` with the policy name and the correlation id, and SHALL NOT log the partition key if it is a user id or an IP address in clear (it SHALL log the policy only).
8. The limits SHALL be fixed in code (named constants), not configuration, in this spec.
9. Because the counters live in the gateway's memory, each gateway instance SHALL count on its own, and a restart SHALL reset them. This is accepted for a single instance; sharing counters across instances (Redis, BRD "Should have") is out of scope.

### Requirement 4: Rejecting what the gateway cannot forward (no FR-ID)

**User Story:** As a client developer, I want a clear, consistent error when a service is down, so that I can tell a gateway problem from an application error.

#### Acceptance Criteria

1. IF a service cannot be reached (connection refused, DNS failure, connection reset before a response), THEN the system SHALL answer `502 Bad Gateway` with `application/problem+json`, an `ApiProblemDetails` body and a fixed `Detail` that does not name the host, port or exception.
2. IF the service accepts the request but does not answer within the gateway's timeout (30 seconds, `HttpRequest.ActivityTimeout` of the cluster), THEN the system SHALL answer `504 Gateway Timeout` in the same shape.
3. IF the client disconnects before the response, THEN the system SHALL NOT write a response body and SHALL NOT log it as an error.
4. IF the gateway has already started to send the response when the failure happens, THEN the system SHALL NOT attempt to write a second response.
5. WHEN a service itself answers with any status (`4xx`, `5xx`, `503` from Booking's resilience, `429`...), THEN the gateway SHALL pass it through as it is and SHALL NOT replace the body.
6. IF an exception escapes the gateway's own pipeline, THEN `ExceptionMiddleware` SHALL turn it into `500` with `ApiProblemDetails`, as in the services (shared-web-infrastructure, Requirement 3).

### Requirement 5: Correlation id end to end (BRD 10/15, no FR-ID)

**User Story:** As an operator, I want every request to carry one id through the gateway and every service, so that I can find all the log lines of a single call.

#### Acceptance Criteria

1. The system SHALL provide `CorrelationIdMiddleware` in `SmartAppointments.BuildingBlocks.Web`, using the header name `X-Correlation-ID` (a new `Constants.CorrelationIdHeaderName`), and the gateway, Auth, Availability and Booking SHALL all use it.
2. WHEN a request carries exactly one `X-Correlation-ID` value that is well formed (1 to 64 characters, each one of `A-Z`, `a-z`, `0-9`, `-`, `_`, `.`), THEN the middleware SHALL keep it as the request's correlation id.
3. IF the header is absent, empty, longer than 64 characters, contains any other character, or appears more than once, THEN the middleware SHALL discard it and generate a new id (a GUID in the `N` format). It SHALL NOT reject the request.
4. WHEN the correlation id has been decided, THEN the middleware SHALL:
   - store it in `HttpContext.Items` and make it readable with an extension `HttpContext.GetCorrelationId()`;
   - write it back into the **request** header `X-Correlation-ID`, so that the gateway forwards exactly the validated or generated value, never a malformed original;
   - open an `ILogger` scope holding `CorrelationId` around the rest of the pipeline, so that every log line written while the request is processed carries it;
   - set the `X-Correlation-ID` header of the **response** to it, whatever the response is: a normal one, a `401`/`403` from the authentication middleware, a `404`, a `429`, a `502`, and a `500` written by `ExceptionMiddleware`. The response header SHALL contain the id exactly once, even when the response was proxied from a service that echoed the same header.
5. WHEN `HttpContext.TraceIdentifier` is read, THEN it SHALL be unchanged by the middleware.
6. The middleware SHALL run first in each application's pipeline, ahead of `ExceptionMiddleware` and `LoggingMiddleware`.
7. WHEN Booking calls Availability while handling a request, THEN the call SHALL carry the same `X-Correlation-ID` as the incoming request, through a `DelegatingHandler` (`CorrelationIdHandler`, in `BuildingBlocks.Web`, using `IHttpContextAccessor`) on the Availability `HttpClient`, so that the id continues beyond the first hop. IF there is no current request, THEN no header SHALL be added; an existing header on the outgoing message SHALL NOT be overwritten. The id SHALL be the same on every retry of one call.
8. WHEN a service writes a log line while handling a request, THEN the line SHALL include the correlation id. Because the default console logger omits scopes, each application's `appsettings.json` SHALL set `Logging:Console:IncludeScopes` to `true`.
9. The correlation id SHALL NOT be stored in any database, and SHALL NOT be added to the problem body.

### Requirement 6: The gateway behaves like the other services (no FR-ID)

**User Story:** As a developer, I want the gateway to be built and operated the way Auth, Availability and Booking are, so that nothing about it surprises me.

#### Acceptance Criteria

1. The gateway SHALL register `CorrelationIdMiddleware`, `ExceptionMiddleware` and `LoggingMiddleware` from `BuildingBlocks.Web`, in that order, ahead of authentication. The gateway SHALL keep `UseHttpsRedirection()`: TLS terminates at the gateway (Requirement 9).
2. The pipeline order SHALL be: correlation id, exception handling, logging, HTTPS redirection, authentication, authorization, rate limiting, then the endpoints (`/healthz` and the reverse proxy). In particular rate limiting SHALL come after authentication (Requirement 3.4) and the correlation id SHALL come before everything that can write a response (Requirement 5.4).
3. WHEN the gateway starts, THEN it SHALL fail with an explanatory message IF a cluster named `auth`, `availability` or `booking` is missing, has no destination, or has a destination whose `Address` is not an absolute `http` or `https` URL. The message SHALL name the configuration key and its environment-variable form.
4. The gateway SHALL NOT take a reference on any `*.Domain`, `*.Application` or `*.Infrastructure` project of a service, nor on a service's database.
5. The gateway SHALL publish OpenAPI documents and a Scalar UI only as Requirement 10 says, in Development; the services' own `/openapi` and `/scalar` paths are never routed (Requirement 1.3).
6. The gateway SHALL add no project to `src/Shared/` beyond what Requirement 5 puts in `BuildingBlocks.Web`.

### Requirement 7: A tested gateway (no FR-ID)

**User Story:** As a developer, I want the routing table, the limits and the health checks to be covered by tests, so that a change to the configuration cannot silently open `/internal/` or drop a limit.

#### Acceptance Criteria

1. The system SHALL provide `tests/Gateway.Tests` (xUnit and Moq, unit tests only, like the other test projects), added to `SmartAppointments.slnx`.
2. The tests SHALL load the gateway's real `appsettings.json` and assert Requirement 1.1 (each of the nine expected routes exists with the expected path, methods and cluster), 1.4 (no `/internal` route, no root catch-all), 2.1 and 2.6 (anonymous only on login and register; every route names a policy), 3.1 (the limiter policy named on each of the three limited routes) and 6.3 (the three clusters exist, with an empty address in `appsettings.json` and the development address in `appsettings.Development.json`).
3. Integration tests that start the gateway and real services (`WebApplicationFactory`, Testcontainers) are out of scope; the end-to-end behaviour is verified by hand in the last task, against four running processes.

### Requirement 8: Aggregated health (no FR-ID)

**User Story:** As an operator, I want one URL that tells me whether the gateway and the services behind it are up, so that I do not have to probe four ports.

#### Acceptance Criteria

1. WHEN a client GETs the gateway's `/healthz`, THEN the system SHALL run these checks: `gateway` (always `Healthy`: the process is up and answering) and one check each for `auth`, `availability` and `booking`, which GETs `<destination address>/healthz` of that service's cluster with a 2-second timeout.
2. WHEN a downstream check receives a `2xx` response, THEN that check SHALL be `Healthy`. WHEN it receives any other status, times out, or cannot connect, THEN that check SHALL be `Degraded`, never `Unhealthy`, and its description SHALL say which (for example "HTTP 503", "timed out", "unreachable") without echoing an exception message, host or port.
3. WHEN the checks have run, THEN the overall status SHALL be the worst of them: `Healthy` when all are healthy, `Degraded` when at least one downstream is not, and `Unhealthy` only if the gateway's own check were to fail.
4. The HTTP status SHALL be `200 OK` for `Healthy`, `200 OK` for `Degraded` and `503 Service Unavailable` for `Unhealthy`.
5. The body SHALL be `application/json` of the form `{ "status": "Degraded", "totalDurationMs": 2013, "checks": [ { "name": "auth", "status": "Healthy", "description": null, "durationMs": 12 }, ... ] }` listing each check, with statuses written as the strings `Healthy`, `Degraded` and `Unhealthy`.
6. The endpoint SHALL be anonymous (Requirement 2.7), SHALL NOT be rate limited, SHALL NOT be a YARP route, and SHALL NOT forward the caller's `Authorization` header to the services. A service's `/healthz` is called directly by the check, never through the proxy.
7. The three downstream checks SHALL run in parallel, so the endpoint answers within about the timeout of one check, not three.
8. YARP's own active and passive health checking SHALL stay off in this spec; routing to a down service answers `502` (Requirement 4.1) rather than being steered around.
9. The check SHALL read each address from the same `ReverseProxy` cluster configuration that routing uses (one source of truth).

### Requirement 9: TLS terminates at the gateway (no FR-ID)

**User Story:** As a developer, I want the services to answer plain `http` calls from the gateway and from Booking, so that a proxied request is never answered with a redirect.

#### Acceptance Criteria

1. The system SHALL remove `UseHttpsRedirection()` from the pipelines of Auth, Availability and Booking. The gateway SHALL keep it.
2. The gateway's destinations and Booking's `Services:Availability:BaseUrl` SHALL be `http://` addresses; a request to any of them SHALL NOT be answered with a `307`/`308`, whichever launch profile the service was started with.
3. The services' `launchSettings.json` and ports SHALL NOT change.
4. `CLAUDE.md` SHALL say that the services do not redirect to HTTPS and that TLS is the gateway's job.

### Requirement 10: Aggregated API documentation, Development only (no FR-ID)

**User Story:** As a developer, I want one Scalar page at the gateway that lists the Auth, Availability and Booking APIs and lets me try them, so that I explore the system as a client sees it.

#### Acceptance Criteria

1. WHEN the gateway runs in the `Development` environment, THEN `GET /openapi/auth/v1.json`, `/openapi/availability/v1.json` and `/openapi/booking/v1.json` SHALL return the OpenAPI document of the Auth, Availability and Booking service respectively (the service's own `/openapi/v1.json`), through the gateway's reverse proxy, without a token.
2. WHEN the gateway runs in `Development`, THEN `GET /scalar` SHALL serve one Scalar UI (`Scalar.AspNetCore`, the same package version as the services) listing those three documents, without a token.
3. WHEN a request is sent from the Scalar UI with "Try it", THEN it SHALL go to the gateway's own base URL, never to a service's address, so that it passes through the gateway's authentication, rate limits and correlation id. To that end the `servers` entry of each served document SHALL be the gateway's scheme, host and port.
4. WHEN a bearer token is entered in the UI, THEN it SHALL be sent as `Authorization: Bearer ...` (the services' documents already declare the bearer scheme through `AddOpenApiWithBearerAuth`).
5. IF the gateway runs in any other environment, THEN `/scalar` and `/openapi/**` SHALL return `404`, and no OpenAPI route SHALL exist in the proxy configuration.
6. The OpenAPI routes SHALL be declared only in `appsettings.Development.json`; the shipped `appsettings.json` SHALL contain none. A test SHALL assert that no route of `appsettings.json` has a path containing `openapi` or `scalar`, and that the Development file's three routes are anonymous, `GET`-only, and map to the clusters `auth`, `availability`, `booking`.
7. The documentation SHALL NOT make a service's own `/scalar` or `/openapi/v1.json` reachable at any other path, and SHALL NOT route `/internal/` (Requirement 1.4). If Availability's document lists its `/internal/` endpoints, that is a property of the service's own document: "Try it" on them returns `404` at the gateway.

## Discrepancies with the BRD

| BRD | This spec | Decision |
|---|---|---|
| Section 6: the gateway does "auth validation". | It validates the token's signature, issuer, audience and lifetime and requires *an* authenticated user; it makes no role decision (Requirement 2.3). | Roles are owned by the services and already enforced there (booking-appointments Requirement 5.10). Duplicating them at the edge would mean two places to change. |
| Section 11.5: "Join queue: 5 requests per minute per user". | Not applied (Requirement 3.1). | The Queue service and its routes do not exist. The row is added with the Queue spec. |
| Section 11.5 does not say how the limiter is shared across instances; section 15 lists Redis rate limiting. | In-memory counters (Requirement 3.9). | Decided by the project owner; Redis is a later "Should have". |
| Section 13 lists `/internal/slots/{slotId}/reserve` and `release` next to the public endpoints. | The gateway never routes `/internal/` (Requirement 1.3). | Reserve and release are service-to-service calls authenticated by an API key; exposing them would defeat that (booking-appointments Requirement 1). |
| Section 13 writes `/api/auth/...` in lower case; the controllers are `[Route("api/[controller]")]`, so Swagger/Scalar documents `/api/Auth`. | Routing is case-insensitive and the path is forwarded as sent (Requirement 1.1). | Both spellings work; no rewriting. |
| Section 5 shows a "Client/Swagger" box in front of the gateway. | A Development-only Scalar UI lists the services' documents (Requirement 10). | Decided by the project owner. |
| Section 10 asks for Serilog and OpenTelemetry along with correlation ids. | Only the correlation id and console log scopes are added. | Structured logging and tracing are separate work. |

## Out of scope

- **Rate limit for joining the queue**, and any Queue routes. Added with the Queue spec.
- **Shared rate-limit counters** across gateway instances (Redis).
- **Load balancing, retries, caching, circuit breaking and YARP health checks at the gateway.** One destination per cluster; Booking already retries and breaks circuits on its own call to Availability.
- **Trusting `X-Forwarded-For`** or any forwarded-headers handling. The gateway is the edge. If a load balancer is put in front of it later, that is a deliberate change with a trusted-proxy list.
- **CORS** for browser clients: specified and built in [`gateway-cors`](../gateway-cors/requirements.md). `Constants.AllowedOriginsPolicy` is a role policy, not CORS.
- **A single merged OpenAPI document** for all services, and any documentation outside Development. The gateway's Scalar UI lists the three services' documents side by side (Requirement 10).
- **TLS to the services.** Destinations are `http://`; TLS terminates at the gateway (Requirement 9).
- **Docker Compose**, container images and reverse-proxy deployment. The configuration is shaped to allow environment overrides (Requirement 1.6) and nothing more.
- **Integration tests** (`WebApplicationFactory`, Testcontainers). See Requirement 7.3.
- **Structured logging (Serilog), tracing (OpenTelemetry)** and any sink for correlation ids beyond the console.
- **Request size limits, request logging of bodies and response compression** at the gateway.
