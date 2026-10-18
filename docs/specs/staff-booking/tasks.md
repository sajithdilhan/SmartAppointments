# Staff booking — Tasks

> Each task leaves the solution building and `dotnet test SmartAppointments.slnx` green, and is ticked in the same commit as its code. Chunks A, B and C run in parallel after task 0; chunk D follows C (it edits the same controller and repository); chunk E is last. The design is final; any deviation is recorded at the end of `design.md`.

## Chunk 0 — Shared constant (orchestrator)

- [x] 0. `Constants.BranchIdClaimType = "branch_id"` in `SmartAppointments.BuildingBlocks/Constants.cs`
  - _Requirements: 3.1_

## Chunk A — Availability internal branch endpoint

- [x] 1. Internal branch lookup
  - `InternalBranchResponse(Id, IsActive, TimeZoneId)` with `From(Branch)`; `GetInternalBranchQuery(Guid Id)` and `GetInternalBranchHandler` (inactive included, `BranchErrors.NotFound` for unknown)
  - `InternalBranchesController` (`internal/branches`, class-level `InternalServicePolicy`, `GET {id:guid}`)
  - Tests: `GetInternalBranchHandlerTests`, `InternalBranchesControllerTests` (policy by reflection, `200`/`404` mapping)
  - _Requirements: 11.1, 11.2_

## Chunk B — Auth

- [x] 2. Domain and persistence
  - `User.BranchId`; `RegisterStaff(..., Guid branchId)` (non-empty guard); `AssignBranch(Guid)` (Staff only, non-empty); update the three test callers
  - EF config and migration `AddStaffBranch` (nullable `BranchId`, index `IX_RefreshTokens_UserId_RevokedAtUtc`)
  - `IUserRepository.GetForUpdateByIdAsync`, `ListByIdsAsync`; `IRefreshTokenRepository.RevokeAllForUserAsync` (`ExecuteUpdateAsync` on `UserId && RevokedAtUtc == null`)
  - Tests: `UserTests` for the factory and `AssignBranch` guards
  - _Requirements: 1.1, 2.1, 10.1, 10.3_

- [x] 3. `branch_id` claim
  - `TokenGenerator` adds `Constants.BranchIdClaimType` for Staff with a branch only
  - Tests: `TokenGeneratorTests` (Staff with branch, Customer, Admin); `RefreshTokenCommandHandlerTests` case showing the token is generated from the re-read user
  - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 4. Availability client and configuration
  - `IAvailabilityClient.GetBranchAsync` + `BranchInfo` (Application); `Clients/AvailabilityClient` and `AddAvailabilityClient` copied from Booking (base URL and key required, `CorrelationIdHandler`, standard resilience, 5 s / 20 s); `Microsoft.Extensions.Http.Resilience` package; call it from `AddInfrastructure`
  - `appsettings.json` (`InternalApi:Key` and `Services:Availability:BaseUrl` empty), `appsettings.Development.json` (dev key, `http://localhost:5202`)
  - Tests: `AvailabilityClientTests` (200, 404 → null, 401, 5xx, timeout, path and `X-API-Key`, DI startup failures); shipped-settings check of the new keys
  - _Requirements: 1.4, 11.3, 11.4_

- [x] 5. Create and move staff
  - `CreateStaffCommand`/`Request`, `AssignStaffBranchCommand`/`Request`, `StaffResponse`; validators (register rules + branch; branch) registered in `AddApplication`; `CreateStaffHandler`, `AssignStaffBranchHandler` per the design (order of checks, `422`/`503`, same-branch no-op, save then `RevokeAllForUserAsync`)
  - `StaffController` (`api/auth/staff`, `AdminPolicy`; `POST` → `201`, `PUT {id:guid}/branch` → `200`)
  - Tests: `CreateStaffHandlerTests`, `AssignStaffBranchHandlerTests`, the two validator test classes, `StaffControllerTests`
  - _Requirements: 1.1–1.6, 2.1–2.6, 10.1, 10.2_

- [x] 6. Internal user lookup
  - `AddApiKeyAuthentication(builder.Configuration)` in Auth `Program.cs` after `AddJwtAuthentication`
  - `LookupUsersQuery(Ids)`, validator (1–100, no empty), `LookupUsersHandler` (de-duplicate, `ListByIdsAsync`), `UserLookupItem`; `InternalUsersController` (`internal/users`, `InternalServicePolicy`, `POST lookup`)
  - Tests: `LookupUsersHandlerTests`, `LookupUsersQueryValidatorTests`, `InternalUsersControllerTests`
  - _Requirements: 9.1, 9.2, 9.3, 9.5_

## Chunk C — Booking statuses, transitions and scoped reads

- [x] 7. Domain and migration
  - `AppointmentStatus.Completed`, `NoShow`; `CompletedAtUtc`, `NoShowAtUtc`; `Appointment.Complete(now)` and `MarkNoShow(now)` (`Booked` only, not before start)
  - EF config, migration `AddAppointmentOutcomes` (two columns, `IX_Appointments_BranchId_StartUtc`)
  - `AppointmentResponse` gains `CompletedAtUtc`, `NoShowAtUtc` (update positional callers and tests)
  - Tests: `AppointmentTests` additions
  - _Requirements: 7.1, 7.2, 8.1, 8.4_

- [x] 8. Conditional transitions and cancel
  - `IAppointmentRepository.TryTransitionAsync` (`ExecuteUpdateAsync` with `Status == Booked`, sets the status, `UpdatedAtUtc` and the matching timestamp)
  - `CancelAppointmentCommandHandler`: no-tracking read, `409` for Completed/NoShow, conditional update with re-read on a lost race, release unchanged
  - Tests: `CancelAppointmentHandlerTests` additions (Completed/NoShow `409`, lost race to a cancel → repeat path, lost race to a mark → `409`, existing cases unchanged)
  - _Requirements: 8.3, 8.5_

- [x] 9. Complete and no-show
  - `AppointmentOutcome`, `MarkAppointmentCommand`, `MarkAppointmentHandler` per the design
  - `AppointmentsController`: `CallerBranchId` helper; `POST {id:guid}/complete` and `/no-show` (`AdminOrStaffPolicy`, `200` with the appointment)
  - Tests: `MarkAppointmentHandlerTests` (every status × target, before start, repeat without update, stale update re-read, other branch and no claim `404`, admin), controller tests (routes, policies, claim parsing)
  - _Requirements: 7.1–7.7_

- [x] 10. Branch-scoped read and `/my` statuses
  - `GetAppointmentQuery` gains `CallerBranchId`; handler rule `owner || Admin || Staff of the branch`
  - `MyAppointmentsParameters.TryParseStatus` accepts `Completed`, `NoShow`; validator message updated
  - Tests: `GetAppointmentHandlerTests` (same branch, other branch, no claim, admin, owner), `MyAppointmentsParametersTests` and validator tests updated
  - _Requirements: 4.1, 4.2, 4.3, 8.2_

## Chunk D — Booking branch day list

- [ ] 11. Day and parameters
  - `BranchDay.Today` and `ToUtcRange` (IANA zones, UTC fallback for null or unknown ids, DST gap handling)
  - `BranchAppointmentsParameters` (`TryParseDate` `yyyy-MM-dd`, four statuses, page and size shared with `/my`)
  - Tests: `BranchDayTests` (Auckland and New York ordinary days, both DST changes giving 23 h and 25 h, UTC fallback, unknown zone), `BranchAppointmentsParametersTests`
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.6_

- [ ] 12. Clients and zone provider
  - `IAvailabilityClient.GetBranchAsync` in Booking (`BranchInfo`; 404 → `Success(null)`)
  - `IAuthClient` + `CustomerInfo`; `Clients/AuthClient` and `AddAuthClient` (`Services:Auth:BaseUrl` required, same handlers)
  - `IBranchTimeZoneProvider` + `CachedBranchTimeZoneProvider` (`IMemoryCache`, 5 minutes, no failure caching); `AddMemoryCache`
  - `appsettings.json` `Services:Auth:BaseUrl` empty; Development `http://localhost:5008`
  - Tests: `AvailabilityClientTests` additions, `AuthClientTests`, `CachedBranchTimeZoneProviderTests`, shipped-settings check
  - _Requirements: 5.4, 6.3, 11.3, 11.4_

- [ ] 13. Branch list query and endpoint
  - `ListBranchAppointmentsQuery` (raw strings, caller role and branch), validator, `ListBranchAppointmentsHandler` per the design; `BranchAppointmentResponse`, `CustomerSummary`, `BranchAppointmentsPage`
  - `IAppointmentRepository.ListForBranchAsync`
  - `AppointmentsController`: `GET branch/{branchId:guid}` (`AdminOrStaffPolicy`)
  - Tests: `ListBranchAppointmentsHandlerTests`, `ListBranchAppointmentsQueryValidatorTests`, controller tests
  - _Requirements: 5.1–5.6, 6.1–6.7_

## Chunk E — Delivery

- [ ] 14. Compose, `.http`, BRD and docs
  - `docker-compose.yml`: Auth `InternalApi__Key`, `Services__Availability__BaseUrl: http://availability:8080`; Booking `Services__Auth__BaseUrl: http://auth:8080`
  - `.http` requests for the new public endpoints (Auth and Booking files)
  - `docs/requirements.md`: FR-AUTH-006 and the endpoint drafts; `CLAUDE.md` service descriptions; `docs/specs/README.md` row
  - _Requirements: 12.1, 12.3, 12.4_

- [ ] 15. Manual verification through the gateway (orchestrator)
  - The plan's end-to-end checks on `docker compose up --build`, results recorded at the top of this file
  - _Requirements: 12.5_
