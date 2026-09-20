# Auth identity — Design

> **Retro-fitted spec.** This describes the Auth service as it is built today. References like _(Req 2.3)_ point at requirement 2, criterion 3 in [`requirements.md`](requirements.md).

## Overview

The Auth service is the reference implementation for every other service in this solution: a four-project Clean Architecture split, MediatR CQRS in the Application layer, EF Core + Npgsql in Infrastructure, and `Result<T>` for expected failures with `ExceptionMiddleware` catching the rest.

Two decisions drive most of the rest of the design:

- **Authorization decisions live in the handler, not only in the attribute.** `[Authorize(Policy = AllowedOriginsPolicy)]` establishes only that the caller is *some* authenticated user with a known role. The narrowing rule — a customer may read only their own profile (Req 3.3) — is enforced in `GetCustomerByEmailHandler`, because it depends on the data being requested, not just on the caller. The controller reads the caller's claims and passes them into the query as ordinary parameters (Req 3.8), which keeps the handler free of `HttpContext` and unit-testable with plain Moq.
- **Staging and committing are separate repository operations.** `AddAsync` only stages; `SaveChangesAsync` commits (Req 1.8). This costs one extra line per handler now and buys the ability to make a registration atomic with an outbox insert later, without reworking the repository.

## Architecture

| Layer | Contents |
|---|---|
| `Auth.Api` | `AuthController`; `ExceptionMiddleware`, `LoggingMiddleware`; `Program.cs` composition root; Scalar API reference (dev only); `/healthz` |
| `Auth.Application` | `Commands/`, `Queries/`, `Handlers/`, `Validations/`, `Models/` (DTOs + `JwtOptions`), `Abstractions/` (interfaces Infrastructure implements); `AddApplication()`, `AddAuthentication()`, `AddAuthorizationWithRoles()` |
| `Auth.Domain` | `User` aggregate, `Email` value object. No EF or ASP.NET references. |
| `Auth.Infrastructure` | `ApplicationDbContext`, `UserRepository`, `DatabaseSeeder`, migrations; `PasswordHasher` (BCrypt), `TokenGenerator` (JWT); `AddInfrastructure(configuration)` |

`Program.cs` composes them in a fixed order — `AddApplication()` → `AddInfrastructure(config)` → `AddControllers()` → `AddAuthentication(config)` → `AddAuthorizationWithRoles()` — then seeds, then builds the pipeline: `ExceptionMiddleware` → `LoggingMiddleware` → HTTPS redirection → authentication → authorization → controllers → health checks.

Note that JWT bearer setup and the authorization policies live in **Application**, not Infrastructure. They are policy, not I/O, and putting them beside the DTOs keeps `JwtOptions` in one assembly.

## Components and interfaces

### Endpoints

Route prefix `api/[controller]` → `/api/Auth`. The controller takes `ISender` by primary constructor and does nothing but dispatch and map.

| Verb / route | Action | Authorization | Dispatches |
|---|---|---|---|
| `POST /api/Auth/login` | `Login(UserLoginRequest)` | anonymous | `LoginUserCommand(Email, Password)` |
| `POST /api/Auth/register` | `Register(RegisterCustomerRequest, ct)` | anonymous | `RegisterCustomerCommand(FirstName, LastName, Email, PhoneNumber, Password)` |
| `GET /api/Auth/profile?email=` | `GetProfile(string email, ct)` | `[Authorize(Policy = Constants.AllowedOriginsPolicy)]` | `GetCustomerQuery(RequestedEmail, CurrentUserEmail, CurrentUserRole)` |

`Register` returns `CreatedAtAction(nameof(GetProfile), new { email }, value)` so the `Location` header points at the new resource (Req 1.1).

### Commands, queries and handlers

All three handlers return `Result<T>`; none of them throw for an expected failure.

**`RegisterCustomerCommandHandler`** → `Result<RegisterCustomerResponse>`
validate → `GetByEmailAsync` duplicate check (no-tracking, Req 1.5) → `passwordHasher.Hash` → `User.RegisterCustomer(...)` → `AddAsync` → `SaveChangesAsync` → `(UserId, Email, Role)`.

**`LoginUserHandler`** → `Result<TokenResponse>`
null guard → validate → `GetForUpdateByEmailAsync` (tracked, Req 2.6) → `user is null || !user.IsActive` → 401 → `passwordHasher.Verify` fails → the *same* 401 (Req 2.3) → `user.RecordLogin()` → `SaveChangesAsync` → `GenerateAccessToken` + `GenerateRefreshToken`.

The two 401 branches deliberately carry an identical `Error`, and neither the message nor the status distinguishes "no such user" from "wrong password" — that is the account-enumeration defence, and it is asserted by `LoginUserHandlerTests.Unknown_Email_Returns_401_Not_404`.

**`GetCustomerByEmailHandler`** → `Result<GetCustomerResponse?>`
missing `CurrentUserEmail` or `CurrentUserRole` → `Error(403)` before any database access (Req 3.5) → IF the caller's role is `Customer`, overwrite the requested email with the caller's own (Req 3.3) → `GetByEmailAsync` (no-tracking, Req 3.7) → null → `Error(404)` → project to `GetCustomerResponse`.

Overwriting rather than comparing-and-rejecting is intentional: a customer asking for someone else's profile gets their own back rather than an error that would confirm the other address exists.

### Abstractions

Declared in `Auth.Application/Abstractions/`, implemented in `Auth.Infrastructure/`:

| Interface | Implementation | Notes |
|---|---|---|
| `IUserRepository` | `Persistence/UserRepository` | `AddAsync` (stage only), `GetByEmailAsync` (no-tracking), `GetForUpdateByEmailAsync` (tracked), `SaveChangesAsync`. Both reads funnel through a private `QueryByEmail` that normalises the input through `Email.Create`, so a lookup matches however the value was stored (Req 1.7). |
| `IPasswordHasher` | `Services/PasswordHasher` | BCrypt.Net `Hash` / `Verify` (Req 1.6). |
| `ITokenGenerator` | `Services/TokenGenerator` | HS256; claims `sub`/`email`/`role` written with the short names verbatim (Req 2.8) because bearer validation sets `MapInboundClaims = false`; throws `InvalidOperationException` when `AccessTokenExpirationMinutes <= 0` (Req 2.9); refresh token = 64 random bytes, base64 (Req 2.10). |

### Validation

One FluentValidation validator per command, registered explicitly in `Auth.Application/Dependency/DependencyInjection.cs` and invoked by the handler itself — there is no MediatR validation pipeline behaviour, so a handler that forgets to validate is not covered.

| Validator | Rules | Criteria |
|---|---|---|
| `RegisterCustomerCommandValidator` | first/last name required, ≤ 50; email required + format; phone required + `^\+?[1-9]\d{1,14}$`; password ≥ 8 with upper, lower, digit and `\W` | Req 1.3 |
| `LoginUserRequestValidator` | email required + format; password required | Req 2.4 |

### Authentication and authorization

`AddAuthentication(configuration)` binds the `Jwt` configuration section and configures JwtBearer with `MapInboundClaims = false`, `ClockSkew = TimeSpan.Zero`, `RoleClaimType = "role"`, and validation of issuer, audience, lifetime and signing key (Req 3.9).

`AddAuthorizationWithRoles()` registers five policies from the names in `SmartAppointments.BuildingBlocks.Constants` (Req 3.10): `AdminPolicy`, `StaffPolicy`, `CustomerPolicy`, `AdminOrStaffPolicy`, and `AllowedOriginsPolicy` (any of the three roles). Only `AllowedOriginsPolicy` has a consumer today; the rest exist for the services still to be built.

`AddApplication()` also registers an OpenAPI document transformer that declares the `Bearer` security scheme, so the Scalar reference in development can authenticate.

## Data model

Single aggregate, single table.

**`User`** — private constructor, private setters, created only through the static factories `RegisterCustomer` / `RegisterStaff` / `RegisterAdmin`, each of which assigns `Guid.CreateVersion7()`, trims the strings, sets `RegistrationDateUtc = DateTime.UtcNow` and `IsActive = true` (Req 1.2). Behaviour: `RecordLogin()`, `Deactivate()`, `Activate()`, `ChangePassword(hash)`; computed `FullName`.

**`Email`** — a `record` with a private constructor and a `Create` factory that trims, lower-cases, and throws `ArgumentException` when the value is empty or has no `@` (Req 1.7). Being a record gives it value equality, which is what makes `u.Email == value` work in the repository query.

**EF configuration** (`ApplicationDbContext.OnModelCreating`) — table `Users`, key `Id`; `FirstName`/`LastName` required, max 50; `Email` converted to `string` via `email => email.Value` / `value => Email.Create(value)`, required, max 100, with a **unique index** (Req 1.9); `PasswordHash`, `Role`, `PhoneNumber`, `RegistrationDateUtc`, `IsActive` required; `LastLoginAtUtc` nullable.

**Migration** `20260624055747_InitialCreate` creates `Users` (`uuid` PK, `varchar(50)` ×2, `varchar(100)` email, `text` phone and hash, `integer` role, `timestamptz` registration date, `boolean` active, nullable `timestamptz` last login) plus `IX_Users_Email` unique. There is no refresh-token table — see *Out of scope* in the requirements.

**Seeding** — `DatabaseSeeder.SeedAsync` runs from `Program.cs` before the pipeline is built. It is inert unless the whole `Seed:Admin` section is present, and skips if any `Admin` already exists (Req 4).

## Error handling

Expected failures return `Result<T>.Failure(new Error(status, details))` and the controller maps them. Unexpected exceptions reach `ExceptionMiddleware`, which writes an `ApiProblemDetails` as `application/problem+json` (`ArgumentNullException`/`InvalidOperationException` → 400, `UnauthorizedAccessException` → 401, everything else → 500 with a generic message).

| Condition | `Error.Status` | Controller result |
|---|---|---|
| Register: validation failed | 400 | `BadRequest(error)` |
| Register: email already taken | 400 | `BadRequest(error)` |
| Login: validation failed | 400 | `Unauthorized(error)` ⚠ |
| Login: unknown / inactive / wrong password | 401 | `Unauthorized(error)` |
| Profile: caller claims missing | 403 | `NotFound(error)` ⚠ |
| Profile: user not found | 404 | `NotFound(error)` |

⚠ The controller branches on `IsSuccess` alone, not on `Error.Status`, so two failures are reported with the wrong status. Both are recorded as known gaps in the requirements; fixing them means switching on `result.Error.Status` in `AuthController`, which is the shape every future controller should copy.

## Testing strategy

`tests/Auth.Tests` — xUnit + Moq only. No FluentAssertions, no `WebApplicationFactory`, no Testcontainers; everything is a pure unit test.

- **Controller tests** mock `ISender`, construct `AuthController` directly, and assert on the concrete `IActionResult` type. For the `[Authorize]` action, a `ClaimsPrincipal` is built by hand and assigned through `DefaultHttpContext` → `ControllerContext` — `AuthControllerTests.GetProfile_Successful_Returns_Ok` is the pattern to copy.
- **Handler tests** mock `IUserRepository`, `IPasswordHasher`, `ITokenGenerator`, `IValidator<T>` and `ILogger<T>`, and verify both the returned `Result` and the repository interactions (`Verify(r => r.SaveChangesAsync(...), Times.Never)` is how "rejected without writing" is asserted).
- **`TokenGeneratorTests`** is the one test that exercises a real Infrastructure type, reading the produced JWT back with `JwtSecurityTokenHandler` to assert claim names, lifetime and issuer/audience.

26 test methods across 5 files, 28 executed cases once the role theory in `LoginUserHandlerTests.Any_Active_Role_Can_Log_In` expands. Coverage maps to the criteria cited inline in [`requirements.md`](requirements.md); the units listed in *Known gaps* item 5 have none.

New work on this feature follows the same style — mock and assert on `IActionResult` — rather than introducing integration tests, unless the task explicitly calls for them.
