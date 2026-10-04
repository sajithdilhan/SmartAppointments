# Shared web infrastructure — Tasks

> Each task leaves the solution building and `dotnet test SmartAppointments.slnx` green.
>
> All four tasks are done. `Auth.Tests` and `Availability.Tests` keep passing unchanged apart from losing their copies of `ExceptionMiddlewareTests`, and `BuildingBlocks.Tests` adds 26 tests.

- [x] 1. Create `SmartAppointments.BuildingBlocks.Web`
  - Project with the ASP.NET Core framework reference, the JwtBearer and OpenApi packages, and a reference to `BuildingBlocks`; add it to `SmartAppointments.slnx`
  - `JwtValidationOptions`, `AddJwtAuthentication`, `AddAuthorizationWithRoles`, `AddOpenApiWithBearerAuth`
  - `ExceptionMiddleware` with the new mapping, `LoggingMiddleware`, `ToActionResult`
  - _Requirements: 1.1–1.4, 2.1–2.3, 3.1–3.4_

- [x] 2. Switch Auth and Availability to the shared library
  - Reference it from both `*.Api` projects; rewrite `Program.cs` composition
  - Delete both services' middleware folders, the copied auth and OpenAPI registration in each `Application/Dependency/DependencyInjection.cs`, Availability's `JwtOptions`, and each controller's `ToErrorResult`
  - Drop the packages the `*.Application` projects no longer use
  - _Requirements: 1.5, 2.4_

- [x] 3. `BuildingBlocks.Tests`
  - New test project in `tests/`, added to the solution
  - Move `ExceptionMiddlewareTests` out of `Auth.Tests` and `Availability.Tests` and update it for Req 3
  - `AuthenticationExtensionsTests`, `ErrorResultExtensionsTests`
  - _Requirements: 1.1–1.3, 2.1, 3.1–3.3_

- [x] 4. Documentation
  - `CLAUDE.md`: auth wiring now lives in `BuildingBlocks.Web`; describe the new library
  - Strike through open questions 1 and 2 in `availability-branches/design.md`
  - _Requirements: —_

## Requirement 4: one error body

- [x] 5. Switch the HTTP error representation
  - `ProblemDetailsWriter` serializes with `JsonSerializerOptions.Web`
  - `ToActionResult` returns `ApiProblemDetails` as `application/problem+json`
  - Update `ProblemDetailsWriterTests`, `ErrorResultExtensionsTests`, `ExceptionMiddlewareTests`, and the gateway's `RateLimitRejectionTests` and `ProxyErrorMiddlewareTests`
  - _Requirements: 4.1, 4.2, 4.4_

- [x] 6. Keep Booking reading Availability's errors
  - `AvailabilityClient` reads the error body as `ApiProblemDetails`
  - `AvailabilityClientTests` covers a camelCase 409 body whose `detail` is passed through
  - Add a replay test (or confirm one exists) showing that a stored `{status, details}` failure replays as `{status, detail}`
  - _Requirements: 4.1, 4.3, 4.4_

- [x] 7. Documentation
  - `CLAUDE.md`: errors are `{status, detail}` `application/problem+json` everywhere; update the BuildingBlocks.Web and outcome/error-handling sections
  - `.http` files and specs that show the old `details` body
  - _Requirements: —_

- [x] 8. Model-binding errors use the same body
  - `AddSharedProblemDetails()` in `BuildingBlocks.Web`, called from Auth, Availability and Booking `Program.cs`
  - `ModelBindingProblemExtensionsTests`; `CLAUDE.md` updated
  - _Requirements: 4.5_
