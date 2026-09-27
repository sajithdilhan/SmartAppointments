# Availability branches — Design

> References like _(Req 1.3)_ point at requirement 1, criterion 3 in [`requirements.md`](requirements.md). This design covers the whole feature, so that the repository and the model are shaped once, but [`tasks.md`](tasks.md) builds it one requirement at a time.

## Overview

This is the first real feature in the Availability service, so most of the work is making the service a copy of Auth's runtime shape: the same four-project split, the same DI composition order, the same middleware, the same fail-fast secret handling, and the same `Result<T>` → `ActionResult` mapping. The branch feature on top of that is small.

Three decisions shape it:

- **The code is the natural key; the GUID is the surrogate key.** `Code` is unique, upper-cased and immutable (Req 1.4, 1.5, 2.3). Uniqueness is enforced twice, as Auth does for email: a cheap no-tracking pre-check gives the common case a clean `409`, and a unique index catches a concurrent create. The repository translates that index violation into `DuplicateBranchCodeException`, so the Application layer never references EF Core (Req 1.6, 1.7).
- **Authorization narrowing lives in the handler.** `[Authorize]` decides whether a caller may reach an endpoint at all. The `includeInactive` rule and the "inactive branch looks missing" rule (Req 4.3, 4.5) depend on the caller's role *and* the data, so the controller passes the caller's role into the query and the handler decides. This is the same split Auth uses for profiles.
- **JWT validation is copied, not shared.** Availability needs exactly the bearer validation and the five policies that Auth registers (Req 5.1, 5.4). Moving them into `BuildingBlocks` is the right end state, but it would also change Auth, which this feature has no business touching. The copy is deliberate and is listed under *Open questions*.

## Architecture

| Layer | Contents |
|---|---|
| `Availability.Api` | `BranchesController`; `ExceptionMiddleware`, `LoggingMiddleware` (copies of Auth's); `Program.cs` composition root; Scalar (dev only); `/healthz` |
| `Availability.Application` | `Commands/`, `Queries/`, `Handlers/`, `Validations/`, `Models/` (requests, `BranchResponse`, `JwtOptions`), `Abstractions/` (`IBranchRepository`, `DuplicateBranchCodeException`); `AddApplication()`, `AddAuthentication()`, `AddAuthorizationWithRoles()` |
| `Availability.Domain` | `Branch` aggregate. No EF or ASP.NET references. |
| `Availability.Infrastructure` | `ApplicationDbContext`, `DesignTimeDbContextFactory`, `BranchRepository`, migrations; `AddInfrastructure(configuration)` |

`Program.cs` composes in Auth's order — `AddApplication()` → `AddInfrastructure(config)` → `AddControllers()` → `AddAuthentication(config)` → `AddAuthorizationWithRoles()` — and builds the pipeline `ExceptionMiddleware` → `LoggingMiddleware` → HTTPS redirection → authentication → authorization → controllers → `/healthz`. There is no seeder.

## Components and interfaces

### Endpoints

The controller keeps its existing `[Route("api/[controller]")]`, so the routes are `/api/Branches/...`. ASP.NET matches them case-insensitively, so `/api/branches` works too.

| Verb / route | Action | Authorization | Dispatches | Req |
|---|---|---|---|---|
| `POST /api/branches` | `Create(CreateBranchRequest, ct)` | `AdminPolicy` | `CreateBranchCommand(Code, Name, Description, Address, PhoneNumber)` | 1 |
| `PUT /api/branches/{id}` | `Update(Guid id, UpdateBranchRequest, ct)` | `AdminPolicy` | `UpdateBranchCommand(Id, Name, Description, Address, PhoneNumber)` | 2 |
| `POST /api/branches/{id}/deactivate` | `Deactivate(Guid id, ct)` | `AdminPolicy` | `SetBranchActiveCommand(Id, IsActive: false)` | 3 |
| `POST /api/branches/{id}/activate` | `Activate(Guid id, ct)` | `AdminPolicy` | `SetBranchActiveCommand(Id, IsActive: true)` | 3 |
| `GET /api/branches?includeInactive=` | `GetBranches(bool includeInactive, ct)` | `AllowedOriginsPolicy` | `GetBranchesQuery(IncludeInactive, CurrentUserRole)` | 4 |
| `GET /api/branches/{id}` | `GetBranch(Guid id, ct)` | `AllowedOriginsPolicy` | `GetBranchQuery(Id, CurrentUserRole)` | 4 |

`Create` returns `201 Created` with the `Location` header `/api/branches/{id}` (Req 1.1). Until `GetBranch` exists it uses `Created(uri, value)` with a literal path. When Requirement 4 adds `GetBranch`, it switches to `CreatedAtAction(nameof(GetBranch), ...)`, as Auth's `Register` does.

`UpdateBranchRequest` has no `Code` property. A `Code` field in the JSON body is dropped by the model binder, which is how Req 2.3 is met without a special rule.

Every failure path goes through a private `ToErrorResult(Error)` that is the same as Auth's: 400, 401, 403, 404 and 409 each map to their own result, and anything else to 500 (Req 6.7).

### Commands, queries and handlers

| Request | Handler | Returns |
|---|---|---|
| `CreateBranchCommand(string Code, string Name, string? Description, string Address, string PhoneNumber)` | `CreateBranchCommandHandler` | `Result<BranchResponse>` |
| `UpdateBranchCommand(Guid Id, string Name, string? Description, string Address, string PhoneNumber)` | `UpdateBranchCommandHandler` | `Result<BranchResponse>` |
| `SetBranchActiveCommand(Guid Id, bool IsActive)` | `SetBranchActiveCommandHandler` | `Result<bool>` (value unused; the controller returns `204`) |
| `GetBranchesQuery(bool IncludeInactive, string? CurrentUserRole)` | `GetBranchesHandler` | `Result<List<BranchResponse>>` |
| `GetBranchQuery(Guid Id, string? CurrentUserRole)` | `GetBranchHandler` | `Result<BranchResponse>` |

`CreateBranchCommandHandler` runs these steps in order:

1. Validate. On failure, return `Error(400, "Invalid request data. Errors: ...")` listing every failed rule, and write nothing (Req 1.3).
2. Normalise the code with `Branch.NormaliseCode`, the same function the entity uses, so that the lookup and the stored value can't drift apart (Req 1.4).
3. Call `ExistsByCodeAsync(code)`. If it returns true, return `Error(409, "A branch with code '{code}' already exists.")` and write nothing (Req 1.5).
4. Call `Branch.Create(...)`, then `AddAsync`, then `SaveChangesAsync`. If the commit throws `DuplicateBranchCodeException`, return the same `409` (Req 1.6).
5. Return `BranchResponse.From(branch)`.

`BranchResponse(Guid Id, string Code, string Name, string? Description, string Address, string PhoneNumber, bool IsActive, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc)` replaces the scaffold's `BranchDto`, which lacked `Description` (Req 4.6).

### Abstractions

```csharp
public interface IBranchRepository
{
    Task AddAsync(Branch branch, CancellationToken ct);                          // stages only
    Task<bool> ExistsByCodeAsync(string normalisedCode, CancellationToken ct);   // no tracking
    Task<Branch?> GetByIdAsync(Guid id, CancellationToken ct);                   // no tracking — Req 4
    Task<Branch?> GetForUpdateByIdAsync(Guid id, CancellationToken ct);          // tracked — Req 2, 3
    Task<List<Branch>> ListAsync(bool includeInactive, CancellationToken ct);    // no tracking, ordered — Req 4
    Task SaveChangesAsync(CancellationToken ct);                                 // throws DuplicateBranchCodeException
}
```

Staging and committing are separate, as in Auth, so that a later outbox insert can share the transaction. Each method is added in the task that first needs it, so `GetByIdAsync`, `GetForUpdateByIdAsync` and `ListAsync` wait for Requirements 2–4.

`DuplicateBranchCodeException` lives in `Application/Abstractions`, beside the interface whose contract it is part of, as `DuplicateEmailException` does in Auth.

### Validation

`CreateBranchCommandValidator`:

| Field | Rule | Req |
|---|---|---|
| `Code` | Not empty. After trimming, matches `^[A-Za-z0-9](?:[A-Za-z0-9-]{0,8}[A-Za-z0-9])$`: 2–10 characters, letters, digits and hyphens, no hyphen at either end. The trim happens before the pattern, so ` pg ` is accepted and stored as `PG`. | 1.3, 1.4 |
| `Name` | Not empty, at most 100 characters | 1.3 |
| `Description` | At most 500 characters when not null | 1.3 |
| `Address` | Not empty, at most 200 characters | 1.3 |
| `PhoneNumber` | Not empty, matches `^\+?[1-9]\d{1,14}$` | 1.3 |

Every rule has an explicit `.WithMessage(...)`, matching Auth's validators. `UpdateBranchCommandValidator` will repeat the four non-code rules (Req 2.2). The length limits apply to the raw value, which is stricter than applying them after trimming and guarantees the trimmed value fits its column.

### Service wiring

- **`JwtOptions`** in `Availability.Application.Models` has only `Issuer`, `Audience` and `SecretKey`. This service validates tokens and never issues them, so the lifetime settings have no meaning here.
- **`AddAuthentication(configuration)`** is Auth's method copied as-is: fail on a missing or blank key, a key shorter than 32 bytes, or a blank issuer or audience. It sets `MapInboundClaims = false`, zero clock skew and `role` as the role claim type (Req 5.1, 6.3, 6.4).
- **`AddAuthorizationWithRoles()`** is also a copy of Auth's, with the same five policies (Req 5.4).
- **`AddInfrastructure(configuration)`** fails with a message giving the user-secrets command when the connection string is blank (Req 6.3). It registers `ApplicationDbContext` with Npgsql, `IBranchRepository`, and `AddHealthChecks().AddNpgSql(connectionString)` (Req 6.5).
- **`Availability.Api.csproj`** gets its own `UserSecretsId`. `appsettings.json` keeps `Jwt:Issuer` and `Jwt:Audience`, which must equal Auth's, and blanks `Jwt:SecretKey` and `ConnectionStrings:DefaultConnection` (Req 6.2). Locally the secret key is set to the same value as Auth's.

## Data model

```csharp
public sealed class Branch
{
    public Guid Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string Address { get; private set; }
    public string PhoneNumber { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static Branch Create(string code, string name, string? description, string address, string phoneNumber);
    public static string NormaliseCode(string code);        // Trim().ToUpperInvariant()
    public void UpdateDetails(string name, string? description, string address, string phoneNumber);   // Req 2
    public bool Activate();    // returns false if already active   — Req 3
    public bool Deactivate();  // returns false if already inactive — Req 3
}
```

`Create` assigns `Guid.CreateVersion7()`, sets `IsActive = true` and `CreatedAtUtc = DateTime.UtcNow`, trims every field, upper-cases the code, and stores a blank description as `null` (Req 1.2, 1.4). `Activate` and `Deactivate` return whether they changed anything, so the handler can skip the write when nothing changed (Req 3.3).

EF Core configuration, `Branches` table:

| Column | Configuration |
|---|---|
| `Id` | key |
| `Code` | required, max 10, **unique index `IX_Branches_Code`** (Req 1.7) |
| `Name` | required, max 100 |
| `Description` | optional, max 500 |
| `Address` | required, max 200 |
| `PhoneNumber` | required, max 16 (E.164 is at most 15 digits plus `+`) |
| `IsActive` | required |
| `CreatedAtUtc` | required |
| `UpdatedAtUtc` | optional |

No index on `Name`: it is neither unique (Req 1.8) nor searched, and ordering tens of rows needs no index.

The migration `CreateBranches` lives in `Availability.Infrastructure/Persistence/Migrations`. `DesignTimeDbContextFactory` implements `IDesignTimeDbContextFactory<ApplicationDbContext>`. It reads `ConnectionStrings:DefaultConnection` from the API project's user-secrets and then from the environment, and falls back to a placeholder that is never opened. That lets `dotnet ef migrations add` run with no database and no secrets (Req 6.9). This is the same design as Auth's pending task 15.

## Error handling

| Condition | `Error.Status` | Controller result | Req |
|---|---|---|---|
| Validation fails | 400 | `BadRequestObjectResult` | 1.3, 2.2 |
| Duplicate code, found by the pre-check or at commit | 409 | `ConflictObjectResult` | 1.5, 1.6 |
| Branch not found, or inactive and the caller is not an admin | 404 | `NotFoundObjectResult` | 2.4, 3.4, 4.5 |
| No token or an invalid token | — | `401` from the authentication middleware | 5.2 |
| Wrong role | — | `403` from the authorization middleware | 5.3 |
| Anything unexpected | — | `ExceptionMiddleware` → `problem+json` `500` | 6.6 |

## Testing strategy

A new `tests/Availability.Tests` project mirrors `Auth.Tests`: xUnit and Moq, no database, no `WebApplicationFactory`.

| Test class | Covers |
|---|---|
| `BranchTests` | Create defaults and normalisation; later update and activation behaviour (Req 1.2, 1.4, 2.5, 3.3) |
| `CreateBranchCommandValidatorTests` | Every rule, boundary by boundary (Req 1.3) |
| `CreateBranchCommandHandlerTests` | Stage-then-commit; the duplicate pre-check writes nothing and returns 409; the check uses the normalised code; a commit-time duplicate returns 409, not 500; invalid input writes nothing (Req 1.3–1.6) |
| `BranchesControllerTests` | `201` with the right `Location`; each `Error.Status` maps to its result; the admin endpoints carry `AdminPolicy` and the read endpoints `AllowedOriginsPolicy`, checked by reflecting over the `[Authorize]` attributes (Req 1.1, 5.3, 6.7) |
| `ExceptionMiddlewareTests` | `500` body does not echo the exception message (Req 6.6) |

The unique index, the migration and `BranchRepository` itself are not unit-tested. As in Auth, they wait for the integration-test harness. Instead, the migration is exercised by hand once against a local PostgreSQL before the Requirement 1 tasks are checked off.

## Open questions

1. **Shared auth wiring.** `AddAuthentication`, `AddAuthorizationWithRoles`, `ExceptionMiddleware` and `LoggingMiddleware` now exist in two services, and there will be four more. Moving them into `BuildingBlocks` (which would then need an ASP.NET Core framework reference) should be its own small spec that touches both services at once, and should happen before the Booking service is scaffolded.
2. **`ExceptionMiddleware` maps `InvalidOperationException` to 400 and echoes its message.** This is inherited from Auth. It is harmless today, because no handler throws one on a request path, but a future library exception could leak its text. Worth fixing in the shared version from question 1 rather than in both copies.
