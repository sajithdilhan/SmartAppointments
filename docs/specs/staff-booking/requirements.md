# Staff booking — Requirements

## Introduction

Admins create staff accounts for a branch and move them between branches; a staff member lists their branch's appointments for a day, with the customers' names, and marks each one `Completed` or `NoShow` once it has started; staff are confined to their own branch on every appointment endpoint. The feature spans three services: **Auth** owns staff accounts, their branch (`branch_id` claim) and a customer lookup; **Booking** owns the branch list and the new transitions; **Availability** serves a branch's existence and time zone to the other two.

It refines `FR-BKG-004`, `FR-BKG-006` and `FR-AUTH-003`, and adds `FR-AUTH-006` "Manage Staff", from section 8 of [`docs/requirements.md`](../../requirements.md), plus the Booking endpoint drafts of section 13. It implements the accepted [RFC-0001](../../rfc/0001-staff-branch-assignment.md) and ADRs [0002](../../adr/0002-staff-branch-jwt-claim.md) to [0007](../../adr/0007-revoke-sessions-on-staff-reassignment.md), and builds on [`booking-appointments`](../booking-appointments/requirements.md) (the appointment shape, cancel), [`booking-my-appointments`](../booking-my-appointments/requirements.md) (paging and validation conventions), [`auth-refresh-tokens`](../auth-refresh-tokens/requirements.md) (token families) and [`shared-web-infrastructure`](../shared-web-infrastructure/requirements.md) (the `{status, detail}` error body, the API-key scheme). Every error below is that shared body as `application/problem+json`. Service foundations (JWT validation, policies, `/healthz`, `TimeProvider`) apply unchanged.

## Requirements

### Requirement 1: Create a staff account (FR-AUTH-006)

**User Story:** As an admin, I want to create a staff account for a branch, so that the branch's staff can sign in and work its appointments.

#### Acceptance Criteria

1. WHEN a caller with the `Admin` role POSTs `/api/auth/staff` with `{ firstName, lastName, email, phoneNumber, password, branchId }`, THEN the system SHALL create an active user with role `Staff` and that `BranchId`, and return `201 Created` with `{ id, firstName, lastName, email, phoneNumber, role, branchId }` where `role` is `"Staff"`.
2. The fields SHALL follow the customer registration rules: `firstName` and `lastName` required, at most 50 characters; `email` required and valid; `phoneNumber` required and matching `^\+?[1-9]\d{1,14}$`; `password` at least 8 characters with an upper-case letter, a lower-case letter, a digit and a special character. `branchId` SHALL be a required, non-empty GUID. IF any rule fails, THEN `400 Bad Request` naming every failed rule.
3. IF the email is already registered (any role), THEN the system SHALL return `400 Bad Request` with detail `Email is already registered.`, as registration does, and create nothing.
4. WHEN the request is valid, THEN the system SHALL ask Availability for the branch (Requirement 11). IF the branch does not exist or is inactive, THEN `422 Unprocessable Entity` and nothing is created. IF Availability cannot be reached or answers with a server error, THEN `503 Service Unavailable` and nothing is created.
5. IF the caller is a `Staff` or `Customer`, THEN `403 Forbidden`; IF there is no valid token, THEN `401 Unauthorized`.
6. The password SHALL be stored only as a hash, as for customers. The new staff member SHALL be able to log in at once with `POST /api/auth/login`.

### Requirement 2: Move a staff member to another branch (FR-AUTH-006)

**User Story:** As an admin, I want to move a staff member to another branch, so that their access follows where they work.

#### Acceptance Criteria

1. WHEN an `Admin` PUTs `/api/auth/staff/{id}/branch` with `{ branchId }` for an existing `Staff` user, THEN the system SHALL set the user's `BranchId` and return `200 OK` with the staff shape of Requirement 1.1.
2. IF `{id}` is not a user, or is a user whose role is not `Staff`, THEN `404 Not Found`. IF `branchId` is missing or empty, THEN `400 Bad Request`.
3. The branch SHALL be checked as in Requirement 1.4 (`422` unknown or inactive, `503` Availability unreachable), and on either error the user SHALL be unchanged.
4. WHEN the branch is changed, THEN the system SHALL revoke every refresh-token family of that user in the same request, so each of their refresh tokens is refused afterwards (`401`, as for a revoked token today). WHEN the new `branchId` equals the current one, THEN the system SHALL return `200` and SHALL NOT revoke anything.
5. Access tokens already issued SHALL NOT be revoked; they keep the old `branch_id` until they expire (an accepted residual risk, ADR-0007). The next login SHALL carry the new branch.
6. Access is as Requirement 1.5.

### Requirement 3: Branch claim in staff tokens (FR-AUTH-003, FR-AUTH-006)

**User Story:** As the platform owner, I want every service to know a staff member's branch from their token, so that branch checks need no extra call.

#### Acceptance Criteria

1. WHEN an access token is issued to a `Staff` user (login or refresh) who has a `BranchId`, THEN it SHALL carry the claim `branch_id` with the branch id as a GUID string.
2. Tokens issued to `Customer` and `Admin` users SHALL NOT carry `branch_id`.
3. WHEN a refresh issues a new access token, THEN `branch_id` SHALL be read from the user's current `BranchId`, not copied from the old token.
4. Every existing claim (`sub`, `email`, `role`) and token lifetime SHALL be unchanged.

### Requirement 4: Staff confined to their branch on appointment reads (FR-AUTH-003)

**User Story:** As a customer, I want only the staff of the branch I booked at to see my appointment, so that my data is not shown to the whole organisation.

#### Acceptance Criteria

1. WHEN a `Staff` caller GETs `/api/appointments/{id}`, THEN the system SHALL return the appointment only if its `BranchId` equals the caller's `branch_id`; otherwise `404 Not Found`, the same response as an unknown id.
2. IF a `Staff` caller's token has no `branch_id`, or it is not a GUID, THEN the system SHALL treat them as belonging to no branch (`404` on every appointment id).
3. `Admin` callers and the owning `Customer` SHALL be unaffected; another customer still gets `404`.

### Requirement 5: List a branch's appointments for a day (FR-BKG-004)

**User Story:** As a staff member, I want to see my branch's appointments for a day with who is coming, so that I can prepare and check people in.

#### Acceptance Criteria

1. WHEN a `Staff` caller whose `branch_id` equals `{branchId}`, or an `Admin`, GETs `/api/appointments/branch/{branchId}`, THEN the system SHALL return `200 OK` with `{ items, page, pageSize, totalCount, customersUnavailable }` for that branch and day.
2. IF a `Staff` caller's `branch_id` differs from `{branchId}`, or is missing or not a GUID, THEN `403 Forbidden`. IF the caller is a `Customer`, THEN `403 Forbidden`; IF there is no valid token, THEN `401 Unauthorized`.
3. Each item SHALL have the appointment shape of `GET /api/appointments/{id}` (Requirement 8.4) plus `customer: { firstName, lastName, phoneNumber }` for the appointment's customer, or `customer: null` when Auth does not know the customer.
4. WHEN Auth cannot be reached or answers with a server error, THEN the system SHALL still return `200 OK` with every `customer` `null` and `customersUnavailable` `true`; otherwise `customersUnavailable` SHALL be `false`. Customer data SHALL be fetched with one lookup per page, and SHALL NOT be stored by Booking.
5. The system SHALL make no check that `{branchId}` exists in Availability beyond Requirement 6; an unknown branch returns an empty page.
6. The request SHALL NOT change any data.

### Requirement 6: The day, filters and paging (FR-BKG-004)

**User Story:** As a staff member, I want "a day" to mean my branch's local day, so that early and late appointments land on the right date.

#### Acceptance Criteria

1. WHERE `date` is given, it SHALL be `yyyy-MM-dd`, and the system SHALL return appointments whose `StartUtc` falls in `[local midnight of date, local midnight of the next date)` in the branch's time zone, converted to UTC. On a daylight-saving change the day SHALL be 23 or 25 hours long accordingly.
2. WHERE `date` is omitted, the system SHALL use the branch's current local date, from `TimeProvider`, read once per request.
3. The branch's time zone SHALL come from Availability (Requirement 11), and SHALL be reused for later requests for a few minutes. WHERE the branch has no time zone (no schedule) or is unknown to Availability, the system SHALL use the UTC calendar day. IF Availability cannot be reached and no time zone for the branch is held, THEN `503 Service Unavailable`.
4. WHERE `status` is given, it SHALL be `Booked`, `Cancelled`, `Completed` or `NoShow` (case-insensitive), and only appointments in that status SHALL be returned and counted. WHERE omitted, every status SHALL be returned.
5. Items SHALL be ordered by `StartUtc` ascending, then `Id` ascending.
6. `page` (default `1`, at least 1) and `pageSize` (default `20`, from 1 to 100) SHALL behave as in `booking-my-appointments` Requirement 3.3 to 3.6, reading only the requested page and the count.
7. IF `date` is not a valid `yyyy-MM-dd` date, `status` is not one of the four values, or `page`/`pageSize` are out of range or not integers, THEN `400 Bad Request` with one detail naming every failed rule. Validation SHALL happen before any call to another service.

### Requirement 7: Mark an appointment Completed or NoShow (FR-BKG-006)

**User Story:** As a staff member, I want to record whether a customer came, so that the branch's day has an accurate outcome.

#### Acceptance Criteria

1. WHEN a `Staff` caller whose `branch_id` equals the appointment's `BranchId`, or an `Admin`, POSTs `/api/appointments/{id}/complete`, THEN the system SHALL change a `Booked` appointment to `Completed`, set `CompletedAtUtc` and `UpdatedAtUtc` to the current time, and return `200 OK` with the appointment (Requirement 8.4). `POST /api/appointments/{id}/no-show` SHALL do the same with `NoShow` and `NoShowAtUtc`.
2. IF the current time (`TimeProvider`) is before the appointment's `StartUtc`, THEN `409 Conflict` and no change.
3. WHEN the appointment already has the requested status, THEN `200 OK` with the appointment unchanged (no timestamp moves).
4. IF the appointment is in any other status (`Cancelled`, or `Completed` when asking for no-show, or `NoShow` when asking for complete), THEN `409 Conflict` and no change.
5. IF the appointment does not exist, or the `Staff` caller's `branch_id` differs from its branch (or is missing), THEN `404 Not Found`. IF the caller is a `Customer`, THEN `403 Forbidden`; IF there is no valid token, THEN `401 Unauthorized`.
6. Marking SHALL NOT call Availability or change the slot's reservation.
7. WHEN two transitions on the same appointment race (complete, no-show or cancel), THEN exactly one SHALL change it; each other SHALL get the response its rules give for the status the winner set (a repeat `200` or a `409`).

### Requirement 8: Statuses in the appointment contract (FR-BKG-006, FR-BKG-003)

**User Story:** As a customer, I want my history to show whether I attended, so that it matches what happened.

#### Acceptance Criteria

1. `AppointmentStatus` SHALL have the values `Booked`, `Cancelled`, `Completed` and `NoShow`, written as those strings in responses.
2. `GET /api/appointments/my` SHALL accept `Completed` and `NoShow` as `status` values (case-insensitive) in addition to `Booked` and `Cancelled`; any other value stays `400`. Its `when` filter is unchanged, so a completed past appointment is `past`.
3. IF a customer tries to cancel a `Completed` or `NoShow` appointment, THEN `409 Conflict`, as for any non-`Booked` appointment.
4. The appointment shape returned by every endpoint SHALL add `completedAtUtc` and `noShowAtUtc` (null unless set) to the existing fields; no existing field changes.
5. The existing cancel SHALL change the status with the same race guarantee as Requirement 7.7; its responses for the cases that already exist SHALL be unchanged.

### Requirement 9: Customer lookup for Booking (FR-BKG-004)

**User Story:** As Booking, I want to look up several customers' display data in one call, so that the branch list can show names without storing them.

#### Acceptance Criteria

1. WHEN a caller authenticated with the internal API key POSTs `/internal/users/lookup` to Auth with `{ ids }`, THEN Auth SHALL return `200 OK` with an array of `{ id, firstName, lastName, phoneNumber }` for the ids that are users, and SHALL omit unknown ids.
2. IF `ids` is missing, empty, holds more than 100 values or a value that is not a GUID, THEN `400 Bad Request`. Duplicate ids SHALL be returned once.
3. IF the request has no valid API key, or carries only a JWT, THEN `401 Unauthorized`. The endpoint SHALL NOT return emails, roles or any other field.
4. The gateway SHALL NOT route `/internal/**` (unchanged).
5. Auth SHALL fail at startup if `InternalApi:Key` is missing or shorter than 32 bytes, as Availability and Booking do.

### Requirement 10: Revoke a user's sessions (FR-AUTH-006, FR-AUTH-005)

**User Story:** As an admin moving a staff member, I want their existing sessions ended, so that they sign in again under the new branch.

#### Acceptance Criteria

1. WHEN Requirement 2.4 revokes a user's sessions, THEN every unrevoked refresh token of every family of that user SHALL be marked revoked at the current time, in one database operation.
2. A refresh with any of those tokens afterwards SHALL get `401 Unauthorized`, and SHALL NOT be treated as reuse of a rotated token (no further effect).
3. Other users' tokens SHALL be unaffected.

### Requirement 11: Branch lookup in Availability (FR-AUTH-006, FR-BKG-004)

**User Story:** As Auth and Booking, I want to read a branch's state and time zone, so that assignments are valid and days are local.

#### Acceptance Criteria

1. WHEN a caller authenticated with the internal API key GETs `/internal/branches/{id}` on Availability, THEN it SHALL return `200 OK` with `{ id, isActive, timeZoneId }`, including inactive branches, with `timeZoneId` null when the branch has no schedule.
2. IF the branch does not exist, THEN `404 Not Found`. IF there is no valid API key, THEN `401 Unauthorized`.
3. Auth and Booking SHALL call it with the API key in `X-API-Key`, forward `X-Correlation-ID`, and use the standard resilience handler; a timeout, connection failure or `5xx` after retries SHALL count as "unreachable".
4. Auth SHALL fail at startup if `Services:Availability:BaseUrl` is missing; Booking SHALL fail at startup if `Services:Auth:BaseUrl` is missing.

### Requirement 12: Delivery (no FR-ID)

**User Story:** As the maintainer, I want the feature runnable and documented like the rest, so that it can be checked end to end.

#### Acceptance Criteria

1. Docker Compose SHALL run the feature: Auth receives `InternalApi__Key` and `Services__Availability__BaseUrl`, Booking `Services__Auth__BaseUrl`, from the same `.env` key as today; `.env.example` and the development settings carry working values.
2. The new public endpoints SHALL appear in the OpenAPI documents (shown through the gateway's Scalar in Development); the gateway configuration and its tests SHALL need no change.
3. `.http` files SHALL include a request for each new public endpoint.
4. `docs/requirements.md` SHALL gain `FR-AUTH-006` "Manage Staff" and the final shapes of the new endpoints; `CLAUDE.md` and `docs/specs/README.md` SHALL describe the feature.
5. The full test suite SHALL pass.

## Discrepancies with the BRD

| BRD | This spec | Decision |
|---|---|---|
| FR-BKG-004 "assigned branch" with no requirement for assigning it. | Adds FR-AUTH-006 "Manage Staff" (Requirements 1, 2). | Without it the branch cannot exist; added to the BRD. |
| Section 13 `GET /api/appointments/branch/{branchId}?date=` returns an unspecified list. | Paged `{ items, page, pageSize, totalCount, customersUnavailable }`, `status` filter, branch-local day, customer names. | Matches `/my` paging (section 10) and the user's decisions. |
| Section 13 lists `complete` and `no-show` without rules. | `Booked` only, at or after start; repeat `200`; others `409`. | FR-BKG-006 "after appointment start time"; repeat rule decided with the plan. |

## Out of scope

- Domain events for the new transitions (`AppointmentCompleted`, `AppointmentNoShow`): with the outbox and messaging feature.
- Staff cancelling appointments on a customer's behalf; undoing a mark; a time limit after which marking closes.
- Forced password change for new staff; staff self-service; more than one branch per staff member; listing or deactivating staff.
- Re-checking an assigned branch later deactivated in Availability.
- Revoking already-issued access tokens.
- The walk-in queue, reports, and frontend staff screens.
