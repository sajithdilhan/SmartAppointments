# Auth identity — Design

> **Retro-fitted spec.** This describes the Auth service as it is built today, including the gap-closure work of tasks 9–14. References like _(Req 2.3)_ point at requirement 2, criterion 3 in [`requirements.md`](requirements.md).

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
| `GET /api/Auth/me` | `GetMe(ct)` | `[Authorize(Policy = Constants.AllowedOriginsPolicy)]` | `GetCustomerQuery(callerEmail, CurrentUserEmail, CurrentUserRole)` |
| `POST /api/Auth/refresh` | `Refresh(RefreshTokenRequest, ct)` | anonymous † | `RefreshTokenCommand(RefreshToken)` |
| `POST /api/Auth/logout` | `Logout(RefreshTokenRequest, ct)` | anonymous † | `LogoutCommand(RefreshToken)` |

† Both are anonymous on purpose. The refresh token *is* the credential, and requiring a valid access
token alongside it would defeat the point: the access token has usually just expired, which is why
the client is calling `/refresh` at all. Requiring one on `/logout` would be worse — it would leave
a live refresh token in the world precisely when the user asked to end the session (Req 7.5).

`GetProfile` and `GetMe` are two routes over one private `SendProfileQuery(email, ct)`; the only
difference is where the requested address comes from. `/me` supplies the caller's own `email` claim,
so a caller of any role gets themselves — the customer-narrowing rule in the handler (Req 3.3) is a
no-op for it. Keeping `/profile?email=` is what leaves staff and admin able to look somebody up
(Req 3.4); merging the two would cost that.

`Register` returns `CreatedAtAction(nameof(GetProfile), new { email }, value)` so the `Location` header points at the new resource (Req 1.1). It points at `/profile?email=`, not `/me`, because `/me` is meaningless without the token the caller does not yet have.

Every failure path in the controller goes through one private helper:

```csharp
private ObjectResult ToErrorResult(Error error) => error.Status switch
{
    StatusCodes.Status400BadRequest  => BadRequest(error),
    StatusCodes.Status401Unauthorized => Unauthorized(error),
    StatusCodes.Status403Forbidden   => StatusCode(StatusCodes.Status403Forbidden, error),
    StatusCodes.Status404NotFound    => NotFound(error),
    StatusCodes.Status409Conflict    => Conflict(error),
    _                                => StatusCode(StatusCodes.Status500InternalServerError, error)
};
```

The status a handler chose is therefore the status the caller sees. Branching on `IsSuccess` alone —
the original shape — collapsed 403 into 404 and 400 into 401. `403` uses `StatusCode` rather than
`Forbid()` deliberately: `ForbidResult` runs the authentication handler's challenge and writes no
body, which would drop the `Error` the handler produced. **Every controller in every service should
copy this helper**, including the services still to be built.

### Commands, queries and handlers

All three handlers return `Result<T>`; none of them throw for an expected failure.

**`RegisterCustomerCommandHandler`** → `Result<RegisterCustomerResponse>`
validate → `GetByEmailAsync` duplicate check (no-tracking, Req 1.5) → `passwordHasher.Hash` → `User.RegisterCustomer(...)` → `AddAsync` → `SaveChangesAsync`, catching `DuplicateEmailException` → `(UserId, Email, Role)`.

The duplicate address is checked twice, at two different times, because neither check alone is
enough. The read-then-write pre-check is the cheap common path and saves a BCrypt hash, but it
cannot see a concurrent registration that has not committed; the unique index can, and rejects the
loser at commit. Both produce the identical `Error(400, "Email is already registered.")`, so the
race is invisible to the caller (Req 1.4a).

**`LoginUserHandler`** → `Result<TokenResponse>`
null guard → validate → `GetForUpdateByEmailAsync` (tracked, Req 2.6) → `user is null || !user.IsActive` → 401 → `passwordHasher.Verify` fails → the *same* 401 (Req 2.3) → `user.RecordLogin()` → `SaveChangesAsync` → `GenerateAccessToken` + `GenerateRefreshToken`.

The two 401 branches deliberately carry an identical `Error`, and neither the message nor the status distinguishes "no such user" from "wrong password" — that is the account-enumeration defence, and it is asserted by `LoginUserHandlerTests.Unknown_Email_Returns_401_Not_404`.

**`GetCustomerByEmailHandler`** → `Result<GetCustomerResponse?>`
missing `CurrentUserEmail` or `CurrentUserRole` → `Error(403)` before any database access (Req 3.5) → IF the caller's role is `Customer`, overwrite the requested email with the caller's own (Req 3.3) → the resolved email is blank or has no `@` → `Error(400)` before any database access (Req 3.1b) → `GetByEmailAsync` (no-tracking, Req 3.7) → null → `Error(404)` → project to `GetCustomerResponse`.

The malformed-email guard duplicates a rule that `Email.Create` already enforces, and does so on
purpose. The repository normalises every lookup through `Email.Create`, which *throws* — appropriate
deep in a value object, wrong as the response to a query string a caller typed. Checking in the
handler keeps a caller mistake a 400 instead of letting `ExceptionMiddleware` render it as a 500.

Overwriting rather than comparing-and-rejecting is intentional: a customer asking for someone else's profile gets their own back rather than an error that would confirm the other address exists.

**`RefreshTokenCommandHandler`** → `Result<TokenResponse>`
hash the presented token → `GetByHashForUpdateAsync` → not found, expired, or the user is inactive →
401 → **already revoked** → revoke the descendant chain, then 401 (Req 6.6) → otherwise
`token.Redeem(replacement)` + `AddAsync(replacement)` + prune that user's expired tokens →
`SaveChangesAsync` → new `TokenResponse`.

Every failure carries the identical `Error(401, "Invalid or expired refresh token.")`, for the same
reason login's two 401s are identical: the distinction between "no such token" and "that token was
already used" is information an attacker wants and a legitimate client cannot act on.

The order matters. Revocation of the old token and insertion of the new one are staged and committed
in **one** `SaveChangesAsync`, so a crash between them cannot leave a user holding two live tokens or
none (Req 6.4). The existing staging/commit split in `IUserRepository` is what makes this free — both
repositories share the scoped `ApplicationDbContext`, so one commit covers both.

**`LogoutCommandHandler`** → `Result<bool>`
hash → `GetByHashForUpdateAsync` → not found or already revoked → **succeed anyway** → otherwise
`token.Revoke()` → `SaveChangesAsync` → success.

Logout has no failure path that the caller can observe (Req 7.3). The handler returns success for a
token that never existed, and the controller answers `204 No Content` either way. `Result<bool>` is a
poor fit for an operation with no value to return — a non-generic `Result` in `BuildingBlocks` would
be the right shape, but adding one touches every service, so it is noted as a follow-up rather than
done here.

### Abstractions

Declared in `Auth.Application/Abstractions/`, implemented in `Auth.Infrastructure/`:

| Interface | Implementation | Notes |
|---|---|---|
| `IUserRepository` | `Persistence/UserRepository` | `AddAsync` (stage only), `GetByEmailAsync` (no-tracking), `GetForUpdateByEmailAsync` (tracked), `SaveChangesAsync`. Both reads funnel through a private `QueryByEmail` that normalises the input through `Email.Create`, so a lookup matches however the value was stored (Req 1.7). `SaveChangesAsync` catches `DbUpdateException` whose inner exception is a `PostgresException` with SQLSTATE `23505` and rethrows it as `DuplicateEmailException`. |
| `IPasswordHasher` | `Services/PasswordHasher` | BCrypt.Net `Hash` / `Verify` (Req 1.6). |
| `IRefreshTokenRepository` | `Persistence/RefreshTokenRepository` | `AddAsync` (stage only), `GetByHashForUpdateAsync` (tracked — every caller mutates), `GetDescendantsAsync`, `DeleteExpiredForUserAsync`. No `SaveChangesAsync` of its own: it shares the context with `IUserRepository`, and adding a second commit would invite two transactions where the design needs one. |
| `IRefreshTokenHasher` | `Services/RefreshTokenHasher` | SHA-256, hex-encoded (Req 6.2). Deliberately *not* BCrypt: the stored hash is looked up by equality, which a per-call salt would make impossible. That is safe here where it would not be for a password, because a 64-byte random token has no guessable structure to brute-force. |
| `ITokenGenerator` | `Services/TokenGenerator` | HS256; claims `sub`/`email`/`role` written with the short names verbatim (Req 2.8) because bearer validation sets `MapInboundClaims = false`; throws `InvalidOperationException` when `AccessTokenExpirationMinutes <= 0` (Req 2.9); refresh token = 64 random bytes, base64 (Req 2.10). |

### Configuration and secrets

`appsettings.json` carries `ConnectionStrings:DefaultConnection` and `Jwt:SecretKey` as **empty
strings**. They are the only two secrets the service needs, and they come from `dotnet user-secrets`
locally (the `UserSecretsId` is already on `Auth.Api.csproj`) and from the environment everywhere
else, as `ConnectionStrings__DefaultConnection` and `Jwt__SecretKey`.

The placeholders stay in the file rather than being deleted so that the shape of the configuration
is still self-documenting, and both composition-root extensions validate at startup (Req 5):
`AddInfrastructure` rejects a blank connection string and `AddAuthentication` rejects a blank or
under-32-byte signing key, a blank issuer and a blank audience. Each message names the key and the
`user-secrets` command that sets it. Failing in `builder.Build()` is the point — a service that
starts with no signing key would accept no token and fail every request one at a time instead.

`Seed:Admin` is not validated, because it is optional by design: the seeder is inert unless all five
of its keys are present (Req 4.2).

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

**`RefreshToken`** — the second aggregate. Private setters and a `RefreshToken.Issue(userId, tokenHash, expiresAtUtc)` factory, mirroring `User`. Fields: `Id` (`Guid.CreateVersion7()`), `UserId`, `TokenHash`, `CreatedAtUtc`, `ExpiresAtUtc`, nullable `RevokedAtUtc`, nullable `ReplacedByTokenId`. Behaviour: `Redeem(RefreshToken replacement)` (sets `RevokedAtUtc` and `ReplacedByTokenId` together, so a redeemed token can never lack its successor), `Revoke()` (logout — sets `RevokedAtUtc` and leaves `ReplacedByTokenId` null), and the computed `IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow`.

`ReplacedByTokenId` is what makes criterion 6.6 possible: it turns each login's tokens into a linked list, so a replayed token can be followed forward to revoke exactly its descendants and nothing else. It is also what distinguishes a redeemed token from a logged-out one (Req 7.4) — a logged-out token has no successor, so there is no chain to walk and no leak to infer.

**EF configuration** (`ApplicationDbContext.OnModelCreating`) — table `Users`, key `Id`; `FirstName`/`LastName` required, max 50; `Email` converted to `string` via `email => email.Value` / `value => Email.Create(value)`, required, max 100, with a **unique index** (Req 1.9); `PasswordHash`, `Role`, `PhoneNumber`, `RegistrationDateUtc`, `IsActive` required; `LastLoginAtUtc` nullable.

`RefreshTokens` is configured with key `Id`, a required `TokenHash` with a **unique index** (the lookup path, and a second row with the same hash would be a generator failure worth failing loudly on), a required `UserId` with a non-unique index and a cascade-delete foreign key to `Users`, required `CreatedAtUtc` and `ExpiresAtUtc`, and nullable `RevokedAtUtc` / `ReplacedByTokenId`.

**Migration** `20260624055747_InitialCreate` creates `Users` (`uuid` PK, `varchar(50)` ×2, `varchar(100)` email, `text` phone and hash, `integer` role, `timestamptz` registration date, `boolean` active, nullable `timestamptz` last login) plus `IX_Users_Email` unique. A second migration adds `RefreshTokens`.

Adding it needs one piece of scaffolding that did not exist before: because task 9 emptied the
connection string in `appsettings.json`, `dotnet ef migrations add` can no longer build the host —
`AddInfrastructure` now throws on the blank value. An `IDesignTimeDbContextFactory<ApplicationDbContext>`
in `Auth.Infrastructure` fixes this by reading the connection string from user-secrets or the
environment and falling back to a dummy one, which is sound because `migrations add` never connects
to a database. Without it the secrets fix would have quietly broken the migration workflow.

**Seeding** — `DatabaseSeeder.SeedAsync` runs from `Program.cs` before the pipeline is built. It is inert unless the whole `Seed:Admin` section is present, and skips if any `Admin` already exists (Req 4).

## Error handling

Expected failures return `Result<T>.Failure(new Error(status, details))` and the controller maps them. Unexpected exceptions reach `ExceptionMiddleware`, which writes an `ApiProblemDetails` as `application/problem+json` (`ArgumentNullException`/`InvalidOperationException` → 400, `UnauthorizedAccessException` → 401, everything else → 500 with a generic message).

| Condition | `Error.Status` | Controller result |
|---|---|---|
| Register: validation failed | 400 | `BadRequest(error)` |
| Register: email already taken (pre-check or unique index) | 400 | `BadRequest(error)` |
| Login: validation failed | 400 | `BadRequest(error)` |
| Login: unknown / inactive / wrong password | 401 | `Unauthorized(error)` |
| Profile: caller claims missing | 403 | `StatusCode(403, error)` |
| Profile: requested email malformed | 400 | `BadRequest(error)` |
| Profile: user not found | 404 | `NotFound(error)` |
| Refresh: unknown / expired / replayed / revoked / inactive user | 401 | `Unauthorized(error)` |
| Logout: any outcome at all | — | `NoContent()` |

All seven go through `ToErrorResult`, so the table is the switch rather than a list of per-action
decisions, and adding a status to a handler needs no controller change.

`ExceptionMiddleware` remains the backstop for what is genuinely unexpected. Note what it does *not*
do: a 500 body carries the fixed string "An unexpected error occurred! Please try again later.", never
`ex.Message`, so a Npgsql failure cannot echo a connection string back to the caller. Only the 400
branch reports the exception's own message, and only because `ArgumentNullException` and
`InvalidOperationException` at that layer describe the caller's input.

## Testing strategy

`tests/Auth.Tests` — xUnit + Moq only. No FluentAssertions, no `WebApplicationFactory`, no Testcontainers; everything is a pure unit test.

- **Controller tests** mock `ISender`, construct `AuthController` directly, and assert on the concrete `IActionResult` type. For the `[Authorize]` action, a `ClaimsPrincipal` is built by hand and assigned through `DefaultHttpContext` → `ControllerContext` — `AuthControllerTests.GetProfile_Successful_Returns_Ok` is the pattern to copy.
- **Handler tests** mock `IUserRepository`, `IPasswordHasher`, `ITokenGenerator`, `IValidator<T>` and `ILogger<T>`, and verify both the returned `Result` and the repository interactions (`Verify(r => r.SaveChangesAsync(...), Times.Never)` is how "rejected without writing" is asserted).
- **`TokenGeneratorTests`** is the one test that exercises a real Infrastructure type, reading the produced JWT back with `JwtSecurityTokenHandler` to assert claim names, lifetime and issuer/audience.

- **Value-object and service tests** (`EmailTests`, `PasswordHasherTests`) exercise the real types with no mocks at all, because neither has a dependency to mock.
- **Validator tests** (`RegisterCustomerCommandValidatorTests`, `LoginUserRequestValidatorTests`) construct the validator directly and assert per rule, starting from one valid command and breaking one field at a time. `LoginUserRequestValidatorTests.Login_Does_Not_Apply_The_Registration_Password_Rules` pins the asymmetry deliberately: login must accept any non-empty password.
- **`ExceptionMiddlewareTests`** builds a `DefaultHttpContext` with a `MemoryStream` body and passes a `RequestDelegate` that throws, then reads the serialised `ApiProblemDetails` back. This is the closest thing to a pipeline test in the suite and still needs no host.

- **Refresh and logout tests** mock `IRefreshTokenRepository` and `IRefreshTokenHasher` the same way the existing handler tests mock `IUserRepository`. The cases that matter most are the ones asserting what does *not* happen: a replayed token revokes its descendants and still returns 401; a logout for an unknown token reports success without writing; redemption stages the revocation and the replacement before a single `SaveChangesAsync`, which `Verify(..., Times.Once)` pins.
- **`RefreshTokenTests`** covers the aggregate directly — `Redeem` sets both `RevokedAtUtc` and `ReplacedByTokenId`, `Revoke` sets only the former, and `IsActive` is false once either expiry or revocation applies.

88 executed cases across 9 files, up from 28 across 5. Coverage maps to the criteria cited inline in
[`requirements.md`](requirements.md). `UserRepository`, `DatabaseSeeder` and `LoggingMiddleware` are
still uncovered: each needs a real PostgreSQL instance or a full request pipeline, and forcing them
into this style would test the mocks rather than the code. They wait for an integration-test harness.

New work on this feature follows the same style — mock and assert on `IActionResult` — rather than introducing integration tests, unless the task explicitly calls for them.
