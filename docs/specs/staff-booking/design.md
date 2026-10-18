# Staff booking — Design

## Overview

Three services change, each in its existing four-project layout, with no new shared library and no gateway change. Auth gains a staff `BranchId`, two admin endpoints, the `branch_id` claim, a user-wide revoke and an internal customer lookup; Availability gains one internal read endpoint; Booking gains two statuses, conditional state changes, a branch-scoped read, a branch day list and two outbound clients. The architectural choices are already settled by the accepted RFC-0001 and ADR-0002 to ADR-0007; this design only places them in the code and adds no new ADR (every remaining choice follows an existing convention: Booking's `AvailabilityClient` for HTTP clients, `/my` for paging and the aggregated `400`, Auth's `RefreshTokenRepository` for `ExecuteUpdateAsync`, and the existing `FixedTimeProvider` tests).

Two points are not obvious:

- **Branch-local day.** `BranchDay` (a pure Booking.Application helper) converts a `DateOnly` and an IANA zone id into a UTC half-open range, so DST days are 23 or 25 hours and tests need no system zone (ADR-0004).
- **Conditional transitions.** Cancel, complete and no-show each become one `ExecuteUpdateAsync` with `Status == Booked` in the `WHERE`. When it changes no row the handler re-reads and answers for whatever status won (ADR-0006).

## Architecture

| Layer | Auth | Availability | Booking |
|---|---|---|---|
| Api | `StaffController` (`api/auth/staff`), `InternalUsersController` (`internal/users`); `AddApiKeyAuthentication` in `Program.cs` | `InternalBranchesController` (`internal/branches`) | 3 new actions on `AppointmentsController`; `branch_id` read in a controller helper |
| Application | `CreateStaffCommand`, `AssignStaffBranchCommand`, `LookupUsersQuery`, validators, handlers; `IAvailabilityClient`; `StaffResponse`, `UserLookupItem` | `GetInternalBranchQuery`, handler, `InternalBranchResponse` | `ListBranchAppointmentsQuery`, `MarkAppointmentCommand`, validators, handlers, `BranchDay`, `BranchAppointmentsParameters`; `IAuthClient`, `IBranchTimeZoneProvider`; extended `IAvailabilityClient`, `IAppointmentRepository`, `GetAppointmentQuery`, `CancelAppointmentCommand` |
| Domain | `User.BranchId`, `RegisterStaff(..., branchId)`, `AssignBranch` | — | `AppointmentStatus.Completed`/`NoShow`, `CompletedAtUtc`, `NoShowAtUtc`, `Complete`, `MarkNoShow` |
| Infrastructure | Migration `AddStaffBranch`; `branch_id` claim; `RevokeAllForUserAsync`; `ListByIdsAsync`, `GetForUpdateByIdAsync`; `AvailabilityClient` + `AddAvailabilityClient` | — | Migration `AddAppointmentOutcomes`; conditional updates; `ListForBranchAsync`; `AuthClient` + `AddAuthClient`; `GetBranchAsync`; `CachedBranchTimeZoneProvider` |
| Shared | `BuildingBlocks/Constants.BranchIdClaimType = "branch_id"` (constant only) | | |

Dependencies keep the direction Api → Application → Domain and Infrastructure → Application. No project references cross services; Auth and Availability, and Booking and Auth, talk only through the internal HTTP endpoints under `InternalServicePolicy`. The gateway is unchanged: `/api/auth/{**catch-all}` and `/api/appointments/{**catch-all}` already route the new paths, only `POST /api/appointments` is rate-limited, the CORS policy is path-independent, and `/internal/**` is never routed (Req 9.4, 12.2).

## Components and interfaces

### Endpoints

| Service | Method and path | Policy | Dispatches | Success |
|---|---|---|---|---|
| Auth | `POST /api/auth/staff` | `AdminPolicy` | `CreateStaffCommand` | `201` `StaffResponse` (`StatusCode(201, ...)`; there is no staff GET to point a `Location` at) (Req 1) |
| Auth | `PUT /api/auth/staff/{id:guid}/branch` | `AdminPolicy` | `AssignStaffBranchCommand` | `200` `StaffResponse` (Req 2) |
| Auth | `POST /internal/users/lookup` | `InternalServicePolicy` | `LookupUsersQuery` | `200` `UserLookupItem[]` (Req 9) |
| Availability | `GET /internal/branches/{id:guid}` | `InternalServicePolicy` | `GetInternalBranchQuery` | `200` `InternalBranchResponse` (Req 11.1, 11.2) |
| Booking | `GET /api/appointments/branch/{branchId:guid}` | `AdminOrStaffPolicy` | `ListBranchAppointmentsQuery` | `200` `BranchAppointmentsPage` (Req 5, 6) |
| Booking | `POST /api/appointments/{id:guid}/complete` | `AdminOrStaffPolicy` | `MarkAppointmentCommand(Outcome.Completed)` | `200` `AppointmentResponse` (Req 7) |
| Booking | `POST /api/appointments/{id:guid}/no-show` | `AdminOrStaffPolicy` | `MarkAppointmentCommand(Outcome.NoShow)` | `200` `AppointmentResponse` (Req 7) |
| Booking | `GET /api/appointments/{id:guid}` (changed) | `AllowedOriginsPolicy` | `GetAppointmentQuery` + `CallerBranchId` | unchanged (Req 4) |

A customer gets `403` from `AdminOrStaffPolicy` on the three new Booking actions (Req 5.2, 7.5). Raw query strings are passed to the branch list query, as `/my` does, so bad values reach the validator (Req 6.7).

The Booking controller gets a helper `Guid? CallerBranchId => Guid.TryParse(User.FindFirst(Constants.BranchIdClaimType)?.Value, out var b) && b != Guid.Empty ? b : null;`. A missing or malformed claim is `null`, which matches no branch (Req 4.2, 5.2, 7.5). Handlers never read `HttpContext`.

### Commands, queries and handlers

**Auth**

- `CreateStaffHandler` (Req 1):
  1. Validate (aggregated `400`, `"Invalid request data. Errors: ..."`, as register).
  2. `GetByEmailAsync`: if found, `400 Email is already registered.`
  3. `availability.GetBranchAsync(branchId)`: `404` or `isActive == false` → `422 "Branch '{id}' does not exist or is inactive."`; failure → `503 "The availability service is unavailable. Try again later."`.
  4. Hash, then `User.RegisterStaff(..., branchId)`, `AddAsync`, `SaveChangesAsync`, catching `DuplicateEmailException` → the same `400`.

  Validation and the email check come before the Availability call, so a bad request never waits on another service.
- `AssignStaffBranchHandler` (Req 2, 10):
  1. Validate (`400` for an empty `branchId`).
  2. `GetForUpdateByIdAsync(id)`: null, or a role other than `Staff` → `404 "Staff member '{id}' was not found."`.
  3. If `user.BranchId == branchId` → `200`, no revoke (Req 2.4).
  4. Check the branch as in create (`422`/`503`, user unchanged, Req 2.3).
  5. `user.AssignBranch(branchId)`, `SaveChangesAsync`.
  6. `refreshTokens.RevokeAllForUserAsync(user.Id, now)`.

  Saving before revoking means a failed save leaves both unchanged. A failure between the two (the database lost mid-request) leaves the new branch saved with sessions still live, which the next refresh corrects anyway; this is accepted rather than wrapped in a cross-repository transaction.
- `LookupUsersHandler` (Req 9): validate (`ids` present, 1 to 100, non-empty GUIDs), de-duplicate, `ListByIdsAsync`, map to `UserLookupItem(Id, FirstName, LastName, PhoneNumber)`. It returns users of every role; Booking only ever asks for its customers' ids.

**Availability**

- `GetInternalBranchHandler` (Req 11): `branches.GetByIdAsync(id)` (already includes inactive branches). Null → `BranchErrors.NotFound(id)` (`404`); otherwise `InternalBranchResponse(Id, IsActive, TimeZoneId)`. Unlike `GetBranchHandler`, it does not hide inactive branches.

**Booking**

- `GetAppointmentHandler` (Req 4): the access rule becomes `owner || Admin || (Staff && CallerBranchId == appointment.BranchId)`, otherwise the existing `404`.
- `CancelAppointmentCommandHandler` (Req 8.3, 8.5):
  1. Unchanged access rule, read with `GetByIdNoTrackingAsync`.
  2. If `Completed` or `NoShow` → `409 "The appointment is {status} and can no longer be cancelled."`.
  3. If `Booked`: past start → `409` (unchanged); otherwise `TryTransitionAsync(id, Cancelled, now)`. If it returns `false`, re-read: `Cancelled` continues as a repeat cancel; another status gives the `409` above.
  4. Release the slot as today. A repeat cancel still re-releases idempotently.

  Existing responses are unchanged (Req 8.5).
- `MarkAppointmentHandler` (Req 7): `MarkAppointmentCommand(Guid Id, AppointmentOutcome Outcome, Guid CallerId, string CallerRole, Guid? CallerBranchId)`, where `AppointmentOutcome { Completed, NoShow }` maps to the status.
  1. `GetByIdNoTrackingAsync`: null, or `Staff` with `CallerBranchId != appointment.BranchId` → `404`.
  2. Status equals the target → `200` with the appointment (Req 7.3).
  3. Status not `Booked` → `409 "The appointment is {status} and cannot be marked {target}."` (Req 7.4).
  4. `now < StartUtc` → `409 "The appointment has not started yet."` (Req 7.2).
  5. `TryTransitionAsync(id, target, now)`. If it returns `false`, re-read and apply steps 2 to 3 to the winner's status (Req 7.7). Otherwise re-read and return `200`.

  No Availability call is made (Req 7.6).
- `ListBranchAppointmentsHandler` (Req 5, 6):
  1. Validate (aggregated `400`) before anything else.
  2. `Staff` with `CallerBranchId != branchId` → `403 "You can only view your own branch's appointments."`.
  3. `zoneProvider.GetAsync(branchId)`: failure → `503`; `null` zone → UTC.
  4. `BranchDay.ToUtcRange(date ?? BranchDay.Today(zone, nowUtc), zone)`.
  5. `ListForBranchAsync(branchId, fromUtc, toUtc, status, page, pageSize)`.
  6. If the page has items: `authClient.LookupAsync(distinct customer ids)`. Failure → every `customer` null and `customersUnavailable = true`; success → missing ids get null.
  7. Return the `BranchAppointmentsPage`.

  `now` is read once.

### Abstractions

```csharp
// Auth.Application/Abstractions
public interface IAvailabilityClient
{
    // Success(null) = 404; Failure(503) = unreachable, timeout, 401/403 from Availability or 5xx after retries.
    Task<Result<BranchInfo?>> GetBranchAsync(Guid branchId, CancellationToken ct);
}
public sealed record BranchInfo(Guid Id, bool IsActive, string? TimeZoneId);

// IUserRepository additions
Task<User?> GetForUpdateByIdAsync(Guid id, CancellationToken ct);
Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

// IRefreshTokenRepository addition: ExecuteUpdate on UserId && RevokedAtUtc == null
Task<int> RevokeAllForUserAsync(Guid userId, DateTime nowUtc, CancellationToken ct);

// Booking.Application/Abstractions
// IAvailabilityClient gains:
Task<Result<BranchInfo?>> GetBranchAsync(Guid branchId, CancellationToken ct);
public interface IAuthClient
{
    // Failure(503) on any outage; unknown ids are simply absent from the dictionary.
    Task<Result<IReadOnlyDictionary<Guid, CustomerInfo>>> LookupAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
public sealed record CustomerInfo(string FirstName, string LastName, string PhoneNumber);
public interface IBranchTimeZoneProvider
{
    // Success(zoneId or null) — null means "no schedule or unknown branch": use UTC. Failure(503) when Availability is down and nothing is cached.
    Task<Result<string?>> GetAsync(Guid branchId, CancellationToken ct);
}
// IAppointmentRepository additions
Task<bool> TryTransitionAsync(Guid id, AppointmentStatus to, DateTime nowUtc, CancellationToken ct);
Task<AppointmentPage> ListForBranchAsync(Guid branchId, DateTime fromUtc, DateTime toUtc,
    AppointmentStatus? status, int page, int pageSize, CancellationToken ct);
```

Clients:

- `Auth.Infrastructure/Clients/AvailabilityClient` and `Booking.Infrastructure/Clients/AuthClient` copy Booking's `AvailabilityClient`: `SendAsync` maps `HttpRequestException`, Polly timeout and broken circuit, and a non-caller cancellation to `Failure(503)`; `401`/`403` are logged as a key problem and returned as `503`.
- Their `Add*Client` extensions follow `AddAvailabilityClient`: base URL and key required at startup, `CorrelationIdHandler` before `AddStandardResilienceHandler`, 5 s per attempt, 20 s total (Req 11.3, 11.4).
- The lookup is a `POST` but a read, so the standard retry is safe; a comment says so.
- Auth.Infrastructure gains the `Microsoft.Extensions.Http.Resilience` package reference Booking already uses.

`CachedBranchTimeZoneProvider` (Booking.Infrastructure) wraps `IAvailabilityClient.GetBranchAsync` with `IMemoryCache` (`AddMemoryCache`). It caches the zone or `null` (unknown branch, or no schedule) for 5 minutes absolute and never caches a failure (Req 6.3).

`BranchDay` (Booking.Application, static, pure):

- `DateOnly Today(string? zoneId, DateTime nowUtc)`.
- `(DateTime FromUtc, DateTime ToUtc) ToUtcRange(DateOnly date, string? zoneId)`.

Both resolve the zone with `TimeZoneInfo.FindSystemTimeZoneById`, which accepts IANA ids on every supported OS since .NET 6. `null`, or an id the runtime does not know, means UTC. Local midnight is converted with `TimeZoneInfo.ConvertTimeToUtc` on an `Unspecified` `DateTime`. A midnight that falls in a DST gap is moved forward to the first valid local time (Req 6.1, 6.2).

### Validation

- `CreateStaffCommandValidator`: the `RegisterCustomerCommandValidator` rules (same messages), plus `BranchId` `NotEqual(Guid.Empty)` "Branch is required." (Req 1.2).
- `AssignStaffBranchCommandValidator`: `BranchId` not empty (Req 2.2).
- `LookupUsersQueryValidator`: `Ids` not null, count from 1 to 100, no `Guid.Empty` (Req 9.2).
- `ListBranchAppointmentsQueryValidator`: one `Must` rule per raw parameter through `BranchAppointmentsParameters`:
  - `TryParseDate`: `DateOnly.TryParseExact(raw, "yyyy-MM-dd", InvariantCulture)`; null means omitted.
  - `TryParseStatus`: the four statuses.
  - The page and page-size parsers are shared with `/my`.

  Messages: "The date must be a valid date in the format yyyy-MM-dd.", "The status must be Booked, Cancelled, Completed or NoShow.", and the `/my` page messages (Req 6.4, 6.7).
- `MyAppointmentsParameters.TryParseStatus` accepts `Completed` and `NoShow`. Its message becomes "The status must be Booked, Cancelled, Completed or NoShow." (Req 8.2).
- All validators are registered by hand in each Application `DependencyInjection.cs`, as today.

### Response models

- `StaffResponse(Guid Id, string FirstName, string LastName, string Email, string PhoneNumber, string Role, Guid BranchId)` (Req 1.1).
- `UserLookupItem(Guid Id, string FirstName, string LastName, string PhoneNumber)` (Req 9.1, 9.3).
- `InternalBranchResponse(Guid Id, bool IsActive, string? TimeZoneId)` (Req 11.1).
- `AppointmentResponse` gains `DateTime? CompletedAtUtc, DateTime? NoShowAtUtc` as its last two positional parameters (Req 8.4).
  - Stored idempotency replays written before the change deserialize with the two as `null`, which is correct.
- `BranchAppointmentResponse`: every `AppointmentResponse` field, flattened, plus `CustomerSummary? Customer` (`FirstName, LastName, PhoneNumber`). It is built with `From(Appointment, CustomerInfo?)`.
- `BranchAppointmentsPage(IReadOnlyList<BranchAppointmentResponse> Items, int Page, int PageSize, int TotalCount, bool CustomersUnavailable)` (Req 5.1, 5.3, 5.4).

## Data model

**Auth** (migration `AddStaffBranch`):
- Add a nullable `BranchId uuid` to `Users`.
- Add an index `IX_RefreshTokens_UserId_RevokedAtUtc`, so the user-wide revoke does not scan.

`User.RegisterStaff` requires a non-empty `branchId` and `AssignBranch` throws for non-staff users. Both are domain guards; handlers check first, so the guards are never reached through the API (Req 1.1, 2.1).

**Booking** (migration `AddAppointmentOutcomes`):
- Add nullable `CompletedAtUtc` and `NoShowAtUtc`.
- Add the index `IX_Appointments_BranchId_StartUtc`.
- The `Status` column needs no change: it is a `varchar(16)` string.

The migrations are additive and apply at startup where `Database:MigrateOnStartup` is set (Req 5, 6, 8.1, 12.1).

`TryTransitionAsync` is

```csharp
context.Appointments.Where(a => a.Id == id && a.Status == AppointmentStatus.Booked)
    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, to)
        .SetProperty(a => a.UpdatedAtUtc, nowUtc)
        .SetProperty(a => a.CancelledAtUtc, a => to == Cancelled ? nowUtc : a.CancelledAtUtc)
        /* same for CompletedAtUtc, NoShowAtUtc */, ct) == 1
```

The domain methods `Appointment.Complete(now)` and `MarkNoShow(now)` hold the same rules (`Booked` only, not before start) for unit tests and stay the single description of the rules. Handlers check them on the read copy before the conditional update (ADR-0006).

`ListForBranchAsync` copies `ListForCustomerAsync`: `AsNoTracking`, `BranchId == branchId && StartUtc >= from && StartUtc < to`, optional status, count, the skip guard, `OrderBy(StartUtc).ThenBy(Id)`, `Skip`/`Take`.

## Configuration

| Service | Key | `appsettings.json` | `appsettings.Development.json` | Compose |
|---|---|---|---|---|
| Auth | `InternalApi:Key` | `""` | the shared dev key | `InternalApi__Key: ${INTERNAL_API_KEY:?...}` |
| Auth | `Services:Availability:BaseUrl` | `""` | `http://localhost:5202` | `http://availability:8080` |
| Booking | `Services:Auth:BaseUrl` | `""` | `http://localhost:5008` | `http://auth:8080` |

Auth fails at startup on a missing or short key (through `AddApiKeyAuthentication`) or a missing URL (through `AddAvailabilityClient`). Booking fails on a missing Auth URL (Req 9.5, 11.4, 12.1). `.env.example` needs no new key: `INTERNAL_API_KEY` already exists.

## Error handling

Every failure is a `Result` `Error` turned into `{status, detail}` by `this.ToActionResult`, which already maps `400`, `401`, `403`, `404`, `409`, `422` and `503`. Nothing new is thrown.

| Condition | Status | Detail |
|---|---|---|
| Validation (any endpoint) | 400 | `Invalid request data. Errors: ...` (every failure) |
| Duplicate staff email | 400 | `Email is already registered.` |
| Unknown or inactive branch on assign | 422 | `Branch '{id}' does not exist or is inactive.` |
| Availability unreachable (assign, branch list without cached zone) | 503 | `The availability service is unavailable. Try again later.` |
| Not a staff user (assign) | 404 | `Staff member '{id}' was not found.` |
| Staff on another branch's list | 403 | `You can only view your own branch's appointments.` |
| Appointment unknown or outside the staff member's branch | 404 | `Appointment '{id}' was not found.` |
| Mark before start | 409 | `The appointment has not started yet.` |
| Mark from another status | 409 | `The appointment is {status} and cannot be marked {target}.` |
| Cancel a Completed or NoShow appointment | 409 | `The appointment is {status} and can no longer be cancelled.` |
| Auth unreachable on the branch list | 200 | `customersUnavailable: true` |

## Testing strategy

xUnit + Moq, flat `*Tests.cs` files, the `FixedTimeProvider` of each project, handlers built directly with mocked abstractions, controllers with a `ClaimsPrincipal`, and policies checked by reflection on `AuthorizeAttribute`.

| Req | Tests |
|---|---|
| 1, 2, 10 | `CreateStaffHandlerTests` (success, every validation rule, duplicate email before and at commit, `422` unknown and inactive, `503`, validation and email check before the Availability call), `AssignStaffBranchHandlerTests` (`404` unknown or non-staff, same branch → no revoke, `422`/`503` leave the user unchanged, revoke called once with the user id after the save), `UserTests` (`RegisterStaff`, `AssignBranch` guards), `StaffControllerTests` (status codes, `AdminPolicy`) |
| 3 | `TokenGeneratorTests`: Staff with a branch → `branch_id`; Customer and Admin → none; `RefreshTokenCommandHandlerTests`: the token is built from the re-read user |
| 9, 11 (Auth side) | `LookupUsersHandlerTests`, `LookupUsersQueryValidatorTests`, `InternalUsersControllerTests` (`InternalServicePolicy`), `AvailabilityClientTests` (Auth copy: 200, 404, 401, 5xx, timeout, request path and `X-API-Key`, DI startup failures) |
| 11 (Availability) | `GetInternalBranchHandlerTests` (inactive included, null zone, `404`), `InternalBranchesControllerTests` |
| 4 | `GetAppointmentHandlerTests`: staff same branch `200`, other branch `404`, no claim `404`, admin and owner unchanged |
| 5, 6 | `ListBranchAppointmentsHandlerTests` (`403` other branch and no claim, admin any branch, default date, UTC fallback, `503` zone failure, names merged, unknown customer null, Auth failure → `customersUnavailable`, no lookup on an empty page, validation before any call), `ListBranchAppointmentsQueryValidatorTests`, `BranchAppointmentsParametersTests`, `BranchDayTests` (Pacific/Auckland and America/New_York ordinary days, both DST transitions with 23 and 25 hour ranges, UTC fallback, unknown zone id), `CachedBranchTimeZoneProviderTests`, `AuthClientTests` |
| 7, 8 | `AppointmentTests` (`Complete`, `MarkNoShow` rules), `MarkAppointmentHandlerTests` (each status × target, before start, repeat `200` without update, stale update re-read giving `200` or `409`, other branch `404`, admin), `CancelAppointmentHandlerTests` additions (`409` for Completed/NoShow, lost race), `MyAppointmentsParametersTests` and validator (new statuses), `AppointmentsControllerTests` (new actions, policies, `CallerBranchId` parsing) |
| 12 | `ShippedSettingsTests`/`DatabaseMigratorTests` style checks that the new keys ship empty in `appsettings.json` and filled in Development; manual Compose check |

The repository `ExecuteUpdateAsync` and query code is covered by the manual end-to-end check, as the existing repositories are (there is no database test harness yet).

## Requirements traceability

Every criterion in requirements 1 to 12 maps to a decision above:

| Requirement | Decisions |
|---|---|
| 1 | `CreateStaffHandler`, its validator, `StaffResponse` |
| 2 | `AssignStaffBranchHandler` |
| 3 | `TokenGenerator` |
| 4 | `GetAppointmentHandler` |
| 5, 6 | `ListBranchAppointmentsHandler`, `BranchDay`, the zone provider, `IAuthClient` |
| 7 | `MarkAppointmentHandler`, `TryTransitionAsync` |
| 8 | Statuses, response fields, cancel changes, `/my` parameters |
| 9 | `LookupUsersHandler`, API key in Auth |
| 10 | `RevokeAllForUserAsync` |
| 11 | `GetInternalBranchHandler` and the clients |
| 12 | Configuration, Compose, docs, `.http` |

## Open questions

None. The new public endpoints are visible in OpenAPI through the existing `AddOpenApiWithBearerAuth`. Internal endpoints also appear in each service's own document, as `/internal/slots` does today.

## Implementation deviations

Where the code differs from, or fills a gap in, the design above:

- `CancelAppointmentHandler`: when the conditional update loses a race, the re-read returns `404` if the appointment has vanished in the meantime.
- `MarkAppointmentHandler` re-reads the appointment after every `TryTransitionAsync` and re-applies the branch check to the re-read copy.
- EF Core dropped `IX_RefreshTokens_UserId` in favour of the composite `(UserId, RevokedAtUtc)` index, which serves the same lookups; `Down` recreates the old index.
- The lookup body is `LookupUsersRequest(List<Guid>? Ids)`, mapped to `LookupUsersQuery`. A non-GUID value in the list is a model-binding `400`, not a validator failure.
- `BranchInfo` lives in `Booking.Application/Models`.
- `CachedBranchTimeZoneProvider` is registered as scoped.
- `Auth.Infrastructure` references `BuildingBlocks.Web` for `CorrelationIdHandler`, as `Booking.Infrastructure` already does.
- `BranchDay` moves a local midnight that falls in a DST gap forward in 30-minute steps until it exists.
