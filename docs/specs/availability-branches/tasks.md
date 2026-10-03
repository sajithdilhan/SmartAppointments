# Availability branches — Tasks

> Tasks 1–6 deliver Requirement 1 together with the parts of Requirements 5 and 6 that it cannot ship without. Tasks 7–9 deliver Requirements 2–4 and the rest of Requirement 5. Each task leaves the solution building and `dotnet test SmartAppointments.slnx` green.
>
> All nine tasks are done. Tasks 1–6: besides their unit tests, the flow was verified end to end against local PostgreSQL: both migrations applied, the seeded admin logged in to Auth, and that token created a branch through Availability (`201`, with the code trimmed and upper-cased and a blank description stored as null). Posting the same code again returned `409`, and a customer token returned `403`. Before the database was configured, 401 for a missing or wrongly signed token, 400 listing every failed rule, a non-leaking 500 and 503 from `/healthz` were checked by hand.
>
> Tasks 7–9 were verified the same way against the running services: `PUT` returned `200` with the new details and `UpdatedAtUtc` set, kept the code when the body carried a different one, and returned `400`, `404` and, for a customer token, `403`. `deactivate` and `activate` each returned `204` twice in a row and `404` for an unknown id. An inactive branch was `404` to a customer and `200` to an admin, appeared in the list only for an admin with `includeInactive=true`, and the list was `401` without a token and ordered by name. The suite now has 106 Availability tests.

## Requirement 1 and the service foundations

- [x] 1. Move secrets out of source control
  - Blank `ConnectionStrings:DefaultConnection` and `Jwt:SecretKey` in `Availability.Api/appsettings.json`; drop the token-lifetime keys this service never reads
  - Give `Availability.Api` its own `UserSecretsId` and document the two keys in `CLAUDE.md` beside Auth's, noting that `Jwt:SecretKey` must equal Auth's
  - The committed key is also in history and must be rotated in every environment that uses it — that is an operational step, not a code change
  - _Known gap 1; Requirements: 6.2_

- [x] 2. `Branch` aggregate
  - Add `Code`, nullable `Description`, `IsActive`, `CreatedAtUtc`, nullable `UpdatedAtUtc`; `Create` trims, upper-cases the code, and stores a blank description as null
  - `Branch.NormaliseCode` as the single normalisation rule the handler also uses
  - `BranchTests`: defaults on create, trimming, code upper-casing, blank description → null
  - _Known gap 4; Requirements: 1.2, 1.4_

- [x] 3. Persistence
  - `ApplicationDbContext` with the `Branches` configuration and unique index `IX_Branches_Code`
  - `IBranchRepository` (`AddAsync`, `ExistsByCodeAsync`, `SaveChangesAsync`) and `DuplicateBranchCodeException` in `Application/Abstractions`; `BranchRepository` translates SQLSTATE `23505` into that exception
  - `DesignTimeDbContextFactory` reading user-secrets, then the environment, then a placeholder
  - `CreateBranches` migration
  - `AddInfrastructure(configuration)`: fail-fast connection string, DbContext, repository, Npgsql health check
  - _Requirements: 1.6, 1.7, 6.1, 6.3, 6.5, 6.9_

- [x] 4. Service wiring
  - Copy Auth's `AddAuthentication` and `AddAuthorizationWithRoles` into `Availability.Application`, with a validation-only `JwtOptions`; register the OpenAPI bearer scheme in `AddApplication`
  - Copy `ExceptionMiddleware` and `LoggingMiddleware` into `Availability.Api`
  - Rewrite `Program.cs` in Auth's composition and pipeline order, including Scalar (dev only) and `/healthz`
  - Replace the template `Availability.Api.http`
  - _Known gaps 2, 5, 7; Requirements: 5.1, 5.2, 5.3, 5.4, 6.3, 6.4, 6.6, 6.8_

- [x] 5. Create a branch
  - `CreateBranchRequest`, `CreateBranchCommand`, `CreateBranchCommandValidator`, `CreateBranchCommandHandler`, `BranchResponse` (replaces `BranchDto`)
  - `POST /api/branches` under `AdminPolicy`, returning `201 Created` with `Location: /api/branches/{id}`
  - `ToErrorResult(Error)` in `BranchesController`, identical to Auth's
  - _Requirements: 1.1, 1.3, 1.4, 1.5, 1.6, 6.7_

- [x] 6. `Availability.Tests`
  - New xUnit + Moq project in `tests/`, added to `SmartAppointments.slnx`
  - `CreateBranchCommandValidatorTests`: every rule at its boundary
  - `CreateBranchCommandHandlerTests`: stage then commit; duplicate pre-check → 409 without writing; the pre-check receives the normalised code; commit-time duplicate → 409 not 500; invalid input → 400 without writing
  - `BranchesControllerTests`: `201` and its `Location`; 400 and 409 mapping; `Create` carries `AdminPolicy`
  - `ExceptionMiddlewareTests`: a `500` does not echo the exception message
  - Apply the migration to a local PostgreSQL once and create a branch through the running service with an Auth-issued admin token
  - _Requirements: 1.1–1.6, 5.3, 6.6, 6.7_

## Requirements 2–4

- [x] 7. Update a branch
  - `Branch.UpdateDetails`; `IBranchRepository.GetForUpdateByIdAsync`
  - `UpdateBranchRequest` (no `Code`), `UpdateBranchCommand`, `UpdateBranchCommandValidator`, `UpdateBranchCommandHandler`
  - `PUT /api/branches/{id}` under `AdminPolicy`
  - Tests: happy path sets `UpdatedAtUtc`; unknown id → 404; `IsActive` and `Code` untouched; inactive branch still updatable; validator rules
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_

- [x] 8. Deactivate and reactivate a branch
  - `Branch.Activate` / `Deactivate` returning whether they changed anything
  - `SetBranchActiveCommand` and handler; `POST /api/branches/{id}/activate` and `/deactivate` under `AdminPolicy`, returning `204`
  - Tests: state flips and `UpdatedAtUtc` set; a repeat call returns 204 without `SaveChangesAsync`; unknown id → 404
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

- [x] 9. Read branches
  - `IBranchRepository.GetByIdAsync` and `ListAsync`; replace the placeholder `GetBranchesHandler` with a real one; add `GetBranchQuery` and handler
  - `GET /api/branches` and `GET /api/branches/{id}` under `AllowedOriginsPolicy`, passing the caller's `role` claim into the query
  - Switch `Create` to `CreatedAtAction(nameof(GetBranch), ...)`
  - Tests: non-admin never sees inactive branches, whether or not `includeInactive` is set; admin with `includeInactive` does; inactive branch → 404 for non-admin and 200 for admin; the handler keeps the repository's ordering and reads through the no-tracking lookup. Ordering and `AsNoTracking` themselves live in `BranchRepository`, which waits for the integration-test harness like the rest of the repository
  - _Known gap 3; Requirements: 4.1–4.8, 5.5_
