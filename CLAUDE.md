# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

This is an in-progress .NET microservices portfolio project (Smart Appointment & Queue Management System). **`README.md` and [`docs/requirements.md`](docs/requirements.md) describe the target/aspirational architecture for the full system — they are not a description of what's currently built.** `docs/requirements.md` is the full BRD/solution-architecture document (functional requirements with FR-IDs, business rules, non-functional requirements, API endpoint drafts, domain events, and the 3-week build plan) — consult it for the intended behavior of a feature before implementing it, but verify against actual code for what already exists. Only these pieces currently have real implementation:

- **Auth service** (`src/Services/Auth/`) — fully wired: JWT auth, registration, login, get-profile.
- **Availability service** (`src/Services/Availability/`) — wired the same way as Auth (EF Core + Npgsql persistence, JWT validation of Auth-issued tokens, the same policies and middleware, `/healthz`, Scalar), with `tests/Availability.Tests`. Branches (`/api/branches`) and service types (`/api/services`) are complete with the same shape: create, update, activate/deactivate (admin-only) and list/get by id (any signed-in role; inactive ones visible only to admins). Branches also carry a weekly schedule (IANA time zone + one opening interval per weekday, `PUT /api/branches/{id}/schedule`). Slots are generated from it by admins (`POST /api/slots/generate`, length = the service type's duration, overlapping slots skipped) and searched by any role (`GET /api/slots/available`); the calendar logic is the pure `SlotPlanner` in the Domain, and handlers take `TimeProvider` for "now". Slots can also be read, reserved and released by other services through `/internal/slots/{id}` (get, `POST .../reserve`, `POST .../release`), authenticated by an API key instead of a JWT (`InternalServicePolicy`; FR-AVL-005). Reserve and release are idempotent per appointment id (a `SlotReservations` table, atomic conditional update of `ReservedCount`). Progress is tracked in `docs/specs/availability-branches/tasks.md` and `docs/specs/booking-appointments/tasks.md`.
- **Booking service** (`src/Services/Booking/`) — the same 4-project layout and wiring as Availability, with `tests/Booking.Tests` and its own database `smart_appointment_booking`. `POST /api/appointments` (customers) creates an appointment, `GET /api/appointments/{id}` (owner, Staff, Admin; anyone else gets the same `404` as an unknown id) reads it, and `POST /api/appointments/{id}/cancel` (owner only) cancels it (FR-BKG-001/002/005). Create requires an `Idempotency-Key` header (stored in `IdempotencyRecords` with a lease, so a retry replays the original outcome); a customer cannot hold overlapping appointments (per-customer advisory lock). Slots are reserved and released through `IAvailabilityClient`, an `HttpClient` with `AddStandardResilienceHandler` calling Availability's internal API with `X-API-Key`; an Availability outage surfaces as `503`. Spec: `docs/specs/booking-appointments/`. The outbox and domain events (e.g. AppointmentBooked/Cancelled) are still pending; Booking publishes nothing yet.
- **API Gateway** (`src/ApiGateway/SmartAppointments.Gateway/`) — stub only; still the default minimal-API template, no YARP reference or routing config yet.
- **BuildingBlocks** (`src/Shared/SmartAppointments.BuildingBlocks/`) — much smaller than README implies: just `Constants.cs`, `Enums/Enums.cs`, `Models/Result.cs`, `Models/ApiProblemDetails.cs`. It has no ASP.NET Core dependency because every `*.Domain` project references it.
- **BuildingBlocks.Web** (`src/Shared/SmartAppointments.BuildingBlocks.Web/`) — the HTTP code every service shares, referenced from each `*.Api` project: `AddJwtAuthentication(configuration)` and `AddAuthorizationWithRoles()` (`Authentication/`), `AddOpenApiWithBearerAuth()` (`OpenApi/`), `ExceptionMiddleware` and `LoggingMiddleware` (`Middlewares/`), the controller extension `this.ToActionResult(error)` (`Results/`, which also maps `422` and `503`), and the service-to-service API-key scheme (`AddApiKeyAuthentication(configuration)` reading `InternalApi:Key`, plus `InternalServicePolicy`, which accepts only that scheme and carries no role claims). Tested by `tests/BuildingBlocks.Tests`. Spec: `docs/specs/shared-web-infrastructure/`.
- Across both shared libraries there is no Messaging/RabbitMQ or Observability/Serilog code yet, despite being named in the constants. Resilience exists only in Booking's Availability client (`Microsoft.Extensions.Http.Resilience`), not as a shared library.

Queue, Notification, and Reporting services referenced in the README do not exist in code yet. Don't assume RabbitMQ, outbox pattern, Redis, or Testcontainers are wired up anywhere — check before relying on them.

## Commands

```powershell
# Build everything
dotnet build SmartAppointments.slnx

# Run a single service (from its Api project directory, or with --project)
dotnet run --project src/Services/Auth/Auth.Api/Auth.Api.csproj

# Run all tests
dotnet test SmartAppointments.slnx

# Run one test project
dotnet test tests/Auth.Tests/Auth.Tests.csproj

# Run a single test by fully-qualified name
dotnet test tests/Auth.Tests/Auth.Tests.csproj --filter "FullyQualifiedName~AuthControllerTests.GetProfile_Successful_Returns_Ok"

# EF Core migrations (run from the *.Api project so the connection string/startup config is picked up)
dotnet ef migrations add <Name> --project src/Services/Auth/Auth.Infrastructure --startup-project src/Services/Auth/Auth.Api
dotnet ef database update --project src/Services/Auth/Auth.Infrastructure --startup-project src/Services/Auth/Auth.Api
```

### Local settings and secrets

Each service's `appsettings.json` deliberately ships `ConnectionStrings:DefaultConnection` and
`Jwt:SecretKey` as empty strings, and the service fails at startup with an explanatory message if either
is missing. For **local development** both are filled in each service's checked-in
`appsettings.Development.json`, against a local PostgreSQL (`localhost:5432`, user `postgres`, password
`password`) with one database per service: `smart_appointment_users` for Auth and
`smart_appointment_availability` for Availability and `smart_appointment_booking` for Booking. These are throwaway local values and are committed on
purpose.

Every service's `Jwt:SecretKey` **must be the same value as Auth's**, because the other services validate
the tokens Auth signs; `Jwt:Issuer` and `Jwt:Audience` in `appsettings.json` must match Auth's too. When
adding a service, copy the key from Auth's `appsettings.Development.json`.

Service-to-service calls use an API key, `InternalApi:Key` (at least 32 bytes; the service fails at startup if it is missing or too short). It must be the **same value in Availability and Booking**: empty in `appsettings.json`, a throwaway value in each `appsettings.Development.json`, and `InternalApi__Key` in the environment elsewhere. Booking also needs `Services:Availability:BaseUrl` (empty in `appsettings.json`, `http://localhost:5202` in development).

Outside local development both come from the environment (`ConnectionStrings__DefaultConnection`,
`Jwt__SecretKey`) and must never be committed. `dotnet user-secrets` still works and overrides the
development file if a machine needs different values. The optional `Seed:Admin` section (`Email`,
`Password`, `FirstName`, `LastName`, `PhoneNumber`) for Auth lives in user-secrets; without all five keys
the seeder is inert.

`Availability.Infrastructure` has an `IDesignTimeDbContextFactory`, so `dotnet ef migrations add` works for
that service with no database; `database update` reads the connection string from the API project's
appsettings files, user-secrets or the environment.

There is no `global.json`, so the SDK floats to whatever is installed locally (target framework is `net10.0` across all projects). There is no `docker-compose.yml`, `.editorconfig`, or `Directory.Build.props` at the repo root yet.

## Architecture conventions (per service)

Each service follows a 4-project Clean Architecture split — `<Service>.Api`, `<Service>.Application`, `<Service>.Domain`, `<Service>.Infrastructure` — wired together via `AddApplication()` / `AddInfrastructure()` extension methods in each layer's `Dependency/DependencyInjection.cs`, called from `Program.cs` in that order.

- **Api**: ASP.NET Core MVC controllers (not minimal APIs), e.g. `Controllers/AuthController.cs`. Controllers take an `ISender` (MediatR) via primary-constructor DI and just dispatch commands/queries. Failed `Result`s are returned with `this.ToActionResult(result.Error!)` from `BuildingBlocks.Web`, never a per-controller switch. `Program.cs` composes `AddApplication()` → `AddInfrastructure(config)` → `AddControllers()` → `AddOpenApiWithBearerAuth()` → `AddJwtAuthentication(config)` → `AddAuthorizationWithRoles()`, and registers the shared `ExceptionMiddleware` and `LoggingMiddleware` ahead of auth middleware. `/healthz` health checks are exposed. API docs use **Scalar** (`Scalar.AspNetCore`), not Swashbuckle/Swagger UI, mapped only in dev alongside `MapOpenApi()`.
- **Application**: CQRS via MediatR. `Commands/`, `Queries/`, and one `Handlers/` class per command/query (e.g. `RegisterCustomerCommand` → `RegisterCustomerCommandHandler`). `Validations/` holds one FluentValidation validator per command, registered in this layer's DI. `Abstractions/` holds interfaces the Infrastructure layer implements (`IUserRepository`, `IPasswordHasher`, `ITokenGenerator`). `Models/` holds DTOs/request/response records. This layer's `DependencyInjection.cs` registers only MediatR and the validators; token validation and the policies (AdminPolicy/StaffPolicy/CustomerPolicy/AdminOrStaffPolicy/AllowedOriginsPolicy) come from `BuildingBlocks.Web`. Don't copy them into a service.
- **Domain**: plain entities and value objects (e.g. `Entities/User.cs`, `ValueObjects/Email.cs`). No shared base entity/aggregate abstractions exist yet.
- **Infrastructure**: EF Core + Npgsql (PostgreSQL) for the Auth service — `Persistence/ApplicationDbContext.cs`, `Persistence/Migrations/`, `Persistence/UserRepository.cs`, plus concrete `Services/PasswordHasher.cs` / `Services/TokenGenerator.cs`. Registers the DbContext, repositories, `JwtOptions`, and `AddHealthChecks().AddNpgSql(...)`.

### Outcome/error handling pattern

Expected failures use `SmartAppointments.BuildingBlocks.Models.Result<T>` (`Result.Success(...)` / `Result.Failure(...)` with a `record Error(int Status, string Details)`), returned from handlers and translated into the appropriate `ActionResult` in the controller. Unhandled exceptions are caught by `ExceptionMiddleware` and converted to `ApiProblemDetails` (problem+json); it never echoes an exception's message (only 401 for `UnauthorizedAccessException`, the carried status for `BadHttpRequestException`, otherwise 500), so don't throw to report an expected failure. Prefer this `Result<T>` pattern over throwing for anything that is a normal/expected failure path (validation, not-found, auth failures) — reserve exceptions for truly unexpected errors.

## Testing conventions

`tests/Auth.Tests` uses xUnit + Moq only (no FluentAssertions, no Testcontainers, no `WebApplicationFactory` yet, despite the README mentioning them as an eventual goal). Tests are pure unit tests: mock `ISender` (or other abstractions) with Moq, construct the controller directly, and assert on the concrete `IActionResult` type (`Assert.IsType<OkObjectResult>(...)`, etc.). To test an `[Authorize]`-decorated action, manually build a `ClaimsPrincipal` / `DefaultHttpContext` / `ControllerContext` and assign it to `controller.ControllerContext` — see `AuthControllerTests.GetProfile_Successful_Returns_Ok` for the pattern. Follow this same style (mock-and-assert-on-ActionResult) for new controller tests rather than introducing integration-style tests unless asked.

When adding a new service, mirror the Auth service's project layout and DI wiring order exactly — the Availability service already does this and is the second reference point if Auth's implementation is more complete/further along on a given concern.

## Working with Claude Code subagents

When delegating work to a subagent (the `Agent` tool), run it on **Sonnet** — pass `model: "sonnet"`
explicitly on every spawn rather than letting it inherit the parent session's model. Sonnet is the
project's chosen subagent model because delegated tasks (codebase searches, spec drafting, focused
refactors) don't need the larger model and run faster and cheaper on it. The intended model is
**Sonnet 5.5**; the `sonnet` alias resolves to the latest Sonnet release, so it picks up Sonnet 5.5
without pinning a version-specific model ID.
