# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

This is an in-progress .NET microservices portfolio project (Smart Appointment & Queue Management System). **`README.md` and [`docs/requirements.md`](docs/requirements.md) describe the target/aspirational architecture for the full system — they are not a description of what's currently built.** `docs/requirements.md` is the full BRD/solution-architecture document (functional requirements with FR-IDs, business rules, non-functional requirements, API endpoint drafts, domain events, and the 3-week build plan) — consult it for the intended behavior of a feature before implementing it, but verify against actual code for what already exists. Only these pieces currently have real implementation:

- **Auth service** (`src/Services/Auth/`) — fully wired: JWT auth, registration, login, get-profile.
- **Availability service** (`src/Services/Availability/`) — scaffolded (Api/Application/Domain layers mirror Auth's structure with a `BranchesController` / `GetBranchesQuery`), but `Availability.Infrastructure` has no persistence wired yet (no DbContext).
- **API Gateway** (`src/ApiGateway/SmartAppointments.Gateway/`) — stub only; still the default minimal-API template, no YARP reference or routing config yet.
- **BuildingBlocks** (`src/Shared/SmartAppointments.BuildingBlocks/`) — much smaller than README implies: just `Constants.cs`, `Enums/Enums.cs`, `Models/Result.cs`, `Models/ApiProblemDetails.cs`. No Messaging/RabbitMQ, Observability/Serilog, or Resilience/Polly code exists yet, despite being named in the constants (e.g. `ApiKeyAuthenticationScheme` is defined but unused).

Booking, Queue, Notification, and Reporting services referenced in the README do not exist in code yet. Don't assume RabbitMQ, outbox pattern, Redis, or Testcontainers are wired up anywhere — check before relying on them.

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

### Local secrets

`src/Services/Auth/Auth.Api/appsettings.json` deliberately ships `ConnectionStrings:DefaultConnection`
and `Jwt:SecretKey` as empty strings — they are the two values that must never be committed. The Auth
service fails at startup with an explanatory message if either is missing, so set them once per machine:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=smart_appointment_users;Username=postgres;Password=<password>" --project src/Services/Auth/Auth.Api
dotnet user-secrets set "Jwt:SecretKey" "<at least 32 bytes of random text>" --project src/Services/Auth/Auth.Api
```

Outside local development both come from the environment (`ConnectionStrings__DefaultConnection`,
`Jwt__SecretKey`). The optional `Seed:Admin` section (`Email`, `Password`, `FirstName`, `LastName`,
`PhoneNumber`) also belongs in user-secrets; without all five keys the seeder is inert.

There is no `global.json`, so the SDK floats to whatever is installed locally (target framework is `net10.0` across all projects). There is no `docker-compose.yml`, `.editorconfig`, or `Directory.Build.props` at the repo root yet.

## Architecture conventions (per service)

Each service follows a 4-project Clean Architecture split — `<Service>.Api`, `<Service>.Application`, `<Service>.Domain`, `<Service>.Infrastructure` — wired together via `AddApplication()` / `AddInfrastructure()` extension methods in each layer's `Dependency/DependencyInjection.cs`, called from `Program.cs` in that order.

- **Api**: ASP.NET Core MVC controllers (not minimal APIs), e.g. `Controllers/AuthController.cs`. Controllers take an `ISender` (MediatR) via primary-constructor DI and just dispatch commands/queries. Custom `Middlewares/ExceptionMiddleware.cs` and `LoggingMiddleware.cs` are registered ahead of auth middleware. `/healthz` health checks are exposed. API docs use **Scalar** (`Scalar.AspNetCore`), not Swashbuckle/Swagger UI, mapped only in dev alongside `MapOpenApi()`.
- **Application**: CQRS via MediatR. `Commands/`, `Queries/`, and one `Handlers/` class per command/query (e.g. `RegisterCustomerCommand` → `RegisterCustomerCommandHandler`). `Validations/` holds one FluentValidation validator per command, registered in this layer's DI. `Abstractions/` holds interfaces the Infrastructure layer implements (`IUserRepository`, `IPasswordHasher`, `ITokenGenerator`). `Models/` holds DTOs/request/response records. Auth wiring itself (`AddAuthentication`, `AddAuthorizationWithRoles` — policy-based: AdminPolicy/StaffPolicy/CustomerPolicy/AdminOrStaffPolicy/AllowedOriginsPolicy) lives in this layer's `DependencyInjection.cs`, not Infrastructure.
- **Domain**: plain entities and value objects (e.g. `Entities/User.cs`, `ValueObjects/Email.cs`). No shared base entity/aggregate abstractions exist yet.
- **Infrastructure**: EF Core + Npgsql (PostgreSQL) for the Auth service — `Persistence/ApplicationDbContext.cs`, `Persistence/Migrations/`, `Persistence/UserRepository.cs`, plus concrete `Services/PasswordHasher.cs` / `Services/TokenGenerator.cs`. Registers the DbContext, repositories, `JwtOptions`, and `AddHealthChecks().AddNpgSql(...)`.

### Outcome/error handling pattern

Expected failures use `SmartAppointments.BuildingBlocks.Models.Result<T>` (`Result.Success(...)` / `Result.Failure(...)` with a `record Error(int Status, string Details)`), returned from handlers and translated into the appropriate `ActionResult` in the controller. Unhandled exceptions are caught by `ExceptionMiddleware` and converted to `ApiProblemDetails` (problem+json). Prefer this `Result<T>` pattern over throwing for anything that is a normal/expected failure path (validation, not-found, auth failures) — reserve exceptions for truly unexpected errors.

## Testing conventions

`tests/Auth.Tests` uses xUnit + Moq only (no FluentAssertions, no Testcontainers, no `WebApplicationFactory` yet, despite the README mentioning them as an eventual goal). Tests are pure unit tests: mock `ISender` (or other abstractions) with Moq, construct the controller directly, and assert on the concrete `IActionResult` type (`Assert.IsType<OkObjectResult>(...)`, etc.). To test an `[Authorize]`-decorated action, manually build a `ClaimsPrincipal` / `DefaultHttpContext` / `ControllerContext` and assign it to `controller.ControllerContext` — see `AuthControllerTests.GetProfile_Successful_Returns_Ok` for the pattern. Follow this same style (mock-and-assert-on-ActionResult) for new controller tests rather than introducing integration-style tests unless asked.

When adding a new service, mirror the Auth service's project layout and DI wiring order exactly — the Availability service already does this and is the second reference point if Auth's implementation is more complete/further along on a given concern.
