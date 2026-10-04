# My appointments — Requirements

## Introduction

A customer lists the appointments they have booked, filtered by status and by whether they are upcoming or past, one page at a time. The feature is a single read endpoint, `GET /api/appointments/my`, owned by the Booking service. It reads only Booking's own `Appointments` table; it calls no other service.

The spec refines section 8 (`FR-BKG-003`, "View Own Appointments"), section 10 (Performance: pagination, indexes) and section 13 (`GET /api/appointments/my`) of [`docs/requirements.md`](../../requirements.md). It builds on [`booking-appointments`](../booking-appointments/requirements.md), whose Requirement 7 defines the appointment shape returned here and whose Out of scope section deferred this feature, and on the shared error body of [`shared-web-infrastructure`](../shared-web-infrastructure/requirements.md) Requirement 4. The service foundations of the Booking service (JWT validation, policies, `/healthz`, `TimeProvider`) apply unchanged and are not restated.

## Requirements

### Requirement 1: List my appointments (FR-BKG-003)

**User Story:** As a customer, I want to see my own appointments, so that I can find the ones I booked and open or cancel them.

#### Acceptance Criteria

1. WHEN a caller with the `Customer` role GETs `/api/appointments/my`, THEN the system SHALL return `200 OK` with `{ items, page, pageSize, totalCount }`, where `items` holds the caller's appointments on the requested page, `page` and `pageSize` echo the values applied (including defaults), and `totalCount` is the number of the caller's appointments that match the filters, across all pages.
2. WHEN the caller is identified, THEN the system SHALL use only the `sub` claim of the token. The endpoint SHALL accept no customer id in the path, query or body, and a `customerId` query parameter SHALL be ignored. Every returned appointment SHALL have `CustomerId` equal to the caller's id, so a customer can never see another customer's appointments.
3. IF the `sub` claim is missing or is not a non-empty GUID, THEN the system SHALL return `401 Unauthorized`, as the other appointment endpoints do.
4. WHEN the caller has no appointments, or none match the filters, THEN the system SHALL return `200 OK` with `items` empty and `totalCount` `0`. The system SHALL NOT return `404` for an empty list.
5. WHEN an appointment is returned, THEN each item SHALL have exactly the shape of `GET /api/appointments/{id}` (`booking-appointments` Requirement 7.1): `{ Id, CustomerId, SlotId, BranchId, ServiceTypeId, StartUtc, EndUtc, Status, CreatedAtUtc, UpdatedAtUtc, CancelledAtUtc }`, with `Status` written as `"Booked"` or `"Cancelled"`. The items SHALL NOT include branch names, service type names or any other data owned by Availability.
6. The system SHALL include appointments in every status (`Booked` and `Cancelled`) unless the `status` filter of Requirement 2 narrows them.
7. The request SHALL NOT call Availability or any other service, and SHALL NOT change any data.

### Requirement 2: Filter by status and by time (FR-BKG-003)

**User Story:** As a customer, I want to separate my upcoming appointments from my history, and my cancelled ones from my active ones, so that I see what I need without scrolling.

#### Acceptance Criteria

1. WHERE the `status` query parameter is `Booked` or `Cancelled`, compared case-insensitively (`booked`, `CANCELLED`), the system SHALL return only appointments with that status, and `totalCount` SHALL count only those. WHERE `status` is omitted, the system SHALL return both statuses.
2. WHERE the `when` query parameter is `upcoming` (case-insensitive), the system SHALL return only appointments whose `StartUtc` is at or after the current time, and WHERE it is `past`, only those whose `StartUtc` is before the current time. WHERE `when` is omitted, the system SHALL apply no time filter. "Current time" SHALL come from `TimeProvider`, read once per request, so an appointment starting exactly "now" is `upcoming` and never both or neither.
3. WHERE both filters are given, the system SHALL apply both (for example `status=Booked&when=upcoming` returns the customer's active future appointments).
4. IF `status` is present but is not `Booked` or `Cancelled` (including an empty value, `Completed`, or `NoShow`), THEN the system SHALL return `400 Bad Request`. IF `when` is present but is not `upcoming` or `past`, THEN `400 Bad Request`. The body SHALL be the shared error body `{"status":400,"detail":"..."}` (`application/problem+json`, `shared-web-infrastructure` Requirement 4), and `detail` SHALL name every invalid parameter.
5. WHEN a `Booked` appointment's start has passed, THEN it SHALL be listed as `Booked` and `past`. The endpoint SHALL NOT infer, change or store a completed status; `FR-BKG-006` is separate.

### Requirement 3: Order and paging (FR-BKG-003, BRD section 10 Performance)

**User Story:** As a customer with many appointments, I want a predictable order and pages of a bounded size, so that the list loads quickly and I never see an item twice or miss one while paging.

#### Acceptance Criteria

1. WHERE `when` is `upcoming`, the system SHALL order items by `StartUtc` ascending (soonest first). WHERE `when` is `past` or omitted, the system SHALL order by `StartUtc` descending (most recent first).
2. WHEN two appointments have the same `StartUtc`, THEN the system SHALL order them by `Id` ascending, in either direction of Criterion 1, so the same request on unchanged data always returns the same order and paging neither repeats nor skips an item.
3. The `page` query parameter SHALL be an integer of at least 1 and default to `1`. The `pageSize` parameter SHALL be an integer from 1 to 100 inclusive and default to `20`. WHEN `page` is `p` and `pageSize` is `n`, THEN `items` SHALL hold the matching appointments at positions `(p-1)*n` up to `p*n - 1` of the order above, so a page holds at most `n` items.
4. IF `page` is less than 1, or `pageSize` is less than 1 or greater than 100, or either is not an integer, THEN the system SHALL return `400 Bad Request` with the shared error body of Requirement 2.4, and SHALL NOT clamp the value.
5. WHEN `page` is beyond the last page, THEN the system SHALL return `200 OK` with `items` empty, the requested `page` and `pageSize`, and the correct `totalCount`.
6. WHEN the request is served, THEN the system SHALL read only the requested page and the count from the database. It SHALL NOT load all of the customer's appointments and page them in memory.
7. WHEN several of the checks above fail at once, THEN the system SHALL return one `400 Bad Request` listing every failed rule, as the create endpoint does.

### Requirement 4: Access and routing (FR-BKG-003)

**User Story:** As the platform owner, I want only customers to use this endpoint and the gateway to route it without further change, so that the feature adds no new exposure.

#### Acceptance Criteria

1. IF the caller has the `Staff` or `Admin` role, THEN the system SHALL return `403 Forbidden` from the `CustomerPolicy`. Staff and admins have no "my appointments"; their views are `FR-BKG-004` and `GET /api/appointments/{id}`.
2. IF there is no valid token, THEN the system SHALL return `401 Unauthorized`.
3. The route `/api/appointments/my` SHALL be matched ahead of `/api/appointments/{id:guid}`, so `my` is never parsed as an appointment id and `GET /api/appointments/{id}` is unchanged.
4. The gateway SHALL need no change: the existing `appointments` route (`/api/appointments/{**catch-all}`) already forwards the path to the Booking cluster with the JWT checked at the edge, and the endpoint SHALL NOT be rate limited at the gateway (only `POST /api/appointments`, login and slot search are). The gateway tests SHALL NOT need a new route.
5. WHEN the endpoint is documented, THEN it SHALL appear in Booking's OpenAPI document, so Scalar shows it through the gateway in Development.

## Discrepancies with the BRD

| BRD | This spec | Decision |
|---|---|---|
| Section 13 draws `GET /api/appointments/my` with no parameters and no response shape. | Adds `status`, `when`, `page` and `pageSize`, and wraps the list as `{ items, page, pageSize, totalCount }`. | Section 10 asks for pagination, and a customer's history grows without bound, so an unbounded array would break that NFR. Filters follow `FR-BKG-003`'s "history" and the upcoming/past split a client needs. |
| `FR-BKG-003` says "appointment history" without defining what "history" includes. | History is every `Booked` and `Cancelled` appointment, with optional filters. | Matches the two statuses that exist today; `Completed` and `NoShow` arrive with `FR-BKG-006` and will extend the `status` values then. |

## Out of scope

- **The staff and admin view of a branch's appointments (`FR-BKG-004`)** and **complete and no-show (`FR-BKG-006`)**. Each is its own spec. A staff or admin caller gets `403` here.
- **Branch and service type names.** The response holds ids only; the client joins them with `GET /api/branches` and `GET /api/services`. Booking does not call Availability for a read, which keeps this endpoint fast and independent of Availability's uptime.
- **Filtering by branch, service type or a date range**, and **free-text search.** Only `status` and `when` exist.
- **Cursor (keyset) paging.** Offset paging with a stable order is enough for the expected volume per customer; a cursor can replace it later without changing the item shape.
- **Sorting options.** The order is fixed by Requirement 3.1.
- **Caching and `ETag` support.**

## Notes for design

- BRD section 10 names "indexes" as a performance measure but sets no numeric target, so this spec does not state a latency requirement. The design should serve the filter and order from an index on `(CustomerId, StartUtc)` (with `Id` for the tie-break) and say in the design whether `Status` joins that index. Booking's existing overlap check may already use a similar index; the design should reuse it if it fits.
- `StartUtc` is a UTC instant; the "now" comparison and the stored values are both UTC, so no time zone conversion belongs in this endpoint.
