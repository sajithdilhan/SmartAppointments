# Staff management — Tasks

> Each task leaves `dotnet build SmartAppointments.slnx` and `dotnet test SmartAppointments.slnx` green. Do them in order within a chunk. All code is in the Auth service and `tests/Auth.Tests`, in the xUnit + Moq style. The design is final; ADR-0008 and ADR-0009 are Accepted.

## Chunk A — Lifecycle

- [ ] 1. Domain: soft delete and the sign-in rule
  - `User.DeletedAtUtc` (`DateTime?`, private set), `IsDeleted`, `CanSignIn` (`IsActive && !IsDeleted`)
  - `MarkDeleted(DateTime nowUtc)`: sets `DeletedAtUtc` and `IsActive = false`; throws `InvalidOperationException` for a non-staff or already deleted user
  - `Activate()` throws `InvalidOperationException` when the user is deleted; `Deactivate()` unchanged
  - `UserTests`: `MarkDeleted` sets both fields and throws in both invalid cases; `CanSignIn` for active, inactive, deleted, and deleted with `IsActive` forced back to true by reflection (the state the activate/delete race can leave); `Activate` throws when deleted
  - _Requirements: 6.1, 6.2, 6.8_

- [ ] 2. Persistence: column, migration and the shared transactional save
  - `ApplicationDbContext`: map `DeletedAtUtc` (nullable); `IsDeleted` and `CanSignIn` not mapped
  - Migration `AddUserDeletedAt` (`dotnet ef migrations add AddUserDeletedAt --project src/Services/Auth/Auth.Infrastructure --startup-project src/Services/Auth/Auth.Api`): one nullable column, no index, no backfill
  - Rename `IUserRepository.SaveBranchMoveAsync` → `SaveAndRevokeSessionsAsync` (interface, `UserRepository`, its doc comment per the design), and update `AssignStaffBranchHandler` and `AssignStaffBranchHandlerTests` to the new name; behaviour unchanged
  - _Requirements: 4.1, 6.1, 6.2_

- [ ] 3. Staff shape
  - `StaffResponse` gains `IsActive` and `RegisteredAtUtc` after `BranchId`; `From` maps `user.IsActive` and `user.RegistrationDateUtc`
  - Update `CreateStaffHandlerTests`, `AssignStaffBranchHandlerTests` and `StaffControllerTests` that build or assert a `StaffResponse`; add assertions that the new fields are mapped and that no deletion time is exposed
  - _Requirements: 1.1, 1.2, 1.3, 7.3_

- [ ] 4. Sign-in and lookups for deleted accounts
  - `LoginUserHandler` and `RefreshTokenCommandHandler`: `!user.IsActive` → `!user.CanSignIn` (same `401`, refresh still revokes the family)
  - `GetCustomerByEmailHandler`: a deleted user gives the existing `404 "Customer not found"`
  - Tests: login and refresh `401` for a deleted user and for a deleted user whose `IsActive` is true (the activate/delete race); profile `404` for a deleted user; `CreateStaffHandlerTests` and `RegisterCustomerCommandHandlerTests` give `400 "Email is already registered."` when `GetByEmailAsync` returns a deleted user; `LookupUsersHandlerTests` still returns a deleted user
  - _Requirements: 4.3, 6.3, 6.4, 6.5, 6.6_

- [ ] 5. Move: deleted and inactive accounts
  - `AssignStaffBranchHandler`: `404` when `user.IsDeleted`; no `IsActive` check
  - Tests: `404` for a deleted user with no Availability call and no save; an inactive user is moved (`200`, `SaveAndRevokeSessionsAsync` called) and stays inactive; an active move unchanged
  - _Requirements: 7.1, 7.2, 7.3_

- [ ] 6. Activate and deactivate
  - `SetStaffActiveCommand(Guid Id, bool IsActive) : IRequest<Result<Unit>>` and `SetStaffActiveHandler(IUserRepository, IAvailabilityClient, TimeProvider, ILogger)` per the design
  - `SetStaffActiveHandlerTests`: deactivate saves through `SaveAndRevokeSessionsAsync` with the `TimeProvider` instant, also when already inactive, and never calls Availability; activate of an inactive user checks its branch, `422` and `503` leave it inactive with no save, success calls `Activate` and plain `SaveChangesAsync` (no revocation); activate of an active user makes no Availability call and no save; `404` (no save) for unknown, customer, admin and deleted ids in both directions
  - _Requirements: 4.1, 4.2, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3, 5.4, 5.5, 8.3_

- [ ] 7. Delete
  - `DeleteStaffCommand(Guid Id) : IRequest<Result<Unit>>` and `DeleteStaffHandler(IUserRepository, TimeProvider, ILogger)` per the design
  - `DeleteStaffHandlerTests`: active and inactive users are marked deleted with the `TimeProvider` instant and saved through `SaveAndRevokeSessionsAsync`; `404` (no save) for unknown, customer, admin and already deleted ids
  - _Requirements: 6.1, 6.2, 6.3, 6.7, 8.3_

- [ ] 8. Lifecycle actions on `StaffController`
  - `POST {id:guid}/deactivate`, `POST {id:guid}/activate` (both through one private helper, like `BranchesController.SetActive`) and `DELETE {id:guid}`: `NoContent()` on success, `this.ToActionResult` on failure; `[ProducesResponseType]` for `204`, `404`, and `422`/`503` on activate; XML summaries
  - `StaffControllerTests`: each action dispatches the right command and returns `NoContentResult`; `404`, `422` and `503` map to the problem result; reflection shows the routes and that the class-level `AdminPolicy` covers them
  - _Requirements: 4.1, 5.2, 6.1, 8.1, 8.2, 8.5_

## Chunk B — Reads

- [ ] 9. List parameters, query and validator
  - `StaffListParameters` (internal static, `Auth.Application/Models`): `TryParseBranchId`, `TryParseIsActive`, `TryParsePage`, `TryParsePageSize` with `DefaultPage = 1`, `DefaultPageSize = 20`, `MaxPageSize = 100`, per the design's table
  - `ListStaffQuery(string? BranchId, string? IsActive, string? Page, string? PageSize) : IRequest<Result<StaffPage>>`; `StaffPage(Items, Page, PageSize, TotalCount)`
  - `ListStaffQueryValidator` (one rule and message per parameter) registered in `AddApplication()`
  - `StaffListParametersTests` (accepted, rejected and default values incl. `+1`, `1.0`, `-1`, `""`, `TRUE`, `1`, `Guid.Empty`, `xyz`) and `ListStaffQueryValidatorTests` (each rule alone; `isActive=x`, `branchId=xyz`, `page=0` together give three messages)
  - _Requirements: 2.3, 2.4, 2.5, 2.9, 2.10_

- [ ] 10. List repository query
  - `StaffListPage(IReadOnlyList<User> Items, int TotalCount)` beside `IUserRepository`; `ListStaffAsync(Guid? branchId, bool? isActive, int page, int pageSize, CancellationToken)`
  - `UserRepository.ListStaffAsync`: no tracking; `Role == Staff && DeletedAtUtc == null`, optional branch and active filters (AND); `CountAsync`, then order by `RegistrationDateUtc`, `Id`, `Skip`/`Take`
  - Update any test double implementing `IUserRepository` so the solution builds
  - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.11_

- [ ] 11. List handler
  - `ListStaffHandler(IUserRepository, IValidator<ListStaffQuery>)`: validate (one `Error(400, "Invalid request data. Errors: ...")`, repository untouched on failure), parse, call `ListStaffAsync`, map with `StaffResponse.From` into `StaffPage`
  - `ListStaffHandlerTests`: defaults; parsed `branchId`, `isActive`, `page` and `pageSize` reach the repository; mapping and `totalCount`; empty and beyond-the-end pages are `200`; invalid input never calls the repository
  - _Requirements: 2.1, 2.3, 2.4, 2.5, 2.6, 2.7, 2.9, 2.10_

- [ ] 12. Read one
  - `GetStaffQuery(Guid Id) : IRequest<Result<StaffResponse>>` and `GetStaffHandler(IUserRepository)` over `GetByIdAsync`
  - `GetStaffHandlerTests`: active and inactive staff `200` with the shape; `404` for unknown, customer, admin and deleted ids
  - _Requirements: 3.1, 3.2, 8.3_

- [ ] 13. List and get actions; create's `Location`
  - `GET` (`List`): `[FromQuery] string?` `branchId`, `isActive`, `page`, `pageSize` through a private `Raw(name, bound)` helper (copied from Booking's `AppointmentsController`), `Ok(page)` or `this.ToActionResult`
  - `GET {id:guid}` (`Get`): `Ok` or `this.ToActionResult`
  - `Create`: `CreatedAtAction(nameof(Get), new { id = value.Id }, value)`; remove the "No staff GET exists" comment
  - `[ProducesResponseType]` and XML summaries on the new actions
  - `StaffControllerTests`: `List` passes the raw strings (and `""` for an empty value) and returns `OkObjectResult`, `400` maps to the problem result; `Get` `200` and `404`; `Create` returns `CreatedAtActionResult` with action `Get` and the new id; reflection for routes and the `{id:guid}` constraint
  - _Requirements: 2.1, 2.10, 3.1, 3.2, 3.3, 3.4, 8.1, 8.2, 8.5_

## Chunk C — Delivery (orchestrator)

- [ ] 14. Requests and docs
  - `Auth.Api.http` and the gateway's `SmartAppointments.Gateway.http`: list (with filters and a bad-value example), get, activate, deactivate, delete
  - `docs/requirements.md`: widen FR-AUTH-006 and add the endpoints to the Auth endpoint draft (section 13)
  - `CLAUDE.md` Auth description; `docs/specs/README.md` row
  - _Requirements: 8.4, 8.5_

- [ ] 15. Manual verification through the gateway
  - The plan's end-to-end checks on `docker compose up --build`, results recorded at the top of this file
  - _Requirements: 2.2, 2.8, 2.11, 3.4, 4.3, 4.4, 5.2, 6.3, 6.4, 6.6, 8.1, 8.2, 8.4_

## Execution chunks

- **A (tasks 1–8)** then **B (tasks 9–13)**, one implementer each, **in sequence**: both edit `StaffController`, `StaffControllerTests`, `IUserRepository` and `UserRepository`, and B relies on A's `DeletedAtUtc`, `StaffResponse` fields and test-double updates. They cannot run in parallel.
- **C (tasks 14–15)** is the orchestrator's, after A and B are committed.
