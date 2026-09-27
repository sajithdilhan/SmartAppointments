# Availability branches — Requirements

> **Partly retro-fitted spec.** A scaffold for this feature already exists in `src/Services/Availability/` — a `Branch` entity, a `GetBranchesQuery` and a `BranchesController` — but none of it is real: the handler returns hard-coded data and the host never registers the layers it depends on. The requirements below describe the feature as it should be built. What the scaffold does today, and where it disagrees, is recorded under *Known gaps* rather than being treated as delivered behaviour.

## Introduction

A branch is a physical location where customers are served. The Availability service owns branches because every other thing it manages — service types, working hours, slots — hangs off one, and Booking and Queue refer to branches by the identifier this service assigns.

This spec covers creating, updating, activating and deactivating a branch, and reading the branch list. It also covers the service foundations the first real Availability endpoint needs: persistence, validation of the JWTs that Auth issues, secret handling, and health checks. Those foundations are included here rather than in a separate spec because this feature is the first to need them and cannot ship without them. The spec refines sections 6, 8 (Availability Management), 9, 12 and 13 of [`docs/requirements.md`](../../requirements.md) and covers `FR-AVL-001`.

Requirement 1 is built, together with Requirements 5 and 6. The exception is 5.5, which has no caller until the read endpoints exist. Requirements 2–4 are not built yet. [`tasks.md`](tasks.md) tracks which is which.

## Requirements

### Requirement 1: Create a branch (FR-AVL-001)

**User Story:** As an administrator, I want to create a branch with a code, name, address and phone number, so that customers can book appointments and join queues at it.

#### Acceptance Criteria

1. WHEN a caller with the `Admin` role POSTs a valid `CreateBranchRequest` to `/api/branches`, THEN the system SHALL create the branch and return `201 Created` with a `Location` header pointing at `/api/branches/{id}` and a body containing the branch as defined in Requirement 4.6.
2. WHEN a branch is created, THEN the system SHALL assign a version-7 GUID as its identifier, set `IsActive` to true, and set `CreatedAtUtc` to the current UTC time.
3. WHEN a create request is received, THEN the system SHALL reject it with `400 Bad Request` and a message listing every failed rule IF any of the following does not hold:
   - `Code` is 2–10 characters made up of letters, digits and hyphens, and neither starts nor ends with a hyphen;
   - `Name` is non-empty and at most 100 characters;
   - `Description`, if supplied, is at most 500 characters;
   - `Address` is non-empty and at most 200 characters;
   - `PhoneNumber` is non-empty and matches E.164 (`^\+?[1-9]\d{1,14}$`), the same rule Auth applies to customers.
4. WHEN a branch is stored, THEN the system SHALL trim whitespace from every text field and upper-case `Code`, so that `pg` and ` PG ` are the same code.
5. IF another branch, active or inactive, already has the same normalised `Code`, THEN the system SHALL return `409 Conflict` with "A branch with code '{Code}' already exists." and SHALL NOT write to the database.
6. WHERE the duplicate code is only discovered at commit time because a concurrent create won the race, the system SHALL return the same `409 Conflict` rather than surfacing the unique-index violation as a `500`.
7. WHERE two branches would share a `Code`, the database SHALL reject the second one via a unique index.
8. WHERE two branches share a `Name`, the system SHALL accept both — the code is the identifying key, not the name.

### Requirement 2: Update a branch (FR-AVL-001)

**User Story:** As an administrator, I want to correct a branch's name, description, address or phone number, so that customers see accurate details without the branch losing its history.

#### Acceptance Criteria

1. WHEN a caller with the `Admin` role PUTs a valid `UpdateBranchRequest` to `/api/branches/{id}` for an existing branch, THEN the system SHALL replace its `Name`, `Description`, `Address` and `PhoneNumber`, set `UpdatedAtUtc` to the current UTC time, and return `200 OK` with the updated branch.
2. WHEN an update request is received, THEN the system SHALL apply the same field rules and trimming as Requirement 1.3 and 1.4 and return `400 Bad Request` listing every failed rule.
3. WHERE an update request carries a `Code`, the system SHALL ignore it — the code is immutable once assigned, because Booking and Queue will copy it into their own records and events.
4. IF no branch exists with the given `id`, THEN the system SHALL return `404 Not Found`.
5. WHEN an update is applied, THEN the system SHALL NOT change `IsActive`; activation is governed only by Requirement 3.
6. WHERE the branch is inactive, the system SHALL still allow its details to be updated.

### Requirement 3: Deactivate and reactivate a branch (FR-AVL-001)

**User Story:** As an administrator, I want to take a branch out of service and bring it back, so that customers stop seeing a closed branch without destroying the records that reference it.

#### Acceptance Criteria

1. WHEN a caller with the `Admin` role POSTs to `/api/branches/{id}/deactivate`, THEN the system SHALL set `IsActive` to false, set `UpdatedAtUtc`, and return `204 No Content`.
2. WHEN a caller with the `Admin` role POSTs to `/api/branches/{id}/activate`, THEN the system SHALL set `IsActive` to true, set `UpdatedAtUtc`, and return `204 No Content`.
3. WHEN either endpoint is called on a branch that is already in the requested state, THEN the system SHALL return `204 No Content` without writing to the database, so that both endpoints are idempotent.
4. IF no branch exists with the given `id`, THEN either endpoint SHALL return `404 Not Found`.
5. WHERE a branch exists, the system SHALL provide no way to delete it. Deactivation is the only way to retire a branch, because appointments and queue items will hold its identifier.

### Requirement 4: Read branches (FR-AVL-001)

**User Story:** As a customer, I want to see the branches that are open for business, so that I can pick one to book at or queue at. As an administrator, I also want to see the ones that are closed, so that I can reactivate or correct them.

#### Acceptance Criteria

1. WHEN an authenticated caller holding any of the `Customer`, `Staff` or `Admin` roles GETs `/api/branches`, THEN the system SHALL return `200 OK` with the active branches ordered by `Name`, then by `Code`.
2. WHERE the caller's role is `Admin` and the request carries `?includeInactive=true`, the system SHALL include inactive branches in the list.
3. WHERE the caller's role is not `Admin`, the system SHALL ignore `includeInactive` and return only active branches, rather than rejecting the request.
4. WHEN an authenticated caller holding any of the three roles GETs `/api/branches/{id}` for an active branch, THEN the system SHALL return `200 OK` with that branch.
5. IF no branch exists with the given `id`, OR the branch is inactive and the caller's role is not `Admin`, THEN the system SHALL return `404 Not Found`, so that a closed branch looks the same as a missing one to anyone who cannot act on it.
6. WHEN a branch is returned, THEN the body SHALL contain `{ Id, Code, Name, Description, Address, PhoneNumber, IsActive, CreatedAtUtc, UpdatedAtUtc }`, with `Description` and `UpdatedAtUtc` null when unset.
7. WHEN reading branches, THEN the system SHALL use read-only (no-tracking) queries.
8. WHEN the list is read, THEN the system SHALL return every matching branch in one response, without pagination.

### Requirement 5: Accept the tokens Auth issues

**User Story:** As a signed-in user, I want the token Auth gave me to work here too, so that I sign in once for the whole system.

*(No FR-ID — this applies `FR-AUTH-003` to a second service.)*

#### Acceptance Criteria

1. WHEN a bearer token is presented, THEN the system SHALL validate issuer, audience, lifetime and signing key against the same `Jwt:Issuer`, `Jwt:Audience` and `Jwt:SecretKey` values that Auth signs with, with zero clock skew, and SHALL treat the `role` claim as the role claim type.
2. IF a request to any endpoint in this spec carries no token or an invalid one, THEN the system SHALL return `401 Unauthorized`.
3. IF a caller with a valid token lacks the role an endpoint requires, THEN the system SHALL return `403 Forbidden` before the handler runs.
4. WHEN the service starts, THEN the system SHALL register the same named policies as Auth (`AdminPolicy`, `StaffPolicy`, `CustomerPolicy`, `AdminOrStaffPolicy`, `AllowedOriginsPolicy`) with the same role sets, so that a policy name means the same thing in every service.
5. WHEN the handler needs the caller's role, THEN the controller SHALL read it from the `role` claim and pass it to the query explicitly, rather than letting the handler reach into `HttpContext`.

### Requirement 6: Run as a real service

**User Story:** As an operator, I want the Availability service to start, persist, report its health and fail loudly when misconfigured, in the same way Auth does, so that there is one way to run every service.

*(No FR-ID — this refines the security, observability and data-ownership sections of [`docs/requirements.md`](../../requirements.md).)*

#### Acceptance Criteria

1. WHEN the service starts, THEN it SHALL persist branches in its own PostgreSQL database (`availability_db` in the BRD) and SHALL NOT share tables with any other service.
2. WHERE a configuration value is a secret — the database connection string and the JWT signing key — the checked-in `appsettings.json` SHALL carry an empty placeholder and nothing more.
3. IF `ConnectionStrings:DefaultConnection` or `Jwt:SecretKey` is absent or blank at startup, THEN the system SHALL throw an `InvalidOperationException` naming the key and the `dotnet user-secrets` command that sets it.
4. IF `Jwt:SecretKey` is shorter than 32 bytes, or `Jwt:Issuer` or `Jwt:Audience` is blank, THEN the system SHALL refuse to start.
5. WHEN `/healthz` is requested, THEN the system SHALL report healthy only if the database is reachable.
6. IF an unhandled exception escapes a handler, THEN the system SHALL return `500` as `application/problem+json` without echoing the exception message, in the same shape as Auth.
7. WHEN a handler returns a failed `Result`, THEN the controller SHALL map `Error.Status` 400, 401, 403, 404 and 409 to the matching response, so that no failure is reported under the wrong status.
8. WHERE the environment is Development, the system SHALL serve the OpenAPI document and the Scalar UI.
9. WHEN the schema changes, THEN the change SHALL be delivered as an EF Core migration that `dotnet ef` can generate without a live database or populated secrets.

## Discrepancies with the BRD

| BRD | This spec | Decision |
|---|---|---|
| The event example in section 14 uses `"branchId": "PG"` — a short code, not a GUID. | Branches have a GUID `Id` **and** a unique, immutable `Code`. | Both are needed. The GUID is the key other services store; the code is the human-readable handle (and a natural queue-number prefix). The BRD example should be changed to carry both `branchId` (GUID) and `branchCode`. |
| Section 13 lists only `POST /api/branches` and `GET /api/branches`. | Adds `GET /api/branches/{id}`, `PUT /api/branches/{id}`, and `POST /api/branches/{id}/activate` and `/deactivate`. | `FR-AVL-001` says "create and update", which the two drafted routes cannot do. The `GET` by id is also what the `201` `Location` header points at. |
| The route in section 13 is lower-case. | The existing controller uses `[Route("api/[controller]")]`, which gives `/api/Branches`. | Cosmetic: ASP.NET routing ignores case when matching. Same decision as in `auth-identity`. |
| Section 10 asks for pagination. | Requirement 4.8 does not paginate the branch list. | A deployment has tens of branches, not thousands. Pagination is left for slot search, where the volume is real. |

## Out of scope

- **Working hours and time zone.** Section 6 of the BRD gives Availability working hours. They belong with slot generation (`FR-AVL-003`), which is the first thing to consume them, and will be specced there. Adding them later is a single migration.
- **Staff-to-branch assignment.** `FR-BKG-004` and the queue flow refer to a staff member's "assigned branch", but nothing in the BRD says which service owns that assignment. It needs a decision before the Booking or Queue specs, and is not made here.
- **Cascading deactivation.** Deactivating a branch does not cancel its slots, appointments or queue items. What an inactive branch forbids — new slots, new bookings, joining the queue — will be enforced by the specs for those features, which can check `IsActive`.
- **Hard delete.** See Requirement 3.5.
- **Gateway routing.** The gateway is still a stub. Branch endpoints are reached directly on the service until the gateway is specced.
- **Integration tests.** The feature follows the pure-unit-test convention in `CLAUDE.md`. The repository and the migration wait for the integration-test harness, as in `auth-identity`.

## Known gaps

These describe the scaffold as it stood in `src/Services/Availability/` before this spec. Each has a matching item in `tasks.md`. Closed gaps are struck through rather than deleted, as in `auth-identity`.

1. **Secrets are committed.** ~~In the working tree~~ closed by task 1: `appsettings.json` now carries empty placeholders. **Still open operationally:** the signing key that was committed here is the same one commit `335a6cb` removed from Auth. It remains in the history of both services and must be rotated in every environment that uses it.
2. ~~**The only endpoint cannot run.**~~ Closed by task 4. `Program.cs` now composes the layers in Auth's order.
3. **The handler returns placeholder data.** `GetBranchesHandler` builds two hard-coded branches with fresh GUIDs on every call, and `GET /api/Branches` is still anonymous. Open until task 9.
4. ~~**The model is incomplete.**~~ Closed for creation by task 2. `BranchResponse` replaces `BranchDto`. The update and activation behaviours arrive with tasks 7 and 8.
5. ~~**No security or plumbing.**~~ Closed by tasks 3 and 4.
6. ~~**No tests.**~~ Closed by task 6's unit tests.
7. ~~**The `.http` file is the template's.**~~ Closed by task 4.
