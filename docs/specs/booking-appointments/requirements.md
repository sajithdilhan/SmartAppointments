# Booking appointments — Requirements

## Introduction

A customer books an appointment by reserving one of the slots the Availability service generated. This spec delivers the first thread of the Booking service end to end: reserving and releasing slot capacity inside Availability (internal, service-to-service), and in a new Booking service creating an appointment idempotently, reading it back and cancelling it.

Booking owns appointments; Availability owns capacity. Booking never writes to Availability's database. It asks Availability to reserve or release one unit of a slot's capacity over HTTP, and Availability decides whether that is allowed. Because HTTP calls can be retried and can fail half-way, the reserve and release operations are **idempotent per appointment**, and the create-appointment endpoint is idempotent per `Idempotency-Key`.

The spec refines sections 7.1, 7.2, 8 (Availability Management, Booking), 9, 11.1, 11.4, 13 and 14 of [`docs/requirements.md`](../../requirements.md) and covers `FR-AVL-005`, `FR-BKG-001`, `FR-BKG-002` and `FR-BKG-005`. It builds on [`availability-slots`](../availability-slots/requirements.md), which already gave `Slot` its `Capacity` and `ReservedCount`, and on the shared web code of [`shared-web-infrastructure`](../shared-web-infrastructure/requirements.md). The service foundations of [`availability-branches`](../availability-branches/requirements.md) (Requirements 5 and 6 there: JWT validation, policies, `/healthz`, configuration that fails at startup) apply to the Booking service unchanged and are not restated.

## Requirements

### Requirement 1: Internal-only access to Availability's internal endpoints (FR-AVL-005, no FR-ID of its own)

**User Story:** As the platform owner, I want the reserve, release and slot-read endpoints to be callable only by other services, so that a customer with a valid token cannot change a slot's capacity.

#### Acceptance Criteria

1. WHEN a request to any route under `/internal/` carries the configured shared key in the `X-API-Key` header, THEN the system SHALL authenticate it as the internal service and let it reach the endpoint.
2. IF the `X-API-Key` header is missing, THEN the system SHALL return `401 Unauthorized`; IF it is present but not equal to the configured key, THEN `401 Unauthorized`.
3. IF a request to an `/internal/` route carries a valid customer, staff or admin JWT but no valid API key, THEN the system SHALL return `401 Unauthorized`. A JWT, whatever its role, SHALL NOT grant access to an internal route.
4. IF a request to an `/api/` route carries a valid API key but no JWT, THEN the system SHALL return `401 Unauthorized`. The API key SHALL NOT satisfy any role policy.
5. WHEN the key is compared, THEN the comparison SHALL take the same time whether it matches, differs in the first byte or differs in length.
6. WHEN a service that registers the internal-API scheme starts, THEN it SHALL fail with an explanatory message IF `InternalApi:Key` is missing, blank or shorter than 32 bytes. The key SHALL be empty in `appsettings.json`, filled in `appsettings.Development.json` for local development, and supplied by the environment (`InternalApi__Key`) everywhere else.
7. The key SHALL NOT be written to logs or returned in any response.
8. The scheme SHALL live in `SmartAppointments.BuildingBlocks.Web`, named by `Constants.ApiKeyAuthenticationScheme`, with a policy named by a new `Constants.InternalServicePolicy`, so that Queue and later services reuse it.

### Requirement 2: Read a slot internally (FR-AVL-005)

**User Story:** As the Booking service, I want to read a slot's identity, times and capacity by id, so that I can record what the customer is booking without trusting the client.

#### Acceptance Criteria

1. WHEN the internal service GETs `/internal/slots/{id}`, THEN the system SHALL return `200 OK` with `{ Id, BranchId, ServiceTypeId, StartUtc, EndUtc, Capacity, ReservedCount, AvailableCapacity }`, where `AvailableCapacity` is `Capacity` minus `ReservedCount`.
2. IF no slot exists with the given `id`, THEN the system SHALL return `404 Not Found`.
3. The read SHALL return the slot whether or not the branch or service type is active, and whether or not the slot has started. It reports facts; whether a slot can be reserved is decided by Requirement 3.

### Requirement 3: Reserve a slot (FR-AVL-005)

**User Story:** As the Booking service, I want to reserve one unit of a slot's capacity for an appointment, so that two customers cannot take the last place.

#### Acceptance Criteria

1. WHEN the internal service POSTs `{ AppointmentId }` to `/internal/slots/{id}/reserve` and the slot can be reserved, THEN the system SHALL record a reservation for `(slot, appointment)`, increase the slot's `ReservedCount` by one, and return `204 No Content`.
2. WHEN a reservation is made, THEN the reservation row and the increment SHALL be written in one database transaction, and the increment SHALL be a single conditional update that applies only while `ReservedCount < Capacity`, so that concurrent reservations of the last place cannot both succeed.
3. WHERE a reservation already exists for the same `(slot, appointment)`, the system SHALL return `204 No Content` and change nothing. This check SHALL come before the state checks in criterion 4, so a retry that arrives after the slot started or after the branch was deactivated still succeeds. Repeating the request any number of times SHALL never raise `ReservedCount` more than once.
4. IF a new reservation is requested and any of the following holds, THEN the system SHALL return `409 Conflict` with a message naming the reason, and change nothing:
   - the slot has already started (`StartUtc` is at or before the current time);
   - the slot's branch is inactive;
   - the slot's service type is inactive;
   - the slot is full (`ReservedCount` has reached `Capacity`).
5. IF no slot exists with the given `id`, THEN the system SHALL return `404 Not Found`.
6. IF `AppointmentId` is missing or the empty GUID, THEN the system SHALL return `400 Bad Request`.
7. `ReservedCount` SHALL never be negative or greater than `Capacity`. The database SHALL enforce this with a check constraint as well as the conditional updates.
8. WHEN "now" is needed, THEN the handler SHALL take it from `TimeProvider`.

### Requirement 4: Release a slot (FR-AVL-005)

**User Story:** As the Booking service, I want to give a reservation back, so that a cancelled or failed booking frees its place.

#### Acceptance Criteria

1. WHEN the internal service POSTs `{ AppointmentId }` to `/internal/slots/{id}/release` and a reservation exists for `(slot, appointment)`, THEN the system SHALL delete that reservation row and decrease `ReservedCount` by one in one database transaction, and return `204 No Content`.
2. WHERE no reservation exists for `(slot, appointment)`, the system SHALL return `204 No Content` and change nothing, so a retried or compensating release is harmless.
3. WHEN a release is made, THEN the system SHALL NOT check whether the slot has started or whether the branch or service type is active. Capacity must always be returnable.
4. IF no slot exists with the given `id`, THEN the system SHALL return `404 Not Found`. IF `AppointmentId` is missing or empty, THEN `400 Bad Request`.
5. Criterion 7 of Requirement 3 applies: the decrement SHALL apply only while `ReservedCount > 0`.

### Requirement 5: Create an appointment (FR-BKG-001)

**User Story:** As a customer, I want to book a slot, so that a place is held for me at that time.

#### Acceptance Criteria

1. WHEN a caller with the `Customer` role POSTs `{ SlotId }` to `/api/appointments` with a valid `Idempotency-Key` header, THEN the system SHALL read the slot from Availability, reserve it for a new appointment, save the appointment with status `Booked`, and return `201 Created` with the appointment and a `Location` header pointing at `/api/appointments/{id}`.
2. WHEN an appointment is created, THEN it SHALL store its id (a version 7 GUID generated by Booking before it reserves), the customer's id (the `sub` claim of the token), the slot id, the branch id, the service type id, the slot's `StartUtc` and `EndUtc` as read from Availability, status `Booked`, and `CreatedAtUtc`. The customer SHALL supply only the slot id; everything else comes from the token or from Availability.
3. IF `SlotId` is missing or the empty GUID, THEN the system SHALL return `400 Bad Request` listing every failed rule.
4. IF Availability has no slot with that id, THEN the system SHALL return `404 Not Found`.
5. IF the customer already holds a `Booked` appointment whose interval overlaps the slot's `[StartUtc, EndUtc)`, at any branch and for any service type, THEN the system SHALL return `409 Conflict` with "You already have an appointment at that time." and reserve nothing (BRD section 9: "cannot double-book same service/time").
6. IF Availability refuses the reservation with `409 Conflict` (slot started, branch or service type inactive, slot full), THEN the system SHALL return `409 Conflict` carrying Availability's reason, and save no appointment.
7. WHERE the reservation succeeded but saving the appointment fails, the system SHALL ask Availability to release the reservation for that appointment id (best effort), and then return `500` through the exception middleware. The release is safe to repeat because of Requirement 4.2. The idempotency record is handled as in Requirement 6.7.
8. IF Availability cannot be reached, times out, answers `5xx` after its retries, or the circuit to it is open, THEN the system SHALL return `503 Service Unavailable` and save no appointment.
9. WHEN the appointment is saved, THEN the system SHALL, inside the same database transaction, first take a lock that is exclusive per customer (a PostgreSQL advisory transaction lock derived from the customer id), then re-run the overlap check of criterion 5, and only then insert the appointment. IF the re-check finds an overlap, THEN the system SHALL save nothing, release the reservation it made, and return `409 Conflict` as in criterion 5. Hence two requests by the same customer with different idempotency keys for overlapping slots can never both end `Booked`. The lock SHALL NOT be held while Availability is being called, and requests by different customers SHALL NOT wait for each other.
10. IF the caller is not a customer, THEN `403`; IF there is no valid token, THEN `401`.
11. The request SHALL NOT publish an event or write an outbox message. `AppointmentBooked` (BRD section 14) arrives with the messaging work.

### Requirement 6: Idempotent creation (FR-BKG-002)

**User Story:** As a customer whose connection dropped, I want to retry the same booking request and get the original result, so that I never end up with two appointments or an unexplained failure.

#### Acceptance Criteria

1. IF the `Idempotency-Key` header is missing, blank, or longer than 128 characters, THEN the system SHALL return `400 Bad Request` and do nothing else.
2. WHEN a valid request arrives, THEN the system SHALL record the key for `(UserId, Key)`, together with a hash of the request body, the id the appointment will have, and an expiry 24 hours after the request, before it calls Availability. Keys of different customers SHALL NOT collide.
3. WHEN a request arrives with a key already recorded for that customer, whose record has not expired, whose request hash is equal, and whose outcome is stored, THEN the system SHALL return the stored status code and body without calling Availability and without changing anything.
4. IF the key is already recorded, not expired, and the request hash is different, THEN the system SHALL return `422 Unprocessable Entity`, whether or not the first request has finished.
5. IF the key is already recorded, not expired, the hash is equal and the first request is still in progress, THEN the system SHALL return `409 Conflict` ("A request with this Idempotency-Key is still being processed."). Two simultaneous first requests with the same key SHALL be decided by a unique constraint in the database, so exactly one proceeds.
6. WHEN a request finishes with success (`201`) or a business outcome in the `4xx` range (`404`, `409`), THEN the system SHALL store its status code and response body with the record, in the same transaction as the appointment where there is one.
7. WHEN a request ends in `5xx` (including the `503` of Requirement 5.8), THEN the system SHALL NOT store the outcome, and SHALL treat the key's record as follows:
   - IF no reservation had been attempted (the failure came before the reserve call, for example while reading the slot), THEN the record SHALL be removed so the client can retry with the same key at once;
   - IF a reserve call had been issued and its result is not known to be a refusal (a timeout, an outage, a lost response, or a failure while saving the appointment), THEN the record SHALL be left `InProgress`, keeping its appointment id. A retry with the same key before the lease ends gets `409` (criterion 5); a retry after it takes the record over and reuses that appointment id (criterion 8). Only success and `4xx` business outcomes are ever stored as outcomes.
8. WHERE an in-progress record is older than its lease (2 minutes, longer than the whole resilience budget of Requirement 9) because the process died, the system SHALL treat it as abandoned and let a retry take it over **reusing the same appointment id**, so that a reservation Availability already made for that id is neither leaked nor made twice. This applies both to a process that died and to a request that ended in `5xx` after a reserve call (criterion 7).
9. WHERE a stored record has expired (24 hours), the system SHALL let a request with that key start afresh by taking over the record.
10. A request rejected before the key is recorded (Requirement 6.1, `400` for `SlotId`, `401`, `403`) SHALL NOT create a record.

### Requirement 7: Get an appointment (FR-BKG-001, no FR-ID of its own)

**User Story:** As a customer, I want to open the appointment I booked, and as staff I want to look one up for a customer, so that the details can be confirmed.

#### Acceptance Criteria

1. WHEN the customer who owns an appointment GETs `/api/appointments/{id}`, THEN the system SHALL return `200 OK` with `{ Id, CustomerId, SlotId, BranchId, ServiceTypeId, StartUtc, EndUtc, Status, CreatedAtUtc, UpdatedAtUtc, CancelledAtUtc }`, with `Status` written as `"Booked"` or `"Cancelled"`.
2. WHEN a caller with the `Staff` or `Admin` role GETs any appointment, THEN the system SHALL return it, whoever owns it.
3. IF the appointment does not exist, OR the caller is a customer who does not own it, THEN the system SHALL return `404 Not Found` with the same body in both cases, so that existence is not disclosed.
4. IF there is no valid token, THEN `401`.

### Requirement 8: Cancel an appointment (FR-BKG-005)

**User Story:** As a customer, I want to cancel my appointment before it starts, so that the place is free for someone else.

#### Acceptance Criteria

1. WHEN the customer who owns a `Booked` appointment, or an `Admin`, POSTs to `/api/appointments/{id}/cancel` before the appointment's `StartUtc`, THEN the system SHALL set its status to `Cancelled`, set `CancelledAtUtc` and `UpdatedAtUtc`, save it, ask Availability to release its slot reservation, and return `204 No Content`.
2. IF the appointment does not exist, OR the caller is neither its owner nor an `Admin`, THEN the system SHALL return `404 Not Found` with the same body in both cases. A `Staff` caller who is not the owner gets `404`.
3. IF the appointment is `Booked` and `StartUtc` is at or before the current time, THEN the system SHALL return `409 Conflict` ("An appointment can only be cancelled before it starts.") and change nothing.
4. WHERE the appointment is already `Cancelled`, the system SHALL call release again and return `204 No Content`, whatever the time. This lets a client whose first cancel returned `503` retry until the capacity has really been given back.
5. IF the release call fails because Availability cannot be reached (Requirement 5.8), THEN the system SHALL return `503 Service Unavailable`. The appointment stays `Cancelled`; the capacity is given back by the repeat cancel of criterion 4.
6. Cancelling SHALL NOT require an `Idempotency-Key`; it is idempotent by its nature (criterion 4).
7. IF there is no valid token, THEN `401`. The cancel request SHALL NOT publish an event.

### Requirement 9: Calling Availability safely (BRD 11.4)

**User Story:** As an operator, I want a slow or failing Availability service to produce a quick, clean `503` from Booking rather than hanging requests, so that one outage does not take both services down.

#### Acceptance Criteria

1. WHEN Booking calls Availability, THEN it SHALL send the `X-API-Key` header with the key from `InternalApi:Key`, to the base address in `Services:Availability:BaseUrl`.
2. WHEN a call fails with a transient error (network failure, timeout, `5xx`, `408`, `429`), THEN Booking SHALL retry it with exponential backoff and jitter, up to a bounded number of attempts and a bounded total time. Retrying a reserve or release is safe because of Requirements 3.3 and 4.2.
3. WHEN calls to Availability fail repeatedly, THEN a circuit breaker SHALL open and further calls SHALL fail fast with `503` until it half-opens.
4. WHEN an attempt exceeds its timeout, THEN it SHALL count as a transient failure.
5. IF Availability answers `401`/`403` (the keys differ), THEN Booking SHALL treat it as a configuration fault: log an error without the key and return `503`.
6. WHEN Booking starts, THEN it SHALL fail with an explanatory message IF `Services:Availability:BaseUrl` is not an absolute `http` or `https` URL, or `InternalApi:Key` is missing, blank or shorter than 32 bytes.

### Requirement 10: The Booking service exists (no FR-ID)

**User Story:** As a developer, I want the Booking service to look and behave like Auth and Availability, so that every service is operated the same way.

#### Acceptance Criteria

1. The system SHALL provide `Booking.Api`, `Booking.Application`, `Booking.Domain` and `Booking.Infrastructure` under `src/Services/Booking/`, wired as the Availability service is: `AddApplication()`, `AddInfrastructure(configuration)`, `AddControllers()`, `AddOpenApiWithBearerAuth()`, `AddJwtAuthentication(configuration)`, `AddAuthorizationWithRoles()`, with `ExceptionMiddleware` and `LoggingMiddleware` ahead of authentication, `/healthz`, and Scalar only in development.
2. WHEN Booking validates a token, THEN it SHALL use the same `Jwt:Issuer`, `Jwt:Audience` and `Jwt:SecretKey` as Auth.
3. WHEN Booking starts, THEN it SHALL fail with an explanatory message IF `ConnectionStrings:DefaultConnection` or `Jwt:SecretKey` is missing. Local development uses the database `smart_appointment_booking`, configured in `appsettings.Development.json`.
4. A handler that needs the current time SHALL take it from `TimeProvider`.
5. The Booking schema SHALL be created by an EF Core migration, and the project SHALL have an `IDesignTimeDbContextFactory` so that `dotnet ef migrations add` works without a database.
6. A failed `Result` with status `422` or `503` SHALL reach the caller with that status, not as a `500`.

## Discrepancies with the BRD

| BRD | This spec | Decision |
|---|---|---|
| Section 13 lists `POST /internal/slots/{slotId}/reserve` and `release`. | Adds `GET /internal/slots/{id}`, and both writes take `{ AppointmentId }` in the body. | Booking needs the slot's branch, service type and times to store on the appointment without trusting the client, and a reservation keyed by appointment is what makes retries idempotent (Req 3.3, 4.2). |
| Section 7.1 steps 6-9: the appointment and an outbox message are created in one transaction, an event is published, Notification and Reporting react. | None of the event flow is built (Req 5.11, 8.7). | The outbox, RabbitMQ and the consumers are Week 2 messaging work. The appointment is saved in a transaction that is ready to take an outbox row later. |
| Section 7.2 order: mark Cancelled, then release. | Kept (Req 8.1), with a repeat cancel that re-releases (Req 8.4). | Confirmed by the project owner; see the design's decisions taken. |
| Section 11.1 `IdempotencyRecords` columns. | Adds `AppointmentId` and an in-progress state. | Needed for in-flight detection (Req 6.5) and to survive a crash without leaking a reservation (Req 6.8). |
| Section 9: "cannot double-book same service/time". | Booking refuses any overlapping `Booked` appointment of the same customer, at any branch and for any service (Req 5.5). | A person cannot be in two places at once. A stricter rule is easier to relax than a looser one is to tighten. |
| Section 10 asks for correlation IDs and Serilog. | Not added. | Observability is separate work. |

## Out of scope

- **My appointments (`FR-BKG-003`), the staff branch view (`FR-BKG-004`), complete and no-show (`FR-BKG-006`).** Each is its own spec; the statuses are `Booked` and `Cancelled` until then.
- **Outbox, RabbitMQ, domain events and consumers.** Deferred to the messaging work. Until then nothing observes a booking or a cancellation, and Reporting and Notification do not exist.
- **API gateway routing and Docker.** Services are called directly on their own ports.
- **Reconciliation of stranded reservations.** A reservation is stranded only if its client never retries with the same key (Requirement 6.7), and a cancelled appointment whose client never retried the release (Requirement 8.5) likewise leaves capacity held. A periodic reconciliation job comes with the outbox.
- **Cleaning up expired idempotency records.** They are only taken over when the same key returns. A purge job is future work.
- **Rate limiting, correlation ids, and TLS between services.** The internal call uses whatever scheme `Services:Availability:BaseUrl` names.
- **Rescheduling an appointment.** Cancel and book again.
