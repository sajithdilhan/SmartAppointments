# Gateway CORS — Tasks

> Each task leaves `dotnet build SmartAppointments.slnx` at 0 warnings and `dotnet test SmartAppointments.slnx` green. Do them in order. The design is final; its open questions were settled by the project owner and folded into the requirements.
>
> Check a box only when the code exists and the tests pass, in the same commit as the code.
>
> Implementation happens on a branch based on `unified-error-shape`: error bodies are camelCase `{status, detail}` (`ProblemDetailsWriter` uses `JsonSerializerOptions.Web`). Test expectations on a problem body (the `500` kept-header test) use `status` and `detail`, not `Status` and `Detail`. All new code is in the gateway project and `tests/Gateway.Tests`; no service and no `BuildingBlocks` project changes.

## Chunk A — Origins and policy

- [x] 1. `CorsOriginsValidator` and the shipped configuration
  - `Configuration/CorsOriginsValidator.Validate(IConfiguration)` returning `IReadOnlyList<string>`: skip null, empty and whitespace entries, collapse ordinal duplicates, reject per the design's rule table (wildcard, not absolute, scheme, user information, query or fragment, non-canonical form including any path, trailing slash, upper case, default port), message naming `Cors:AllowedOrigins:{index}` and `Cors__AllowedOrigins__{index}`
  - `"Cors": { "AllowedOrigins": [] }` in `appsettings.json`; `http://localhost:4200` and `http://localhost:8081` in `appsettings.Development.json`
  - `CorsOriginsValidatorTests` (accepted: with and without port, `http` and `https`, IPv6; empty and absent list; blank and whitespace dropped; duplicates collapsed; every rejected form; index in the message) and `CorsConfigurationTests` (empty in `appsettings.json`, exactly the two origins in Development, both pass the validator)
  - _Requirements: 1.1, 1.2, 1.3, 1.5, 1.6, 7.1, 7.2_

- [x] 2. `GatewayCors`: the fixed policy
  - `Cors/GatewayCors` with `PolicyName`, the named constants for methods, request headers, exposed headers and max-age, `BuildPolicy` (ordinal `SetIsOriginAllowed`, no credentials, no wildcard API), `AddGatewayCors` and `UseGatewayCors`
  - `GatewayCorsPolicyTests` through `ICorsPolicyProvider` (methods, headers, exposed headers, max-age, no credentials, no any-origin/method/header, exact-match cases `https://localhost:4200`, `http://localhost:4201`, `http://localhost:4200.evil.example`, upper case, `null`, `*`, empty)
  - _Requirements: 1.4, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 5.4, 7.3_

## Chunk B — The pipeline

- [ ] 3. Downstream header guard
  - `Cors/DownstreamCorsHeaderTransform.Apply(HttpContext, HttpResponseMessage?, ICorsService, CorsPolicy)`: remove the `Access-Control-*` names the proxied response carried from the outgoing response, then re-apply the policy when the request's origin is allowed; no-op when the proxied response has none
  - `DownstreamCorsHeaderTransformTests` (service `Allow-Origin: *` and `Allow-Credentials: true` removed; allowed origin keeps only the policy's headers, each once; other origin keeps none; no service header leaves the response unchanged)
  - _Requirements: 2.2, 4.2, 4.3, 5.1_

- [ ] 4. Wire CORS into `Program.cs`
  - `var corsOrigins = CorsOriginsValidator.Validate(builder.Configuration);` right after `ReverseProxyValidator.Validate`
  - When `corsOrigins.Count > 0`: `AddGatewayCors`, the YARP `AddTransforms` response transform calling `DownstreamCorsHeaderTransform`, and `app.UseGatewayCors()` directly after `LoggingMiddleware` and before `UseHttpsRedirection` (comment why: before everything that can answer, after correlation id and exception handling); an empty list changes nothing
  - No route, policy or service change; the existing nine-route test stays green
  - _Requirements: 1.2, 3.2, 3.3, 3.6, 4.1, 4.4, 5.2, 6.1, 6.2, 6.3, 6.4_

- [ ] 5. In-process middleware tests
  - `GatewayCorsMiddlewareTests`: the framework `CorsMiddleware` built from `AddGatewayCors` over a `DefaultHttpContext` and a recording `next`
  - Preflight from an allowed origin on a routed, an unrouted and an `/internal/` path: `204`, exact headers, `Vary: Origin`, no `Allow-Credentials`, `next` not called; preflight asking `PATCH` or `X-Evil`: `204` with lists that omit it, `next` not called; preflight from `https://evil.example` and `null`: `204`, no `Access-Control-*`, `next` not called; `OPTIONS` without `Origin` or without `Access-Control-Request-Method`: `next` called; actual request allowed origin: headers and `Vary`, `next` called; no `Origin`: no header and no `Vary`; other origin: no header, `next` called
  - `ExceptionMiddleware` then CORS then a throwing `next`: `500` problem body (`status`, `detail`, camelCase) that still carries `Access-Control-Allow-Origin`; a `next` setting `401`, `404`, `429` keeps the headers
  - _Requirements: 2.1, 2.2, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.3, 5.1, 5.2, 5.3, 5.4, 6.2, 6.4_

## Chunk C — Configuration, manual checks and documentation

- [ ] 6. Docker Compose and `.env.example`
  - `docker-compose.yml`, `gateway.environment`: `Cors__AllowedOrigins__0: ${WEB_ORIGIN:-}`
  - `.env.example`: `WEB_ORIGIN=http://localhost:8081` with a comment (an origin, no trailing slash)
  - `docker compose config` still renders with `WEB_ORIGIN` unset
  - _Requirements: 1.5_

- [ ] 7. `SmartAppointments.Gateway.http`: the CORS section
  - `@WebOrigin = http://localhost:4200` and the requests of the design's manual-check table, each with its expected result in a comment (preflights on routed, anonymous, unrouted and `/internal/` paths; `PATCH` and `x-evil`; disallowed and `null` origin; `OPTIONS` with no `Access-Control-Request-Method` giving `401`; `401`, `404`, `429`, `200`, `502` with an allowed origin; disallowed origin with a token)
  - _Requirements: 3.1, 3.2, 3.4, 3.5, 4.1, 5.1, 5.3, 5.4, 6.2, 7.4_

- [ ] 8. Manual verification against the running system
  - Run Auth, Availability, Booking and the gateway in Development and replay the `.http` CORS section; check each expected result of the design's table, including the correlation id and a log line on a preflight, that nothing reaches Availability for an `/internal/` preflight, and that ten preflights to `/api/auth/login` leave all five logins available
  - Startup failures: `Cors__AllowedOrigins__0` set to `*`, `https://app.example.com/` and `localhost:4200` each stop the gateway naming the key; an empty list (non-Development) adds no `Access-Control-*` header
  - `docker compose up --build` with `WEB_ORIGIN` unset and set
  - Record what was run and what was not at the top of this file, as in `api-gateway/tasks.md`
  - _Requirements: 1.2, 1.3, 1.5, 3.1, 3.2, 3.3, 3.6, 4.1, 4.4, 5.1, 5.3, 6.2, 7.4_

- [ ] 9. Documentation
  - `CLAUDE.md`: the gateway bullet (CORS from `Cors:AllowedOrigins`, exact origins, preflight answered before auth and rate limits, no credentials, the pipeline position) and the "Local settings and secrets" section (`Cors:AllowedOrigins` empty in `appsettings.json`, the two origins in Development, `Cors__AllowedOrigins__<index>`, compose `WEB_ORIGIN`)
  - `docs/specs/README.md`: an index row for `gateway-cors` (Implemented, no FR-ID, links to the three documents)
  - `docs/specs/api-gateway/requirements.md`: the "Out of scope" CORS line now points to this spec
  - _Requirements: 1.1, 1.5, 6.4_
