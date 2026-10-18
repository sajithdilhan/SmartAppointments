# Staff booking — Plan

**Status:** Accepted (2026-10-18)

## Problem and goal

Staff accounts exist only as a role: nothing creates them through the API, nothing ties a staff member to a branch, and Booking has no staff endpoints. A branch's staff cannot see the day's appointments or record what happened to them, an appointment can only be `Booked` or `Cancelled`, and any staff member can read any appointment by id. Once this feature exists, an Admin creates staff for a branch (and moves them), a staff member sees their branch's appointments for a day with the customers' names, and marks each one `Completed` or `NoShow` once it has started; staff are confined to their own branch everywhere.

## Scope

- **Auth**
  - `User.BranchId` (required for Staff, null for every other role); extend the existing `User.RegisterStaff` with `branchId` and add `AssignBranch(branchId)`; update its existing callers.
  - Admin-only `POST /api/auth/staff` (create a staff account with an initial password and a branch) and `PUT /api/auth/staff/{id}/branch` (move them).
  - The branch is validated through Availability's new internal endpoint: unknown or inactive → `422`, Availability unreachable → `503`.
  - A `branch_id` claim (GUID string) in a staff member's access token, absent for other roles.
  - On reassignment, revoke every refresh-token family of that user (new `RevokeAllForUserAsync`), forcing a re-login.
  - New internal batch lookup of customer display data for Booking (`POST /internal/users/lookup`, API key), which also brings the API-key scheme into Auth.
- **Availability**: internal `GET /internal/branches/{id}` → `{id, isActive, timeZoneId}` (`404` for an unknown id), API key.
- **Booking**
  - `GET /api/appointments/branch/{branchId}?date=yyyy-MM-dd&status=&page=&pageSize=`: the branch-local day (midnight to midnight in the branch zone, DST-safe), paged like `/my` with one `400` listing every failure; each item carries the customer's name and phone from Auth.
  - `POST /api/appointments/{id}/complete` and `/no-show`: from `Booked` at or after `StartUtc`; repeating the same mark returns `200` unchanged; any other transition `409`; done with a conditional update (`WHERE Status = 'Booked'`) so a racing cancel or mark loses cleanly with `409`.
  - The existing cancel becomes a conditional update too (same race).
  - `GET /api/appointments/{id}`: Staff only for their branch; another branch's appointment, or a missing claim, gets the same `404` as an unknown id.
  - `Completed`, `NoShow`, `CompletedAtUtc`, `NoShowAtUtc`; index `(BranchId, StartUtc)`; `/my`'s `status` filter accepts the new values.
  - Admins may list any branch and mark any appointment; staff only their own branch (`403` on the list for another branch).
- **BRD**: add FR-AUTH-006 "Manage Staff" to `docs/requirements.md` (FR table and the Auth endpoint draft) and the new Booking endpoints' final shapes.
- Tests in `Auth.Tests`, `Availability.Tests`, `Booking.Tests`; `.http` requests; Compose and `.env.example`; docs.

## Out of scope

- Domain events for completed/no-show: they come with the outbox and messaging feature.
- Staff cancelling or serving appointments on a customer's behalf (the BRD's staff "cancel"/"serve" belong to the queue feature); undoing a mark.
- Forced password change for new staff, staff self-service, several branches per staff member.
- Re-checking a branch later deactivated in Availability (the assignment stays until an admin changes it).
- The walk-in queue, reports, and the frontend staff screens.

## FR-IDs

| FR-ID | Summary |
|---|---|
| FR-BKG-004 | Staff view appointments for their assigned branch and a date |
| FR-BKG-006 | Staff mark appointments Completed or NoShow after the start time |
| FR-AUTH-003 | Role-based access (staff confined to their branch, including `GET /{id}`) |
| FR-AUTH-006 (new) | Manage Staff: admins create staff accounts and assign their branch. Added to the BRD by this feature |

## Approach

Auth owns who a staff member is and where they work. Booking authorises staff by the signed `branch_id` claim, so no call to Auth is needed to decide access; the 60-minute access-token lifetime bounds how long a moved staff member keeps the old branch (accepted residual risk; revocation forces a re-login but cannot shorten an issued access token).

Two read-only internal endpoints carry what each service lacks: Availability tells Auth that a branch exists and tells Booking its time zone (Booking caches the zone in memory for a few minutes); Auth tells Booking the names and phones of a page of customers in one batch call. Both clients follow Booking's existing `AvailabilityClient` pattern (typed `HttpClient`, `AddStandardResilienceHandler`, `X-API-Key`, `CorrelationIdHandler`), and an unreachable dependency surfaces as `503`. Auth's own client to Availability is new for Auth, so Auth joins the services that require `InternalApi:Key` at startup.

State changes on an appointment (cancel, complete, no-show) become single conditional `UPDATE`s, like the reservation and token-rotation code already does, so concurrent requests cannot both win.

## Affected services and projects

| Service / project | Change |
|---|---|
| Availability.Api / Application | Internal branch endpoint and query |
| Auth.Domain | `BranchId`, `RegisterStaff(..., branchId)`, `AssignBranch` |
| Auth.Application | Create-staff and assign-branch commands, user lookup query, `IAvailabilityClient`, validators, handlers |
| Auth.Infrastructure | Migration, `branch_id` claim, `RevokeAllForUserAsync`, Availability client |
| Auth.Api | `StaffController`, internal users controller, API-key auth registration, config keys in appsettings files |
| Booking.Domain | Two statuses, timestamps, transitions |
| Booking.Application | Branch list query, complete and no-show commands, `IAuthClient`, extended `IAvailabilityClient`, branch-scoped get |
| Booking.Infrastructure | Migration (columns, index), conditional updates, repository query, Auth client, zone cache |
| Booking.Api | Three actions, config keys (`Services:Auth:BaseUrl`) |
| BuildingBlocks | `Constants.BranchIdClaimType` |
| `docker-compose.yml`, `.env.example` | Auth gets `InternalApi__Key` and `Services__Availability__BaseUrl`; Booking gets `Services__Auth__BaseUrl`; dependencies |
| `docs/requirements.md`, `CLAUDE.md`, `docs/specs/README.md`, `.http` files | FR-AUTH-006, endpoint drafts, service descriptions, requests |
| ApiGateway | None: `/api/auth/{**catch-all}` and `/api/appointments/{**catch-all}` already route the new paths (only `POST /api/appointments` is rate-limited) and `/internal/**` stays unrouted |

## Risks

- **Old branch for up to 60 minutes** after a move (existing access token). Accepted; revocation forces re-login.
- **Deactivated staff** also keep their access token until it expires (existing behaviour: inactive users are refused at login and refresh only). Accepted, same window.
- **New synchronous dependencies**: Auth→Availability (assignment only) and Booking→Availability, Booking→Auth (branch list only). Mitigated with the resilience handler, the zone cache and `503`s; the customer booking path is unaffected.
- **Customer personal data in Booking responses**: names and phones go to staff of the appointment's branch and admins only, never stored in Booking.
- **Concurrency**: covered by conditional updates; tested with a stale-status case per transition.
- **Web client**: older builds see `Completed`/`NoShow` in `/my` only after staff start marking; the frontend adds the statuses in a follow-up.
- **Rollback**: once rows are `Completed`/`NoShow`, old Booking code cannot read them; rollback is safe only before the first mark, otherwise fix forward.

## Open decisions

All answered on 2026-10-18:

1. Branch to Booking: **`branch_id` JWT claim**.
2. Staff accounts: **admin creates them** (`POST /api/auth/staff`, `PUT /api/auth/staff/{id}/branch`).
3. Branch validation: **yes, through Availability's internal endpoint** (`422` unknown/inactive, `503` outage).
4. `date`: **branch-local calendar day**, zone from Availability, cached.
5. Admins: **view and mark any branch**.
6. Repeat mark: **`200`, no change**; other transitions `409`.
7. `GET /api/appointments/{id}`: **restricted to the staff member's branch** (`404` otherwise).
8. Reassignment: **revoke all of the user's refresh-token families**.
9. Branch list: **includes customer names and phone**, from a new Auth internal batch lookup.

Left to design (proposed there, approved with the design): the outage behaviour of the branch list when only Auth is down (`503` vs names omitted), the zone-cache lifetime, the exact lookup payload.

## ADRs expected

Written as Accepted with RFC-0001 (ADR-0002 to ADR-0007):
- Staff branch carried as a `branch_id` JWT claim.
- Auth validates staff branch assignment through Availability's internal API.
- Branch-local day boundary for staff queries, zone read from Availability and cached.
- Booking reads customer display data from Auth through an internal batch lookup.
- Appointment state transitions use conditional updates.
- Revoke all of a staff member's sessions on branch reassignment.

## RFC

**Required:** Yes. It touches three services (Auth, Booking, Availability), adds two internal endpoints and two service-to-service clients, and changes the access-token contract. Draft: [`docs/rfc/0001-staff-branch-assignment.md`](../../rfc/0001-staff-branch-assignment.md).

## Work split

| Chunk | Owns (projects / folders) | Depends on |
|---|---|---|
| 0. Shared constant | `BuildingBlocks/Constants.cs` (`BranchIdClaimType`) | — (orchestrator) |
| 1. Availability internal branch endpoint | `Availability.*`, `Availability.Tests` | — |
| 2. Auth staff, claim, revocation, user lookup | `Auth.*`, `Auth.Tests` | 0; 1 (contract only) |
| 3. Booking domain, migration, transitions and scoped get | `Booking.Domain`, Booking migration (both columns and the index), cancel/complete/no-show commands, `GET /{id}` scoping, controller actions for these, tests | 0 |
| 4. Booking branch list and clients | branch query, validators, repository query, Availability and Auth client additions, zone cache, list action, tests | 1, 2 (contracts), 3 (migration and controller file) |
| 5. Compose, BRD, docs, `.http` | `docker-compose.yml`, `.env.example`, `docs/requirements.md`, `CLAUDE.md`, `docs/specs/README.md`, `*.http` | 1–4 |

Chunks 1, 2 and 3 run in parallel after chunk 0; chunk 4 follows chunk 3 (it edits `AppointmentsController` after chunk 3).

## Verification plan

**Automated tests** (xUnit + Moq, existing style):
- Domain: `User.AssignBranch` and `RegisterStaff` invariants; `Appointment` transitions from each status, before and after start, repeat marks.
- Auth: create-staff and assign-branch handlers (`422` unknown/inactive branch, `503` Availability down, duplicate email `400`, non-staff target `404`), `RevokeAllForUserAsync` called on reassignment, `TokenGenerator` emits `branch_id` for Staff only, lookup handler, controller policies.
- Availability: internal branch query and controller (`404`, inactive).
- Booking: branch list handler (wrong branch `403`, admin any branch, day boundaries in a far-from-UTC zone and on DST-change days, paging and the aggregated `400`, names merged from the Auth client, `503` paths); complete/no-show handlers (`409` before start, stale status from the conditional update, repeat `200`); scoped `GET /{id}`; `/my?status=Completed|NoShow`; controller tests with a `ClaimsPrincipal` carrying `branch_id`.
- No Gateway.Tests change: the existing catch-all routes cover the paths.

**Manual end-to-end checks** (Docker Compose through the gateway, `.http` files):
- Admin creates a staff member for a branch (`201`); an unknown branch (`422`).
- The staff member's token carries `branch_id`.
- A customer books two slots at that branch; staff lists the day (`200`, both with the customer's name).
- Staff of another branch gets `403` on the list and `404` on `GET /{id}`.
- Complete one before its start (`409`) and after it (`200`, repeat `200`); mark the other no-show; the customer's `/my?status=Completed` shows it.
- Reassign the staff member: their refresh token is refused, and a new login carries the new branch.
- Stop Availability: assigning a branch gives `503`.
