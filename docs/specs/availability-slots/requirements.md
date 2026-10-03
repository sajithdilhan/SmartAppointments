# Availability slots — Requirements

## Introduction

A slot is a bookable interval at one branch for one service type, with a capacity. Administrators generate slots in bulk for a date range from the branch's weekly working hours, and customers search the slots that can still be booked on a given day. This is what Booking will reserve against.

Slots are generated from working hours, so this spec also gives each branch a **schedule**: an IANA time zone and its opening hours for each day of the week. The branches spec deliberately left working hours and time zones to this one.

The spec refines sections 6, 7.1, 8 (Availability Management), 9 and 13 of [`docs/requirements.md`](../../requirements.md) and covers `FR-AVL-003` and `FR-AVL-004`. Reserving and releasing a slot (`FR-AVL-005`) is out of scope here; it is specified and built in [`booking-appointments`](../booking-appointments/requirements.md), and the slot model already carries the reserved count it will need. The service foundations of [`availability-branches`](../availability-branches/requirements.md) (Requirements 5 and 6) apply unchanged.

## Requirements

### Requirement 1: Set a branch's schedule (FR-AVL-003, no FR-ID of its own)

**User Story:** As an administrator, I want to record a branch's time zone and weekly opening hours, so that generated slots fall inside the hours the branch is actually open, in its own local time.

#### Acceptance Criteria

1. WHEN a caller with the `Admin` role PUTs a valid `SetBranchScheduleRequest` to `/api/branches/{id}/schedule`, THEN the system SHALL replace the branch's time zone and weekly working hours, set `UpdatedAtUtc`, and return `200 OK` with the branch.
2. WHEN a schedule request is received, THEN the system SHALL reject it with `400 Bad Request` listing every failed rule IF any of the following does not hold:
   - `TimeZoneId` is an IANA time zone identifier known to the server, such as `Asia/Colombo`;
   - each entry of `WorkingHours` names a valid day of the week, and no day appears more than once;
   - `OpensAt` and `ClosesAt` are 24-hour `HH:mm` times and `OpensAt` is earlier than `ClosesAt`.
3. WHERE a day of the week is absent from `WorkingHours`, the branch SHALL be treated as closed on that day. An empty list closes the branch every day.
4. IF no branch exists with the given `id`, THEN the system SHALL return `404 Not Found`.
5. WHEN a branch is returned by any endpoint, THEN the body SHALL also contain `TimeZoneId`, null until a schedule is set, and `WorkingHours`, as `{ DayOfWeek, OpensAt, ClosesAt }` entries ordered Monday to Sunday, empty until a schedule is set.
6. WHEN a schedule changes, THEN slots that already exist SHALL NOT change.
7. WHERE the branch is inactive, the system SHALL still accept a schedule.

### Requirement 2: Generate slots (FR-AVL-003)

**User Story:** As an administrator, I want to generate slots for a branch and service over a date range, so that customers have times to book without me creating each one.

#### Acceptance Criteria

1. WHEN a caller with the `Admin` role POSTs a valid `GenerateSlotsRequest` (`BranchId`, `ServiceTypeId`, `FromDate`, `ToDate`, `Capacity`) to `/api/slots/generate`, THEN the system SHALL create the slots described below and return `200 OK` with `{ CreatedCount, SkippedCount }`.
2. WHEN slots are generated, THEN for each date from `FromDate` to `ToDate` inclusive, read as a date in the branch's time zone, on which the branch has working hours, the system SHALL create back-to-back slots, each lasting the service type's `DurationMinutes`, starting at `OpensAt`, as long as the slot ends no later than `ClosesAt`.
3. WHEN a slot is created, THEN it SHALL store its branch, service type, local date, start and end as UTC instants, `Capacity`, a reserved count of zero, and `CreatedAtUtc`.
4. WHERE a candidate slot would start at or before the current time, the system SHALL NOT create it.
5. WHERE a local start time does not exist on that date because the clocks go forward, the system SHALL NOT create that slot. Where a local time occurs twice because the clocks go back, the system SHALL use the first occurrence (standard time, as `TimeZoneInfo` does).
6. WHERE a candidate slot overlaps an existing slot for the same branch and service type, the system SHALL skip it and count it in `SkippedCount`, so that generating the same range twice creates nothing the second time.
7. WHEN a generate request is received, THEN the system SHALL reject it with `400 Bad Request` listing every failed rule IF `BranchId` or `ServiceTypeId` is empty, `FromDate` is after `ToDate`, the range spans more than 31 days, or `Capacity` is not between 1 and 100.
8. IF the branch or the service type does not exist, THEN the system SHALL return `404 Not Found`.
9. IF the branch or the service type is inactive, THEN the system SHALL return `409 Conflict` and create nothing (BRD section 9: "inactive services cannot have new slots").
10. IF the branch has no schedule, or no working hours on any day, THEN the system SHALL return `409 Conflict` with "Branch '{Code}' has no working hours." and create nothing.
11. WHERE two generate requests for the same branch and service type race, the database SHALL reject a second slot with the same start via a unique index, and the losing request SHALL return `409 Conflict` instead of a `500`.

### Requirement 3: Search available slots (FR-AVL-004)

**User Story:** As a customer, I want to see the slots I can still book at a branch for a service on a given day, so that I can choose a time.

#### Acceptance Criteria

1. WHEN an authenticated caller holding any of the `Customer`, `Staff` or `Admin` roles GETs `/api/slots/available?branchId=&serviceId=&date=`, THEN the system SHALL return `200 OK` with the slots for that branch and service type on that local date that start after the current time and have available capacity, ordered by start time.
2. WHEN a slot is returned, THEN the body SHALL contain `{ Id, BranchId, ServiceTypeId, Date, StartAtUtc, EndAtUtc, LocalStartTime, LocalEndTime, Capacity, AvailableCapacity }`, where the local times are in the branch's time zone and `AvailableCapacity` is `Capacity` minus the reserved count.
3. IF `branchId` or `serviceId` is missing or empty, or `date` is missing, THEN the system SHALL return `400 Bad Request`.
4. IF the branch or the service type does not exist or is inactive, THEN the system SHALL return `404 Not Found`, whatever the caller's role, because nothing at an inactive branch or for an inactive service can be booked.
5. WHERE no slot matches, the system SHALL return `200 OK` with an empty list.
6. WHEN searching, THEN the system SHALL use no-tracking queries and return every match without pagination; a single day holds at most a few hundred slots.
7. IF a request to any endpoint in this spec has no valid token, THEN the system SHALL return `401`; IF the caller lacks the required role, THEN `403`.

## Discrepancies with the BRD

| BRD | This spec | Decision |
|---|---|---|
| `FR-AVL-003` lists "duration" as a generation input. | A slot's length is the service type's `DurationMinutes`; the request has no duration. | One service, one length: a slot that is shorter than its service cannot be honoured, and one that is longer wastes capacity. To change the length, update the service type. |
| `FR-AVL-003` says nothing about which hours slots fall in. | Slots follow the branch's weekly schedule. | Without opening hours the admin would have to give times for every generation request, and nothing would stop slots at 3 a.m. Section 6 already lists working hours as Availability's responsibility. |
| Section 13: `GET /api/slots/available?branchId=&serviceId=&date=`. | Kept as drafted, including the `serviceId` parameter name. | — |
| Section 10 asks for pagination. | Search is not paginated (Req 3.6). | It is scoped to a single day for one branch and service. |

## Out of scope

- **Reserve and release (`FR-AVL-005`).** They need the Booking service and service-to-service authentication. `Slot` already carries `Capacity` and `ReservedCount`. They are specified in [`booking-appointments`](../booking-appointments/requirements.md).
- **Deleting, editing or blocking out slots**, and holidays or one-off closures. Each needs its own rules about slots that already have reservations.
- **An admin view of all slots, including full and past ones.** Reporting will cover utilisation.
- **Split shifts (two intervals on one day).** One interval per day; a lunch break can be modelled later as a second interval without breaking this API.
