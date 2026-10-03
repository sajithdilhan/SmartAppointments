# Availability service types — Requirements

## Introduction

A service type is something a customer can book or queue for — a vehicle inspection, a passport renewal — with a fixed duration. The Availability service owns service types because slots are generated per branch *and* service type, and Booking and Queue will refer to a service type by the identifier this service assigns.

Service types form one catalogue shared by every branch. Which branch offers which service is decided by generating slots, not by a separate assignment (see *Out of scope*). This spec covers creating, updating, activating, deactivating and reading service types. It refines sections 6, 8 (Availability Management), 9 and 13 of [`docs/requirements.md`](../../requirements.md) and covers `FR-AVL-002`. It reuses the service foundations delivered by [`availability-branches`](../availability-branches/requirements.md) (Requirements 5 and 6 there) and does not restate them.

## Requirements

### Requirement 1: Create a service type (FR-AVL-002)

**User Story:** As an administrator, I want to define a service with a code, name and duration, so that slots can be generated for it.

#### Acceptance Criteria

1. WHEN a caller with the `Admin` role POSTs a valid `CreateServiceTypeRequest` to `/api/services`, THEN the system SHALL create the service type and return `201 Created` with a `Location` header pointing at `/api/services/{id}` and the service type as defined in Requirement 4.6.
2. WHEN a service type is created, THEN the system SHALL assign a version-7 GUID, set `IsActive` to true and set `CreatedAtUtc` to the current UTC time.
3. WHEN a create request is received, THEN the system SHALL reject it with `400 Bad Request` listing every failed rule IF any of the following does not hold:
   - `Code` is 2–30 characters made up of letters, digits, hyphens and underscores, and neither starts nor ends with a hyphen or underscore;
   - `Name` is non-empty and at most 100 characters;
   - `Description`, if supplied, is at most 500 characters;
   - `DurationMinutes` is between 5 and 480 inclusive and a multiple of 5.
4. WHEN a service type is stored, THEN the system SHALL trim every text field, upper-case `Code`, and store a blank `Description` as null.
5. IF another service type, active or inactive, already has the same normalised `Code`, THEN the system SHALL return `409 Conflict` with "A service type with code '{Code}' already exists." and SHALL NOT write to the database, including when the duplicate is only detected by the unique index at commit time.

### Requirement 2: Update a service type (FR-AVL-002)

**User Story:** As an administrator, I want to correct a service's name, description or duration, so that new slots reflect how long the service really takes.

#### Acceptance Criteria

1. WHEN a caller with the `Admin` role PUTs a valid `UpdateServiceTypeRequest` to `/api/services/{id}` for an existing service type, THEN the system SHALL replace its `Name`, `Description` and `DurationMinutes`, set `UpdatedAtUtc`, and return `200 OK` with the updated service type.
2. WHEN an update request is received, THEN the system SHALL apply the rules of Requirement 1.3 and 1.4 (except those for `Code`) and return `400 Bad Request` listing every failed rule.
3. WHERE an update request carries a `Code`, the system SHALL ignore it; the code is immutable.
4. IF no service type exists with the given `id`, THEN the system SHALL return `404 Not Found`.
5. WHEN `DurationMinutes` changes, THEN slots that already exist SHALL keep their times; only slots generated afterwards use the new duration.
6. WHEN an update is applied, THEN `IsActive` SHALL NOT change, and an inactive service type SHALL still be updatable.

### Requirement 3: Deactivate and reactivate a service type (FR-AVL-002)

**User Story:** As an administrator, I want to withdraw a service and bring it back, so that customers stop seeing it without losing the records that reference it.

#### Acceptance Criteria

1. WHEN a caller with the `Admin` role POSTs to `/api/services/{id}/deactivate` or `/api/services/{id}/activate`, THEN the system SHALL set `IsActive` accordingly, set `UpdatedAtUtc`, and return `204 No Content`.
2. WHEN either endpoint is called on a service type already in the requested state, THEN the system SHALL return `204 No Content` without writing to the database.
3. IF no service type exists with the given `id`, THEN either endpoint SHALL return `404 Not Found`.
4. WHERE a service type exists, the system SHALL provide no way to delete it.
5. WHERE a service type is inactive, the system SHALL NOT generate new slots for it (BRD section 9; enforced by [`availability-slots`](../availability-slots/requirements.md)).

### Requirement 4: Read service types (FR-AVL-002)

**User Story:** As a customer, I want to see which services are offered, so that I can choose one to book. As an administrator, I also want to see withdrawn ones.

#### Acceptance Criteria

1. WHEN an authenticated caller holding any of the `Customer`, `Staff` or `Admin` roles GETs `/api/services`, THEN the system SHALL return `200 OK` with the active service types ordered by `Name`, then `Code`, without pagination.
2. WHERE the caller's role is `Admin` and the request carries `?includeInactive=true`, the system SHALL include inactive service types; for any other role the flag SHALL be ignored.
3. WHEN such a caller GETs `/api/services/{id}` for an active service type, THEN the system SHALL return `200 OK` with it.
4. IF no service type exists with the given `id`, OR it is inactive and the caller's role is not `Admin`, THEN the system SHALL return `404 Not Found` with the same message in both cases.
5. WHEN reading service types, THEN the system SHALL use no-tracking queries.
6. WHEN a service type is returned, THEN the body SHALL contain `{ Id, Code, Name, Description, DurationMinutes, IsActive, CreatedAtUtc, UpdatedAtUtc }`.
7. IF a request to any endpoint in this spec has no valid token, THEN the system SHALL return `401`; IF the caller lacks the required role, THEN `403`.

## Discrepancies with the BRD

| BRD | This spec | Decision |
|---|---|---|
| Section 13 names the routes `/api/services`. | Routes are `/api/services`, but the entity and table are `ServiceType`/`ServiceTypes`. | "Service" is overloaded in a microservice codebase. The route stays as drafted; the type name avoids the clash. |
| Section 13 lists only `POST` and `GET /api/services`. | Adds `GET {id}`, `PUT {id}`, `activate` and `deactivate`. | `FR-AVL-002` requires updating and an active status; same reasoning as for branches. |
| The event example in section 14 uses `"serviceId": "VEHICLE_INSPECTION"`. | Service types have a GUID `Id` and a unique, immutable `Code`, which allows underscores so that `VEHICLE_INSPECTION` is a valid code. | Events should carry both `serviceTypeId` and `serviceTypeCode`, as for branches. |

## Out of scope

- **Per-branch service offerings.** A branch offers a service when slots exist for that pair. An explicit offering list can be added if a branch ever needs to advertise a service it has no slots for.
- **Price, category, required documents.** Not in the BRD.
- **Hard delete.** See Requirement 3.4.
