# Gateway CORS — Requirements

## Introduction

A browser single-page application (the Angular app in the separate `smart-appointments-web` repository) is served from a different origin than the API and will call only the API Gateway. A browser refuses to let such a page read a cross-origin response, or to send a request with `Authorization` or `Idempotency-Key` headers at all, unless the server opts in with CORS. Today nothing in the repository does: the gateway emits no `Access-Control-*` headers, and a preflight `OPTIONS` request would be answered `401` by the authentication middleware or `404` by the unmatched-route middleware. This spec makes the gateway answer CORS for an explicit list of allowed origins, and nothing else.

CORS belongs to the gateway, the only public entry point; the services behind it stay without it. Note that `Constants.AllowedOriginsPolicy` in `BuildingBlocks` is an authorization role policy (Admin or Staff), **not** a CORS policy, and is unrelated to this spec.

The spec refines the "API Gateway" row of section 6 of [`docs/requirements.md`](../../requirements.md) and the "Security" row of the non-functional requirements in section 10. It has **no FR-ID of its own**: the BRD does not mention browser clients. It completes the line in [`api-gateway`](../api-gateway/requirements.md) "Out of scope" that says CORS needs its own spec, and it must leave that spec's guarantees intact (token check at the edge, rate limits, `404` for unrouted paths, `/internal/` never routed). It also relies on the problem-body shape of [`shared-web-infrastructure`](../shared-web-infrastructure/requirements.md); a separate update of its Requirement 4 makes error bodies `{status, detail}` for the front end, and nothing here changes it.

## Requirements

### Requirement 1: Configured allowed origins (no FR-ID)

**User Story:** As an operator, I want the origins allowed to call the API to be listed in configuration, so that I can open the gateway to my front end, and only to it, in each environment without changing code.

#### Acceptance Criteria

1. The system SHALL read the allowed origins from the string array `Cors:AllowedOrigins`. `appsettings.json` SHALL ship it empty (`[]`), and `appsettings.Development.json` SHALL list `http://localhost:4200` (the Angular dev server) and `http://localhost:8081` (the web container).
2. WHERE `Cors:AllowedOrigins` is empty or absent, THEN the system SHALL start normally and SHALL NOT add any `Access-Control-*` header to any response, and SHALL NOT answer a preflight itself (the request is handled as any other, Requirement 4.3 of this spec does not apply).
3. WHEN the gateway starts, THEN it SHALL fail with an explanatory message that names the configuration key and its environment-variable form (`Cors__AllowedOrigins__<index>`), IF an entry is:
   - the wildcard `*`, or any entry containing `*`;
   - not an absolute URL (a blank entry is not an error: it is dropped, criterion 5);
   - using a scheme other than `http` or `https`;
   - carrying a path other than none (this includes a trailing slash, so `https://app.example.com/` is rejected), a query string, a fragment or user information.

   An entry MAY carry a port (`http://localhost:4200`). The validation SHALL run before the application starts serving, in the same way as `ReverseProxyValidator`.
4. The system SHALL compare the request's `Origin` header to the configured entries as an exact, ordinal string match on scheme, host and port. `http://localhost:4200` SHALL NOT match `https://localhost:4200`, `http://localhost:4201` or `http://localhost:4200.evil.example`. Subdomain wildcards and pattern matching SHALL NOT exist.
5. The docker-compose `gateway` service SHALL set `Cors__AllowedOrigins__0` to `${WEB_ORIGIN}`, and `.env.example` SHALL define `WEB_ORIGIN=http://localhost:8081`. WHEN `WEB_ORIGIN` is unset in `.env`, THEN the compose file SHALL fall back to an empty value rather than refuse to start, and the empty entry SHALL be treated as "no origin configured" (criterion 2), not as a validation failure.
6. The allowed origins SHALL be read once at startup; a change to the list takes effect on restart.

### Requirement 2: The CORS policy (no FR-ID)

**User Story:** As a front-end developer, I want the gateway to allow exactly the methods and headers my SPA uses and to expose the response headers it needs to read, so that the browser lets the app work and nothing more.

#### Acceptance Criteria

1. WHEN a request or preflight carries an `Origin` that matches Requirement 1.4, THEN the system SHALL answer with `Access-Control-Allow-Origin` set to that exact origin (never `*`) and `Vary: Origin`.
2. The system SHALL NOT send `Access-Control-Allow-Credentials` in any response. Credentials are off: the SPA authenticates with a bearer token in the `Authorization` header, not with cookies.
3. The policy SHALL allow the methods `GET`, `POST`, `PUT` and `DELETE` (the preflight answer SHALL carry `Access-Control-Allow-Methods` with exactly these; `OPTIONS` is the preflight itself). `PATCH`, `HEAD` and any other method SHALL NOT be listed.
4. The policy SHALL allow the request headers `Authorization`, `Content-Type`, `Idempotency-Key` and `X-Correlation-ID` (the preflight answer SHALL carry `Access-Control-Allow-Headers` with exactly these). Any other request header named in `Access-Control-Request-Headers` SHALL NOT be allowed.
5. The policy SHALL expose the response headers `X-Correlation-ID`, `Retry-After` and `Location` through `Access-Control-Expose-Headers` on every response that carries `Access-Control-Allow-Origin`, so the SPA can log the correlation id, honour a `429`, and follow a `201 Created` location.
6. The preflight answer SHALL carry `Access-Control-Max-Age: 600` (10 minutes).
7. The values of criteria 3 to 6 SHALL be fixed in code (named constants), not configuration, in this spec. A test SHALL assert them.

### Requirement 3: Preflight is answered by the gateway itself (no FR-ID)

**User Story:** As a front-end developer, I want the browser's preflight request to succeed without a token, so that the real request can be sent at all.

#### Acceptance Criteria

1. WHEN an `OPTIONS` request carries an allowed `Origin` and an `Access-Control-Request-Method` of an allowed method, THEN the system SHALL answer `204 No Content` with the headers of Requirement 2 (`Access-Control-Allow-Origin`, `Access-Control-Allow-Methods`, `Access-Control-Allow-Headers` for the requested headers when they are allowed, `Access-Control-Max-Age`), whatever the path.
2. The preflight SHALL be answered with `204` and SHALL NOT be answered `401` for lack of a token, `404` because the path matches no route, or `429`. This holds for routed paths (`/api/appointments`), for the anonymous routes, and for any other path.
3. A preflight SHALL NOT be forwarded to any service, SHALL NOT count against any rate limit of `api-gateway` Requirement 3, and SHALL NOT consume an allowance of the user or address that sent it.
4. WHEN a preflight from an allowed origin asks for a method outside Requirement 2.3 or a header outside Requirement 2.4, THEN the system SHALL answer `204` with an `Access-Control-Allow-Methods` and `Access-Control-Allow-Headers` that do not list the asked method or header, so that the browser blocks the real request. The response SHALL NOT be forwarded and SHALL NOT be a `5xx`.
5. WHEN an `OPTIONS` request has no `Origin` header, or has no `Access-Control-Request-Method` header, THEN it is not a preflight and SHALL be handled as any other request (and be answered by the existing rules: for example `401` on a routed path without a token).
6. WHEN the gateway answers a preflight, THEN the correlation id (`api-gateway` Requirement 5) and logging (`api-gateway` Requirement 6.1) SHALL still apply: the response carries `X-Correlation-ID` and the request is logged.

### Requirement 4: Actual requests and every error the browser must read (no FR-ID)

**User Story:** As a front-end developer, I want every response the gateway sends to my origin, including the failures, to be readable by the browser, so that the app can show a `401` as "sign in again" and a `429` as "wait" instead of a generic network error.

#### Acceptance Criteria

1. WHEN a non-preflight request carries an allowed `Origin`, THEN every response the gateway sends SHALL carry `Access-Control-Allow-Origin` (Requirement 2.1) and the exposed headers of Requirement 2.5, whatever its status or who produced it. In particular:
   - `401 Unauthorized` with an empty body from the JWT challenge (no token, expired or invalid token);
   - `403 Forbidden` and every other `4xx` or `5xx` that a service produced and the proxy passes through;
   - `404 Not Found` from `UnmatchedRequestMiddleware` for an unrouted path;
   - `429 Too Many Requests` from the rate limiter, with its `Retry-After`;
   - `502 Bad Gateway` and `504 Gateway Timeout` from `ProxyErrorMiddleware`;
   - `500` written by `ExceptionMiddleware`;
   - `200`, `201` and `204` successes, and the `200` or `503` of `/healthz`.
2. WHEN a service's response already carries an `Access-Control-*` header (it should not, Requirement 6.1), THEN the gateway's values SHALL replace it, so that a response has the headers once and only the gateway's.
3. The successful response of a proxied request SHALL otherwise be unchanged: CORS adds headers and SHALL NOT alter a status code, a body, or any other header, and SHALL NOT add a `Vary: Origin` to a response for a request without an `Origin`.
4. The rate limits, the token check and the `404` for unrouted paths SHALL apply to a request from an allowed origin exactly as they apply to one without an `Origin`. CORS is not an authentication or authorization mechanism.

### Requirement 5: Disallowed origins (no FR-ID)

**User Story:** As the platform owner, I want an unlisted origin to get no CORS permission, so that no other web page can read the API's responses from a user's browser.

#### Acceptance Criteria

1. WHEN a request or preflight carries an `Origin` that is not an exact match (Requirement 1.4), THEN the system SHALL NOT add any `Access-Control-*` header, and SHALL NOT answer `Access-Control-Allow-Origin: *` or echo the origin.
2. WHEN a request from a disallowed origin is not a preflight, THEN the gateway SHALL still process it normally: it is routed, authenticated, rate limited and answered by the same rules as any other request, and the response is simply sent without CORS headers. The gateway SHALL NOT reject a request because of its `Origin`. Enforcement is the browser's: it withholds the response from the page, while non-browser clients (curl, other services) are unaffected and still need a valid token.
3. WHEN a preflight from a disallowed origin (or with the origin `null`) arrives, THEN the gateway SHALL answer `204` with no `Access-Control-*` header, so that the browser fails the preflight. The request SHALL NOT be forwarded to a service and SHALL NOT count against a rate limit.
4. WHEN a request carries the `Origin` value `null` (a sandboxed frame or a `file://` page), THEN it SHALL be treated as a disallowed origin.

### Requirement 6: Services and internal endpoints stay closed to browsers (no FR-ID)

**User Story:** As the platform owner, I want only the gateway to speak CORS, so that the services are not reachable by a browser by any other route.

#### Acceptance Criteria

1. Auth, Availability and Booking SHALL NOT register or use any CORS middleware or policy, and SHALL NOT add any `Access-Control-*` header. This spec SHALL NOT change any service project.
2. `/internal/**` SHALL remain unrouted (`api-gateway` Requirement 1.3 and 1.4). A preflight or a request from an allowed origin to a path under `/internal/` SHALL NOT be forwarded: a request is answered `404` (with CORS headers, Requirement 4.1) and a preflight `204`, but nothing reaches Availability's internal API.
3. The gateway SHALL NOT add a route, and SHALL NOT change a route's `AuthorizationPolicy` or `RateLimiterPolicy`, to implement this spec. A test SHALL still assert the nine routes of `api-gateway` Requirement 7.2.
4. The pipeline SHALL remain that of `api-gateway` Requirement 6.2 with the CORS handling added in front of the authentication, the unmatched-route handling and the rate limiter, and behind the correlation id and exception handling (so a failure there still gets a correlation id and a `500` problem body).

### Requirement 7: A tested CORS policy (no FR-ID)

**User Story:** As a developer, I want the origin validation and the CORS behaviour covered by tests, so that a change cannot silently open the gateway to `*` or break the preflight.

#### Acceptance Criteria

1. The system SHALL add to `tests/Gateway.Tests` (xUnit and Moq, like the existing tests) unit tests for the origin validator: each rejected form of Requirement 1.3 (wildcard, malformed, path, trailing slash, query, fragment, user information, non-http(s) scheme), each accepted form (with and without port, `http` and `https`), and the empty list.
2. The tests SHALL assert the shipped configuration: `Cors:AllowedOrigins` is empty in `appsettings.json` and holds exactly `http://localhost:4200` and `http://localhost:8081` in `appsettings.Development.json`.
3. The tests SHALL assert the fixed policy values of Requirement 2 (methods, request headers, exposed headers, max-age, no credentials) and the exact-match rule of Requirement 1.4.
4. Behaviour that needs the whole pipeline (Requirements 3 to 5: a `204` preflight with no token, an `Access-Control-Allow-Origin` on a `401`, `404`, `429` and `502`, no header for a disallowed origin) SHALL be verified by hand against the running gateway in the last task, with the requests recorded in `SmartAppointments.Gateway.http`. In-process integration tests (`WebApplicationFactory`) remain out of scope, as in `api-gateway` Requirement 7.3.

## Revisions

Requirements 1.3, 3.4 and 5.3 were aligned to the framework's verified behaviour during design (see [`design.md`](design.md), "Verified behaviour of the framework middleware"); the intent of each is unchanged.

## Known gaps

- A response to a disallowed origin, or to a request with no `Origin`, carries no `Vary: Origin` (Requirement 4.3 forbids it without an `Origin`). A shared cache could in theory store it and serve it to an allowed origin; the API's authenticated responses are not shared-cacheable, so this is accepted.

## Discrepancies with the BRD

| BRD | This spec | Decision |
|---|---|---|
| Section 10 (Security) lists JWT, roles, hashing, validation and log hygiene; it does not mention browser origins. | The gateway enforces an explicit allowed-origin list (Requirement 1) and no credentials (Requirement 2.2). | A browser front end is a new client type; the BRD should gain a CORS line when it is next revised. |

## Out of scope

- **Wildcard or pattern origins**, including per-environment subdomain wildcards (`*.example.com`) and `*`. The list is exact origins; a new deployment adds its origin to configuration.
- **Changing the origin list without a restart.** Origins are static configuration (Requirement 1.6); hot reload is not provided.
- **Credentials and cookies.** `Access-Control-Allow-Credentials` is never sent; if the front end ever moves to cookie sessions that is a deliberate change with its own CSRF analysis.
- **CORS in Auth, Availability or Booking**, and any route for the services' own documentation or `/internal/` (Requirement 6).
- **CORS on the Development Scalar UI.** `/scalar` is served by the gateway on its own origin, so its "Try it" calls are same-origin.
- **Per-route or per-endpoint CORS policies.** One policy applies to the whole gateway.
- **Content-Security-Policy and other browser-hardening headers**, and the front end's hosting. This belongs to the `smart-appointments-web` repository.
- **The `{status, detail}` error shape** that the front end relies on. It is specified in `shared-web-infrastructure` (Requirement 4); this spec only guarantees the browser can read those bodies (Requirement 4.1 of this spec).
- **Integration tests** (`WebApplicationFactory`), see Requirement 7.4.
