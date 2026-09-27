# Availability branches — Tasks

> Tasks 1–6 deliver Requirement 1 together with the parts of Requirements 5 and 6 that it cannot ship without. Tasks 7–9 are the backlog for Requirements 2–4. Each task leaves the solution building and `dotnet test SmartAppointments.slnx` green.
>
> Tasks 1–5 are done. Task 6 is open only for its last step: its unit tests are written and pass (54 cases), but the migration has not yet been applied to a real database and no branch has been created with an Auth-issued token, because no local database credentials were configured when this was built. Everything that does not need the database was checked by hand against the running service: 401 without a token or with a token signed by the wrong key, 403 for Customer and Staff, 400 with every failed rule listed, a 500 that does not leak its message, 503 from `/healthz`, and the Scalar UI.

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

- [ ] 6. `Availability.Tests`
  - New xUnit + Moq project in `tests/`, added to `SmartAppointments.slnx`
  - `CreateBranchCommandValidatorTests`: every rule at its boundary
  - `CreateBranchCommandHandlerTests`: stage then commit; duplicate pre-check → 409 without writing; the pre-check receives the normalised code; commit-time duplicate → 409 not 500; invalid input → 400 without writing
  - `BranchesControllerTests`: `201` and its `Location`; 400 and 409 mapping; `Create` carries `AdminPolicy`
  - `ExceptionMiddlewareTests`: a `500` does not echo the exception message
  - Apply the migration to a local PostgreSQL once and create a branch through the running service with an Auth-issued admin token
  - _Requirements: 1.1–1.6, 5.3, 6.6, 6.7_

## Backlog

- [ ] 7. Update a branch
  - `Branch.UpdateDetails`; `IBranchRepository.GetForUpdateByIdAsync`
  - `UpdateBranchRequest` (no `Code`), `UpdateBranchCommand`, `UpdateBranchCommandValidator`, `UpdateBranchCommandHandler`
  - `PUT /api/branches/{id}` under `AdminPolicy`
  - Tests: happy path sets `UpdatedAtUtc`; unknown id → 404; `IsActive` and `Code` untouched; inactive branch still updatable; validator rules
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_

- [ ] 8. Deactivate and reactivate a branch
  - `Branch.Activate` / `Deactivate` returning whether they changed anything
  - `SetBranchActiveCommand` and handler; `POST /api/branches/{id}/activate` and `/deactivate` under `AdminPolicy`, returning `204`
  - Tests: state flips and `UpdatedAtUtc` set; a repeat call returns 204 without `SaveChangesAsync`; unknown id → 404
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

- [ ] 9. Read branches
  - `IBranchRepository.GetByIdAsync` and `ListAsync`; replace the placeholder `GetBranchesHandler` with a real one; add `GetBranchQuery` and handler
  - `GET /api/branches` and `GET /api/branches/{id}` under `AllowedOriginsPolicy`, passing the caller's `role` claim into the query
  - Switch `Create` to `CreatedAtAction(nameof(GetBranch), ...)`
  - Tests: non-admin never sees inactive branches, whether or not `includeInactive` is set; admin with `includeInactive` does; inactive branch → 404 for non-admin and 200 for admin; ordering; no-tracking queries
  - _Known gap 3; Requirements: 4.1–4.8, 5.5_
