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
