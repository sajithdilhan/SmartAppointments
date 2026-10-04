# Shared web infrastructure — Requirements

## Introduction

Auth and Availability each carry an identical copy of the JWT validation setup, the five authorization policies, the OpenAPI bearer-scheme registration, `ExceptionMiddleware`, `LoggingMiddleware` and the controller's `Error` → `ActionResult` mapping. Four more services are planned, and each copy would have to be kept in step by hand. This feature moves that code into one shared library that every service references, before a third service copies it again.

It closes open questions 1 and 2 in [`availability-branches/design.md`](../availability-branches/design.md). It has no FR-ID: it refines the security (`FR-AUTH-003`, section 10) and maintainability (section 10, "consistent API response model") parts of [`docs/requirements.md`](../../requirements.md) without changing what any endpoint does, except for the error-handling fix in Requirement 3.

## Requirements

### Requirement 1: One definition of token validation and policies (no FR-ID)

**User Story:** As a developer adding a service, I want to call one method to accept the tokens Auth issues, so that a token and a policy name mean the same thing in every service without copying code.

#### Acceptance Criteria

1. WHEN a service registers JWT authentication, THEN the system SHALL validate issuer, audience, lifetime and signing key against `Jwt:Issuer`, `Jwt:Audience` and `Jwt:SecretKey`, with zero clock skew, `MapInboundClaims = false` and `role` as the role claim type — the same rules both services apply today.
2. IF `Jwt:SecretKey` is missing or blank, shorter than 32 bytes, or `Jwt:Issuer` or `Jwt:Audience` is blank, THEN registration SHALL throw an `InvalidOperationException` naming the key, so the service refuses to start.
3. WHEN a service registers its authorization policies, THEN the system SHALL register `AdminPolicy`, `StaffPolicy`, `CustomerPolicy`, `AdminOrStaffPolicy` and `AllowedOriginsPolicy` with the same role sets as today.
4. WHEN a service registers OpenAPI, THEN the document SHALL declare the `Bearer` HTTP security scheme, so that Scalar offers a token box.
5. WHERE these registrations exist in a service's own projects today, they SHALL be removed, so that there is exactly one copy.

### Requirement 2: One definition of the HTTP error shape (no FR-ID)

**User Story:** As an API client, I want every service to report failures the same way, so that I handle errors once.

#### Acceptance Criteria

1. WHEN a controller maps a failed `Result`, THEN `Error.Status` 400, 401, 403, 404 and 409 SHALL produce the matching response and any other status SHALL produce `500`, with the `Error` as the body, through one shared mapping.
2. WHEN an exception escapes a handler, THEN the shared `ExceptionMiddleware` SHALL return `application/problem+json` in the `ApiProblemDetails` shape.
3. WHEN a request arrives, THEN the shared `LoggingMiddleware` SHALL log its method and path.
4. WHERE Auth and Availability each have their own middleware and mapping today, those copies SHALL be removed.

### Requirement 3: Unexpected exceptions never echo their message (no FR-ID)

**User Story:** As an operator, I want exception text to stay in the logs, so that a library message carrying a connection string, SQL or a file path never reaches a caller.

#### Acceptance Criteria

1. IF an `InvalidOperationException` or `ArgumentException` (including `ArgumentNullException`) escapes, THEN the system SHALL return `500` with the generic message, not `400` with the exception's message. Expected failures already travel as `Result` errors, so an exception reaching the middleware is by definition unexpected.
2. IF an `UnauthorizedAccessException` escapes, THEN the system SHALL return `401` with the fixed message "Unauthorized access.".
3. IF a `BadHttpRequestException` escapes (for example an oversized or unreadable body), THEN the system SHALL return the status code it carries with the fixed message "The request could not be read.".
4. WHEN any exception is caught, THEN the system SHALL log it with its full detail.

### Requirement 4: One error body for browser clients (no FR-ID)

**User Story:** As a frontend developer, I want every failure (a handler's `Result` error, a middleware error or a gateway error) to have the same JSON body, so that the web client reads the message from one field.

Today there are two shapes: controller errors serialize the `Error` record as `{"status":409,"details":"..."}` (`application/json`), while `ProblemDetailsWriter` serializes `ApiProblemDetails` with the non-web defaults as `{"Status":429,"Detail":"..."}` (`application/problem+json`). This requirement amends Requirement 2.1 and 2.2.

#### Acceptance Criteria

1. WHEN a controller maps a failed `Result` through `ToActionResult`, THEN the response SHALL keep the status Requirement 2.1 gives it (with `422` and `503` passed through as today) and SHALL have the body `{"status":<int>,"detail":"<Error.Details>"}` with content type `application/problem+json`.
2. WHEN `ProblemDetailsWriter` writes a body (`ExceptionMiddleware`, the gateway's rate limiter, proxy-error and unmatched-request paths), THEN the keys SHALL be camelCase: `{"status":<int>,"detail":"..."}`.
3. WHEN Booking replays a stored failed outcome for a repeated `Idempotency-Key`, THEN the replayed body SHALL have the shape in 4.1. Records stored before this change SHALL replay correctly with no data migration.
4. WHERE a test asserts an error body or its content type, it SHALL assert the new shape.
5. WHEN `[ApiController]` rejects a request during model binding or validation (a missing or malformed JSON body, an unparseable route or query value, a null body), THEN the response SHALL be `400` with the body `{"status":400,"detail":"Invalid request data. Errors: <msg1>,<msg2>"}` as `application/problem+json`, through one shared registration that every service's Api uses.

## Out of scope

- **Per-field structure of model-binding errors.** Requirement 4.5 gives them the shared body, but their messages are joined into the single `detail` string, not kept as an `errors` map.
- **Field-level validation errors.** FluentValidation messages stay joined into the single `detail` string.
- **Gateway.** It is still a template and validates nothing. It will reference this library when it is specced.
- **Correlation IDs, Serilog, OpenTelemetry.** The BRD wants them in week 3. They will land in this library, but not in this feature.
- **API-key authentication.** `Constants.ApiKeyAuthenticationScheme` stays unused until the internal reserve/release endpoints need it.
