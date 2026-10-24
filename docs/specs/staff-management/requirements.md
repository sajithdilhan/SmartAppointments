# Staff management — Requirements

## Introduction

Admins list the staff accounts, read one, deactivate and reactivate them, and delete them. The feature completes the staff account lifecycle that `staff-booking` began with create and move. It is owned entirely by the **Auth** service and lives on the existing admin-only `StaffController` (`/api/auth/staff`). Reactivation is the only operation that calls another service, the Availability branch check that create and move already use.

The spec refines `FR-AUTH-006` "Manage Staff" and `FR-AUTH-003` "Role-Based Access" from section 8 of [`docs/requirements.md`](../../requirements.md), and the Auth endpoint drafts of section 13. It implements the accepted [plan](plan.md). It builds on:
- [`staff-booking`](../staff-booking/requirements.md): the staff shape (Requirement 1.1), the branch check (Requirement 1.4) and session revocation on a move (Requirement 2.4, ADR-0007).
- [`auth-refresh-tokens`](../auth-refresh-tokens/requirements.md): token families.
- [`booking-my-appointments`](../booking-my-appointments/requirements.md): the paging and validation conventions.
- [`shared-web-infrastructure`](../shared-web-infrastructure/requirements.md): the error body.

Every error below is the shared `{"status":<int>,"detail":"..."}` body as `application/problem+json`. The Auth service foundations (JWT validation, policies, `/healthz`, `TimeProvider`) apply unchanged.

"A staff account" below means a user with role `Staff`. "Deleted" means soft-deleted by Requirement 6.

## Requirements

### Requirement 1: The staff shape (FR-AUTH-006)

**User Story:** As an admin, I want every staff endpoint to describe an account the same way, including whether it is active and when it was created, so that one table can show any of them.

#### Acceptance Criteria

1. WHEN any staff endpoint returns a staff account (create, move, read one, and each list item), THEN the body SHALL be `{ id, firstName, lastName, email, phoneNumber, role, branchId, isActive, registeredAtUtc }`, where `role` is `"Staff"`, `isActive` is the account's active flag, and `registeredAtUtc` is the account's registration instant in UTC (ISO 8601).
2. The fields `id` to `branchId` SHALL keep their current meaning and format from `staff-booking` Requirement 1.1. The two new fields SHALL be added to the create (`POST /api/auth/staff`) and move (`PUT /api/auth/staff/{id}/branch`) responses as well; nothing is removed or renamed.
3. The body SHALL NOT include the password hash, the deletion time or any token data.

### Requirement 2: List staff accounts (FR-AUTH-006)

**User Story:** As an admin, I want to list the staff accounts, optionally for one branch or by active state, so that I can see who works where and pick an account to change.

#### Acceptance Criteria

1. WHEN an `Admin` GETs `/api/auth/staff`, THEN the system SHALL return `200 OK` with `{ items, page, pageSize, totalCount }`.
   - `items` holds the staff accounts on the requested page, each in the shape of Requirement 1.
   - `page` and `pageSize` echo the values applied, including defaults.
   - `totalCount` is the number of matching accounts across all pages.
2. The list SHALL contain only users with role `Staff` that are not deleted. Customers, admins and deleted staff accounts SHALL never appear, and `totalCount` SHALL not count them.
3. WHERE the `isActive` query parameter is absent, the list SHALL include both active and inactive accounts.
4. WHERE `branchId` is given as a GUID, the system SHALL return only accounts whose `branchId` equals it. The system SHALL NOT check the branch with Availability: an unknown or inactive branch gives `200` with the accounts assigned to it, possibly none.
5. WHERE `isActive` is `true` or `false` (case-insensitive), the system SHALL return only accounts with that active flag.
6. WHERE both filters are given, the system SHALL apply both.
7. WHEN no account matches, THEN the system SHALL return `200 OK` with `items` empty and `totalCount` `0`, never `404`.
8. The system SHALL order items by registration instant ascending (oldest first), then by `id` ascending. The same request on unchanged data SHALL return the same order, so paging neither repeats nor skips an account.
9. `page` SHALL be a whole number of at least 1, defaulting to `1`. `pageSize` SHALL be a whole number from 1 to 100 inclusive, defaulting to `20`. Page `p` of size `n` SHALL hold the matching accounts at positions `(p-1)*n` to `p*n - 1` of that order. A page beyond the last SHALL return `200` with `items` empty and the correct `totalCount`.
10. IF any query value is invalid, THEN the system SHALL return `400 Bad Request` whose `detail` names every invalid parameter, in one response, without clamping any value. The invalid values are:
    - `branchId` present but not a non-empty GUID.
    - `isActive` present but not `true` or `false`.
    - `page` or `pageSize` not a whole number in its range. Digits only: `+1`, `1.0` and `-1` are invalid.

    This SHALL hold for values that could not be parsed at all (for example `isActive=x&branchId=xyz&page=0` gives one `400` naming all three), not only for values out of range.
11. The system SHALL read only the requested page and the count from the database, never every staff account to page them in memory, and SHALL change no data.

### Requirement 3: Read one staff account (FR-AUTH-006)

**User Story:** As an admin, I want to open one staff account by id, so that a client can show or refresh a single account and follow the link returned by create.

#### Acceptance Criteria

1. WHEN an `Admin` GETs `/api/auth/staff/{id}` for a staff account that is not deleted, THEN the system SHALL return `200 OK` with the shape of Requirement 1. Inactive accounts SHALL be returned too.
2. IF `{id}` is not a user, is a user whose role is not `Staff`, or is a deleted staff account, THEN the system SHALL return `404 Not Found`. All three cases SHALL get the same response.
3. IF `{id}` is not a GUID, THEN the system SHALL not match the route, and the result SHALL be `404 Not Found`.
4. WHEN `POST /api/auth/staff` creates an account, THEN the system SHALL return `201 Created` with a `Location` header holding the URL of that account's `GET /api/auth/staff/{id}`. The body SHALL be unchanged apart from Requirement 1. Through the gateway the `Location` host SHALL be the gateway's, as for the other services' `Location` headers.

### Requirement 4: Deactivate a staff account (FR-AUTH-006)

**User Story:** As an admin, I want to deactivate a staff account, so that someone who leaves or is suspended can no longer sign in, without losing the account.

#### Acceptance Criteria

1. WHEN an `Admin` POSTs `/api/auth/staff/{id}/deactivate` for an active staff account that is not deleted, THEN the system SHALL do the following, in one transaction, so that either both happen or neither does:
   - Set the account inactive.
   - Revoke every refresh token of that user that is not already revoked (all their token families).

   It SHALL then return `204 No Content` with no body.
2. WHEN the account is already inactive, THEN the system SHALL return `204 No Content` and leave the flag unchanged. It SHALL still revoke any of the user's refresh tokens that are not revoked, so a session created by a login that raced the first deactivation is ended too.
3. WHEN the account has been deactivated, THEN the following SHALL hold:
   - `POST /api/auth/login` with its correct credentials SHALL return `401 Unauthorized`, the same response as an unknown email or a wrong password.
   - `POST /api/auth/refresh` with any of its refresh tokens SHALL return `401 Unauthorized`.
4. Access tokens already issued to the account SHALL NOT be revoked and remain valid until they expire. This is the residual risk ADR-0007 accepts for a move.
5. IF `{id}` is not a user, is not a staff account, or is a deleted staff account, THEN the system SHALL return `404 Not Found` and change nothing.
6. Deactivation SHALL NOT call Availability, and SHALL succeed whether or not the account's branch is still active.

### Requirement 5: Reactivate a staff account (FR-AUTH-006)

**User Story:** As an admin, I want to reactivate a staff account, so that a returning staff member can sign in again at an active branch.

#### Acceptance Criteria

1. WHEN an `Admin` POSTs `/api/auth/staff/{id}/activate` for an inactive staff account that is not deleted, THEN the system SHALL check the account's current branch with Availability, as `staff-booking` Requirement 1.4 does for create.
   - IF the branch does not exist or is inactive, THEN the system SHALL return `422 Unprocessable Entity`.
   - IF Availability cannot be reached or answers with a server error, THEN the system SHALL return `503 Service Unavailable`.

   In both cases the account SHALL stay inactive.
2. WHEN the branch check passes, THEN the system SHALL set the account active and return `204 No Content`. The staff member SHALL be able to log in again at once.
3. Reactivation SHALL restore no session. Refresh tokens revoked by deactivation SHALL stay revoked, so the staff member signs in again to get new tokens.
4. WHEN the account is already active, THEN the system SHALL return `204 No Content`, change nothing and not call Availability.
5. IF `{id}` is not a user, is not a staff account, or is a deleted staff account, THEN the system SHALL return `404 Not Found` and change nothing.

### Requirement 6: Delete a staff account (FR-AUTH-006)

**User Story:** As an admin, I want to delete a staff account, so that it disappears from the staff list and can never be used again, while its record stays for audit.

#### Acceptance Criteria

1. WHEN an `Admin` sends `DELETE /api/auth/staff/{id}` for a staff account that is not deleted, active or inactive, THEN the system SHALL do the following, in one transaction:
   - Record the deletion instant on the account.
   - Set the account inactive.
   - Revoke every refresh token of that user that is not already revoked.

   It SHALL then return `204 No Content` with no body. Deactivating first SHALL NOT be required.
2. The delete SHALL be soft: the user row and its data SHALL remain in the database.
3. WHEN an account has been deleted, THEN the following SHALL hold:
   - Login with its credentials and refresh with any of its refresh tokens SHALL return `401 Unauthorized`, as in Requirement 4.3.
   - The account SHALL be absent from the list (Requirement 2.2).
   - The account SHALL be unknown to every staff endpoint: read one, move, activate, deactivate and delete all return `404 Not Found` for its id. A second `DELETE` is therefore `404`.
   - `GET /api/auth/profile?email=` with its email SHALL return `404 Not Found`, the same as an unknown email.
4. WHEN an account has been deleted, THEN its email SHALL stay registered. Both of the following SHALL return `400 Bad Request` with detail `Email is already registered.` and create nothing:
   - `POST /api/auth/register` with that email.
   - `POST /api/auth/staff` with that email.
5. WHEN an account has been deleted, THEN Auth's internal user lookup for Booking (`POST /internal/users/lookup`) SHALL still return its display data. That keeps names resolvable for records that refer to it.
6. Access tokens already issued SHALL remain valid until they expire, as in Requirement 4.4. This includes `GET /api/auth/me` on such a token.
7. IF `{id}` is not a user or is not a staff account, THEN the system SHALL return `404 Not Found` and change nothing.
8. There SHALL be no endpoint that restores a deleted account.

### Requirement 7: Moving inactive and deleted accounts (FR-AUTH-006)

**User Story:** As an admin, I want to move an inactive staff member to an open branch before reactivating them, so that someone whose branch has closed can come back.

#### Acceptance Criteria

1. WHEN an `Admin` moves an inactive staff account that is not deleted (`PUT /api/auth/staff/{id}/branch`), THEN the system SHALL apply `staff-booking` Requirement 2 unchanged: branch check, `200` with the shape of Requirement 1, and session revocation when the branch changes. The account SHALL stay inactive.
2. IF `{id}` is a deleted staff account, THEN the move SHALL return `404 Not Found`, as for an unknown id, and change nothing.
3. A move of an active account SHALL behave exactly as before this feature, apart from the two fields Requirement 1 adds to the response.

### Requirement 8: Access (FR-AUTH-003)

**User Story:** As the platform owner, I want only admins to manage staff accounts, so that staff contact details and the power to lock someone out stay with admins.

#### Acceptance Criteria

1. IF the caller of any endpoint in Requirements 2 to 6 has the `Staff` or `Customer` role, THEN the system SHALL return `403 Forbidden` and change nothing.
2. IF there is no valid token, THEN the system SHALL return `401 Unauthorized`.
3. Admin accounts SHALL NOT be affected by any of these endpoints: an admin's id gets `404`, as any non-staff user does.
4. The gateway SHALL need no change. Its existing `auth` route (`/api/auth/{**catch-all}`, any method) forwards every new path, with the JWT checked at the edge. None of the new paths SHALL be rate-limited, and the CORS policy already allows `DELETE`.
5. Every new endpoint SHALL appear in Auth's OpenAPI document, so Scalar shows it through the gateway in Development.

## Discrepancies with the BRD

| BRD | This spec | Decision |
|---|---|---|
| `FR-AUTH-006` says "create staff accounts bound to a branch and move a staff member to another branch". | Adds list, read one, deactivate, reactivate and delete. | The row is widened in the BRD (section 8 and the Auth endpoint draft in section 13) by this feature. |
| The BRD does not say what deleting an account means. | Soft delete: the row stays, the email stays registered, and nothing restores it. | Accepted in the plan (2026-10-24), recorded as an ADR during design. |

## Out of scope

- **Undeleting an account, and purging deleted rows.** Delete is final from the API.
- **Editing a staff member's name, email or phone, and resetting their password.**
- **Customers and admins:** deactivating, deleting or listing them, and a general user search. The list has no text search; a `search` parameter can be added later without breaking anything.
- **Cutting off issued access tokens.** Deactivation, deletion and moves end sessions, not access tokens (Requirement 4.4).
- **Branch names in the list.** Items carry `branchId`; the client joins names from `GET /api/branches`, so Auth makes no call to Availability for a read.
- **The web client's screens:** the frontend's F7 phase in `smart-appointments-web`.
- **An index for the list query.** Staff accounts number in the tens. An index can be added later with no API change.
