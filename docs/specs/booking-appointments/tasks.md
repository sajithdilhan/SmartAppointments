# Booking appointments — Tasks

> Each task leaves the solution building and `dotnet test SmartAppointments.slnx` green. Tasks are grouped into chunks (A-E) that can be handed to an implementer one at a time; do them in order. The design's *Decisions taken* section is final.

## Chunk A — Shared API-key authentication

- [x] 1. API-key scheme in `BuildingBlocks.Web`
  - `Constants.InternalServicePolicy`; `ApiKeyAuthenticationOptions`, `ApiKeyAuthenticationHandler` (constant-time compare via SHA-256 digests and `FixedTimeEquals`, principal with no role claims), `AddApiKeyAuthentication(configuration)` (fail-fast on a missing, blank or under-32-byte `InternalApi:Key`; default schemes untouched; policy accepts only the API-key scheme)
  - `ApiKeyAuthenticationTests` in `tests/BuildingBlocks.Tests`: missing/wrong/equal/different-length key, principal has no roles, startup failures, policy scheme
  - _Requirements: 1.1–1.8_

- [x] 2. Map `422` and `503` in `ToActionResult`
  - `422` → `UnprocessableEntity(error)`, `503` → `StatusCode(503, error)`; additions to `ErrorResultExtensionsTests`
  - _Requirements: 10.6_

## Chunk B — Availability: get, reserve, release

- [x] 3. `SlotReservation` and persistence
  - `SlotReservation` entity and `SlotReservationTests`; fix the `Slot.ReservedCount` comment; `SlotReservations` table (composite key, FK to `Slots`, `Restrict`); the two `Slots` check constraints
  - `ISlotRepository.GetByIdAsync`; `ISlotReservationRepository` with `ReserveOutcome` and `ReleaseOutcome`; `SlotReservationRepository` (insert `ON CONFLICT DO NOTHING` then conditional `ExecuteUpdateAsync` in one transaction; delete then conditional decrement in one transaction); register it in `AddInfrastructure`
  - Migration `AddSlotReservations` (`dotnet ef migrations add AddSlotReservations --project src/Services/Availability/Availability.Infrastructure --startup-project src/Services/Availability/Availability.Api`)
  - _Requirements: 3.2, 3.3, 3.7, 4.1, 4.2, 4.5_

- [x] 4. Get, reserve and release handlers
  - `GetInternalSlotQuery`, `ReserveSlotCommand`, `ReleaseSlotCommand`, handlers, validators (registered in `AddApplication`), `InternalSlotResponse`, `SlotReservationRequest`, new `SlotErrors` (started, full)
  - Handler and validator tests (all 404/409 paths, the repeat reserve that skips state checks, release without state checks, `TimeProvider` pinned with `FixedTimeProvider`)
  - _Requirements: 2.1–2.3, 3.1, 3.3–3.6, 3.8, 4.1–4.4_

- [x] 5. `InternalSlotsController` and wiring
  - `GET /internal/slots/{id}`, `POST .../reserve`, `POST .../release` under `InternalServicePolicy`; `AddApiKeyAuthentication` in `Availability.Api/Program.cs`; `InternalApi:Key` empty in `appsettings.json`, a throwaway 32+ character value in `appsettings.Development.json`
  - `InternalSlotsControllerTests` (status mapping, class-level policy by reflection); requests added to `Availability.Api.http` using `X-API-Key`
  - _Requirements: 1.1–1.8, 2, 3, 4_

## Chunk C — Booking service: scaffold, domain, persistence

- [x] 6. Scaffold the Booking projects
  - `Booking.Api`, `.Application`, `.Domain`, `.Infrastructure` under `src/Services/Booking/`, `tests/Booking.Tests` (with a copy of `FixedTimeProvider`), all added to `SmartAppointments.slnx`; references and packages mirror Availability (`Npgsql.EntityFrameworkCore.PostgreSQL`, `AspNetCore.HealthChecks.NpgSql`, MediatR, FluentValidation, `Scalar.AspNetCore`, `BuildingBlocks.Web`) plus `Microsoft.Extensions.Http.Resilience` in Infrastructure
  - `Program.cs` in the wiring order of Availability with `JsonStringEnumConverter`; `AddApplication` registers `TimeProvider.System`, MediatR and validators; `AddInfrastructure` fails fast on a missing connection string; `/healthz`; Scalar in development; `launchSettings.json` with its own ports
  - `appsettings.json` (empty `ConnectionStrings:DefaultConnection`, `Jwt` issuer/audience, empty `Jwt:SecretKey`, empty `InternalApi:Key`, empty `Services:Availability:BaseUrl`); `appsettings.Development.json` (database `smart_appointment_booking`, Auth's JWT key, the dev API key equal to Availability's, `http://localhost:5202` (verified against Availability's launchSettings))
  - A trivial test so `Booking.Tests` runs
  - _Requirements: 10.1–10.4_

- [x] 7. Domain: `Appointment` and `IdempotencyRecord`
  - `AppointmentStatus`, `Appointment.Book`/`Cancel`, `IdempotencyState`, `IdempotencyRecord.Claim`/`Complete`/`IsExpired` (2-minute lease, 24-hour window)
  - `AppointmentTests`, `IdempotencyRecordTests`
  - _Requirements: 5.2, 6.2, 6.8, 6.9, 8.1_

- [x] 8. Persistence and migration
  - `ApplicationDbContext` (`Appointments`, `IdempotencyRecords` as in the design, including the unique `(UserId, IdempotencyKey)` index and `jsonb` body), `UniqueViolation` helper, `DesignTimeDbContextFactory` with the API's user-secrets id, migration `InitialCreate`
  - `IAppointmentRepository`, `IIdempotencyRepository` (and `ClaimResult`, `AddAppointmentOutcome`) in Application; `AppointmentRepository` (including `TryAddBookedAsync`: one transaction taking `pg_advisory_xact_lock(hashtextextended(customerId::text, 0))`, re-running the overlap query, inserting the appointment and completing the idempotency record) and `IdempotencyRepository` (claim, replay detection, expired takeover reusing an in-progress record's appointment id, complete, remove; `RemoveAsync` only deletes a record still in progress and owned by the caller) in Infrastructure; registered in `AddInfrastructure`
  - Nothing here is exercised by a unit test; it is checked by hand in task 13
  - _Requirements: 5.9, 6.2, 6.5, 6.8, 6.9, 10.5_

## Chunk D — Booking: Availability client, create, get, cancel

- [ ] 9. Availability client
  - `SlotInfo`, `IAvailabilityClient`; `AvailabilityClient` with the status mapping of the design; `AddHttpClient<...>().AddStandardResilienceHandler(...)` with the 5 s attempt and 20 s total timeouts, the comment that retrying POST is safe only because reserve and release are idempotent; fail-fast validation of `Services:Availability:BaseUrl` and `InternalApi:Key`
  - `AvailabilityClientTests` with a fake `HttpMessageHandler`: statuses, the `X-API-Key` header, network failure, timeout and `401` → `503`
  - _Requirements: 9.1–9.6_

- [ ] 10. Create appointment
  - `CreateAppointmentRequest`, `AppointmentResponse`, `CreateAppointmentCommand`, validator, `CreateAppointmentCommandHandler` (claim, slot read, overlap pre-check, reserve, `TryAddBookedAsync`, outcome storage, compensation and claim removal)
  - The handler tracks `reserveAttempted`: a failure before the reserve call removes the claim; a `503`, a lost response or a failed save after a reserve call leaves the claim in progress (after a best-effort release) and stores nothing; an `Overlap` from `TryAddBookedAsync` releases the reservation and returns a stored `409`, or `503` with the claim left in progress if that release fails
  - `CreateAppointmentCommandValidatorTests` (key at 128 and 129 characters, blank, missing) and `CreateAppointmentCommandHandlerTests` as listed in the design, including: claim removed on a pre-reserve `503`; claim not removed and nothing stored on a reserve-time `503`; a retry whose claim carries an earlier appointment id reserves with that id; `Overlap` path releases and answers `409` (the advisory lock itself is SQL and is verified in task 13)
  - _Requirements: 5.1–5.9, 5.11, 6.1–6.10_

- [ ] 11. Get and cancel
  - `GetAppointmentQuery`/handler and `CancelAppointmentCommand`/handler with ownership and role rules, the identical `404`, the repeat cancel that re-releases, the `503` on release failure
  - `GetAppointmentHandlerTests`, `CancelAppointmentHandlerTests`
  - _Requirements: 7.1–7.3, 8.1–8.6_

- [ ] 12. `AppointmentsController`
  - `POST /api/appointments` (`CustomerPolicy`, `Idempotency-Key` header, `CreatedAtAction`), `GET /api/appointments/{id}` and `POST /api/appointments/{id}/cancel` (`AllowedOriginsPolicy`); caller id from `sub`, role from `role`; failures through `this.ToActionResult`
  - `AppointmentsControllerTests`: `201` with `Location`, status mapping including `422` and `503`, missing `sub` → `401`, policies by reflection
  - `Booking.Api.http` with the requests
  - _Requirements: 5.1, 5.10, 6.3, 7.4, 8.7_

## Chunk E — End-to-end check and documentation

- [ ] 13. Verify against a local PostgreSQL
  - Create the databases (`smart_appointment_availability` exists; `smart_appointment_booking` is new), apply both services' migrations, run Auth, Availability and Booking, register an admin and two customers
  - Availability: generate slots with capacity 1; with the API key `GET /internal/slots/{id}` returns the slot; reserve twice with the same appointment id leaves `ReservedCount` at 1; a second appointment id gets `409` "full"; release twice leaves it at 0; a customer JWT on `/internal/...` gets `401`; no key, wrong key `401`; the API key on `/api/slots/available` gets `401`; reserve on an inactive branch, a started slot `409`, and release on the same `204`
  - Concurrency: fire a few dozen parallel reserves with different appointment ids at the capacity-1 slot; exactly one `204`, the rest `409`, `ReservedCount` is 1
  - Booking: create without `Idempotency-Key` → `400`; with a key → `201` with `Location`; the same key and body again → the same `201`, no second appointment, `ReservedCount` unchanged; the same key with another slot → `422`; a second slot overlapping in time with a new key → `409`; booking the full slot as a second customer → `409` with Availability's reason; unknown slot → `404`
  - Get: owner `200`; another customer `404`; staff `200`; unknown id `404` with the same body as the other customer's
  - Cancel: owner `204` and the slot's `ReservedCount` returns to 0; repeat cancel `204`; cancel by another customer `404`; cancel after start `409`
  - Overlap race: two parallel creates by one customer with different keys for overlapping slots give one `201` and one `409`, and the loser's slot returns to its prior `ReservedCount`
  - Outage: stop Availability, then create → `503` and, because the reserve call was attempted, the `IdempotencyRecords` row for that key stays `InProgress` with its `AppointmentId`; after the 2-minute lease, retrying with the same key succeeds and reuses that appointment id with `ReservedCount` incremented once; a create that fails while reading the slot leaves no record; cancel an appointment → `503`, status `Cancelled`; restart Availability and repeat the cancel → `204`, capacity released; after repeated failures the circuit opens and calls fail fast
  - Record the results in this file's header, as the other specs do
  - _Requirements: 1–10_

- [ ] 14. Documentation
  - `CLAUDE.md` project status: Availability now has internal reserve/release/get under an API key; add the Booking service paragraph (create/get/cancel, idempotency, resilient client, `smart_appointment_booking`); BuildingBlocks.Web now also holds the API-key scheme and the `InternalApi:Key` setting (and `Services:Availability:BaseUrl` for Booking); mention that the outbox and events are still pending
  - `docs/specs/README.md` row to "Implemented"; `docs/specs/availability-slots/requirements.md` out-of-scope note for `FR-AVL-005` gets a pointer to this spec; the Availability line in `CLAUDE.md` no longer says reserve/release is unbuilt
  - _Requirements: 1–10_
