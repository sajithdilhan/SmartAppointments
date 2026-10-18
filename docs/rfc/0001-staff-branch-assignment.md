# RFC-0001: Staff branch assignment and branch appointment operations

**Status:** Implemented
**Author:** Sajith Dilhan (drafted with Claude Code)
**Created:** 2026-10-18
**Related:** [`docs/specs/staff-booking/plan.md`](../specs/staff-booking/plan.md); FR-BKG-004, FR-BKG-006, FR-AUTH-003, FR-AUTH-006 (new)

## Summary

Staff members get a branch, owned by Auth and set by admins. Booking learns it from a `branch_id` claim in the staff access token and confines staff to their branch, lists a branch's appointments for a branch-local day with the customers' names, and lets staff (and admins) mark appointments `Completed` or `NoShow`. Two read-only internal endpoints fill the gaps: Availability serves a branch's existence and time zone, Auth serves customer display data in batches.

## Motivation

FR-BKG-004 and FR-BKG-006 assume an "assigned branch" that does not exist. Auth has a Staff role but no API to create staff and no branch link; Booking has no staff endpoints, only `Booked`/`Cancelled`, no branch time zone (its slot contract is UTC only) and no customer names. Today any staff member can read any appointment by id, which FR-AUTH-003's role restriction does not intend.

## Detailed design

### Auth

`User.BranchId` (uuid, required for Staff, null otherwise). Admin endpoints (`AdminPolicy`):

| Method | Path | Body | Result |
|---|---|---|---|
| `POST` | `/api/auth/staff` | `{firstName, lastName, email, phoneNumber, password, branchId}` | `201` staff profile with `branchId`; `400` validation or "Email is already registered." (as register); `422` unknown or inactive branch; `503` Availability unreachable |
| `PUT` | `/api/auth/staff/{id}/branch` | `{branchId}` | `200` profile; `404` unknown or non-staff user; `422`/`503` as above; revokes every refresh-token family of the user |

`TokenGenerator` adds `branch_id` (`Constants.BranchIdClaimType = "branch_id"`, a GUID string) for Staff only. With `MapInboundClaims = false` it reads back unchanged. Unknown claims are ignored by every service, so the contract change is backward compatible.

New internal endpoint (API key, `InternalServicePolicy`; Auth registers `AddApiKeyAuthentication` like Availability):

`POST /internal/users/lookup` `{ids: [uuid, ...]}` (at most 100) → `200 [{id, firstName, lastName, phoneNumber}]`, unknown ids omitted.

Auth gains an `IAvailabilityClient` (Application abstraction, Infrastructure typed `HttpClient` with `AddStandardResilienceHandler`, `X-API-Key`, `CorrelationIdHandler`), and the configuration `InternalApi:Key` and `Services:Availability:BaseUrl` (empty in `appsettings.json`, filled in `appsettings.Development.json`, environment elsewhere). Startup fails if either is missing, as in Booking.

### Availability

`GET /internal/branches/{id}` (`InternalServicePolicy`) → `200 {id, isActive, timeZoneId}` (inactive branches included, `timeZoneId` null without a schedule), `404` unknown id. Additive internal contract.

### Booking

| Method | Path | Who | Result |
|---|---|---|---|
| `GET` | `/api/appointments/branch/{branchId}?date=yyyy-MM-dd&status=&page=&pageSize=` | Staff of the branch, Admin | `200 {items, page, pageSize, totalCount}` ordered by `StartUtc`; items add `customer: {firstName, lastName, phoneNumber}`; `403` other branch or missing claim; `400` listing every failure; `503` dependency unreachable |
| `POST` | `/api/appointments/{id}/complete` | Staff of the appointment's branch, Admin | `200` appointment; same status again `200` unchanged; `409` before `StartUtc` or from another status; `404` unknown or other branch |
| `POST` | `/api/appointments/{id}/no-show` | as above | as above |
| `GET` | `/api/appointments/{id}` (changed) | owner, Staff of its branch, Admin | others `404` as today |

`date` is the branch's local day: Booking reads `timeZoneId` from Availability (in-memory cache, a few minutes) and queries `[localMidnight, nextLocalMidnight)` in UTC, which is 23 or 25 hours on DST days. A branch with no schedule has no zone; the design decides between UTC and a `422`.

`AppointmentStatus` gains `Completed` and `NoShow` (stored as strings, the existing `varchar(16)` column fits); `Appointment` gains `CompletedAtUtc`, `NoShowAtUtc`. Cancel, complete and no-show are each one conditional `UPDATE ... WHERE Id = @id AND Status = 'Booked'`; zero rows affected re-reads the row to return the repeat `200` or the `409`. Neither new transition touches the slot. `/my` accepts the new statuses.

Booking gains `IAuthClient` (lookup) and `IAvailabilityClient.GetBranchAsync`, with `Services:Auth:BaseUrl` configuration.

## Services and contracts affected

| Service / library / contract | Change |
|---|---|
| Auth | Staff endpoints, `BranchId`, claim, revoke-all, internal lookup, API-key scheme, Availability client, config |
| Booking | Statuses, transitions, conditional updates, branch list, scoped get, two clients, zone cache, config |
| Availability | Internal branch endpoint |
| Access-token contract | Optional `branch_id` claim for Staff |
| Internal contracts | `GET /internal/branches/{id}` (Availability), `POST /internal/users/lookup` (Auth) |
| Appointment response contract | New `status` values, nullable `completedAtUtc`, `noShowAtUtc` |
| BuildingBlocks | `Constants.BranchIdClaimType` |
| Compose, `.env.example` | Auth: `InternalApi__Key`, `Services__Availability__BaseUrl`; Booking: `Services__Auth__BaseUrl`; `depends_on` |
| Gateway | None: catch-all routes cover the new public paths; `/internal/**` is never routed |

## Data and migration

- Auth: nullable `BranchId uuid` on `Users` (staff created by hand before this has none and is refused by the staff endpoints until assigned).
- Booking: nullable `CompletedAtUtc`, `NoShowAtUtc`; index `IX_Appointments_BranchId_StartUtc`. No change to the status column.
- Additive; applied at startup in Compose as today.

## Rollout and compatibility

Deploy Availability, then Auth, then Booking; each step is additive. Old tokens lack `branch_id` and simply cannot use staff endpoints until refreshed. Web clients see the new statuses only after staff begin marking; the frontend adds them in a follow-up. Rollback of Booking is safe only before the first mark (older code cannot read `Completed`/`NoShow`); after that, fix forward.

## Security

- Staff are confined to their branch on every appointment endpoint; customers' names and phones go only to that branch's staff and to admins, and are never stored in Booking.
- Residual risk, accepted: a moved or deactivated staff member keeps the old access token's rights for at most its lifetime (60 minutes). Reassignment revokes all their refresh families, forcing a re-login.
- The two internal endpoints use the existing shared API key and are never routed by the gateway. The lookup returns only display fields, capped at 100 ids.

## Alternatives

- **Booking calls Auth for the branch on each staff request**: always current, but every staff request depends on Auth. Rejected for the claim.
- **Booking-owned staff table**: duplicates user data in the wrong service. Rejected.
- **No branch validation in Auth**: keeps Auth independent, but a mistyped id silently creates a useless staff member. Rejected; the cost is that admins cannot assign during an Availability outage.
- **UTC or client-supplied day**: no Availability call, but wrong local days or logic pushed to every client. Rejected.
- **Ids only in the branch list**: no Booking→Auth call, but unusable for staff. Rejected by the user.
- **No revocation on reassignment**: the next refresh already carries the new branch; revocation was chosen to force a clean re-login.

## Open questions

None; the plan's nine decisions are answered. Design settles the zone-cache lifetime, the no-schedule branch case and the branch list's behaviour when only Auth is down.

## Resulting ADRs

- [ADR-0002](../adr/0002-staff-branch-jwt-claim.md): Carry a staff member's branch as a JWT claim
- [ADR-0003](../adr/0003-auth-validates-branch-via-availability.md): Auth validates staff branch assignments through Availability
- [ADR-0004](../adr/0004-branch-local-day-for-staff-queries.md): Staff day queries use the branch's local day
- [ADR-0005](../adr/0005-booking-reads-customers-from-auth.md): Booking reads customer display data from Auth through a batch lookup
- [ADR-0006](../adr/0006-conditional-appointment-transitions.md): Appointment state transitions use conditional updates
- [ADR-0007](../adr/0007-revoke-sessions-on-staff-reassignment.md): Revoke all of a staff member's sessions when their branch changes
