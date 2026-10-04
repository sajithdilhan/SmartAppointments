# My appointments — Tasks

> **Manual verification (task 6).** Date 2026-10-04. Run on a throwaway integration branch (the three feature branches merged on `unified-error-shape`, identical in `src` and `tests` to `auth-refresh-tokens`), `docker compose up --build -d` (PostgreSQL on 5433), admin seeded through `SEED_ADMIN_*` in the gitignored `.env`; requests with curl and Python through the gateway on 5290 (Auth directly on the compose network for the parallel refresh runs, to stay clear of the 5/min login limit). `dotnet test SmartAppointments.slnx` green on the merge: BuildingBlocks 73, Gateway 138, Auth 171, Booking 170, Availability 303. No bug found; no code changed.
> - Data: customer A with 52 appointments (36 `Booked`, 16 `Cancelled`, 28 upcoming, 24 past, two with an equal `StartUtc`), customer B with one, customer C with none; one Staff (promoted in SQL) and the seeded Admin. Inserted by SQL into `smart_appointment_booking`.
> - Filters: default lists both statuses with `page=1`, `pageSize=20`, `totalCount` 52, descending; `status=booked`, `Booked` and `CANCELLED` work (36, 36, 16); `when=upcoming` ascending and all future (28), `when=past` and unfiltered descending (24); upcoming plus past is 52; both filters together (8); ties ordered by `Id` ascending.
> - Paging: pages of 7 for no filter, upcoming, past and `status=booked` never repeat or skip an item and add up to `totalCount`; `page=99` is `200` with empty `items` and `totalCount` 52; `pageSize=100` works; customer C gets `200` with `totalCount` 0.
> - Invalid input: `status=Completed`, `NoShow`, empty; `when=now`, empty; `page=0`, `abc`, empty; `pageSize=101`, `0`, empty each give `400` with the `{status, detail}` body naming the parameter; `status=x&page=0&pageSize=500` gives one `400` listing all three. Empty values are not treated as omitted, so the `DisplayFormat(ConvertEmptyStringToNull = false)` binding works and the `Request.Query` fallback was not needed.
> - Access: `?customerId=<B>` changes nothing; Staff and Admin `403`; no token `401`; `GET /api/appointments/{id}` still `200`; 40 calls in a minute through the gateway, no `429`.
> - `EXPLAIN (ANALYZE)` of the four shapes (upcoming, upcoming with status, past, unfiltered) on 50 052 rows after `ANALYZE`: `Index Scan` (`Backward` for descending) on `IX_Appointments_CustomerId_StartUtc` under an `Incremental Sort` on the `Id` tie-break, no full sort; no migration needed. The statements were written by hand in the shape of the repository's query (EF's generated SQL was not captured).
> - Not verified live: a row starting exactly at the clock's "now" (it cannot be hit with a real clock; covered by the handler unit tests).

> Each task leaves the solution building and `dotnet test SmartAppointments.slnx` green. Do them in order. The design's *Open questions — resolved* section is final. There is no migration: the existing `IX_Appointments_CustomerId_StartUtc` serves the query.

## Chunk A — Application

- [x] 1. Parameters, models and validator
  - `AppointmentTimeFilter` (`Upcoming`, `Past`) and `PagedResponse<T>(Items, Page, PageSize, TotalCount)` in `Booking.Application/Models`
  - `MyAppointmentsParameters` helper (`TryParseStatus`, `TryParseWhen`, `TryParsePage`, `TryParsePageSize`; explicit case-insensitive name match, digits-only invariant-culture integers, defaults 1 and 20, max 100)
  - `ListMyAppointmentsQuery(CallerId, Status, When, Page, PageSize)` (raw strings) and `ListMyAppointmentsQueryValidator` with the four rules and messages of the design; register it in `AddApplication()`
  - `MyAppointmentsParametersTests` and `ListMyAppointmentsQueryValidatorTests` (each rule alone, several failures at once)
  - _Requirements: 2.1, 2.2, 2.4, 3.3, 3.4, 3.7_

- [x] 2. Repository abstraction and implementation
  - `AppointmentPage(Items, TotalCount)` and `IAppointmentRepository.ListForCustomerAsync(customerId, status, when, nowUtc, page, pageSize, ct)`
  - `AppointmentRepository.ListForCustomerAsync`: no tracking, always filter on `CustomerId`, optional status and time filters (`>= now` upcoming, `< now` past), `CountAsync`, `long` skip with the `skip >= total` early return, order by `StartUtc` asc (upcoming) or desc then `Id` asc, `Skip`/`Take`. No migration and no `OnModelCreating` change
  - Any existing test double implementing `IAppointmentRepository` is updated so the solution still builds
  - _Requirements: 1.2, 2.1–2.3, 3.1, 3.2, 3.5, 3.6_

- [x] 3. Handler
  - `ListMyAppointmentsHandler(IAppointmentRepository, IValidator<ListMyAppointmentsQuery>, TimeProvider)`: validate (one `Error(400, "Invalid request data. Errors: ...")` joined with `","`, repository untouched on failure), resolve parameters, read `TimeProvider` once, call the repository, map with `AppointmentResponse.From`, return `PagedResponse<AppointmentResponse>`
  - `ListMyAppointmentsHandlerTests` per the design (defaults, parsed filters reach the repository, caller id, clock read once, mapping, empty and beyond-the-end pages, invalid input never calls the repository)
  - _Requirements: 1.1, 1.4–1.7, 2.1–2.5, 3.3–3.5, 3.7_

## Chunk B — API

- [x] 4. `GetMine` action
  - `GET /api/appointments/my` on `AppointmentsController`, above `GetById`: `CustomerPolicy`, `string?` parameters `status`, `when`, `page`, `pageSize` each with `[FromQuery, DisplayFormat(ConvertEmptyStringToNull = false)]`, no `customerId`; `TryGetCaller` then `Unauthorised()` on a bad `sub`; failures through `this.ToActionResult`; success `Ok(result.Value)`; XML summary
  - Additions to `AppointmentsControllerTests`: `200` with the page, the query carries the `sub` and the four raw strings, `400` mapping, missing and non-GUID `sub` → `401` with no `Send`, reflection for `[HttpGet("my")]` and `CustomerPolicy`, `GetById` still `[HttpGet("{id:guid}")]`, no parameter named `customerId`
  - _Requirements: 1.1–1.3, 2.4, 4.1–4.3, 4.5_

## Chunk C — Documentation and end-to-end check

- [x] 5. Documentation and request samples
  - `CLAUDE.md`: extend the Booking bullet with `GET /api/appointments/my` (customers; `status`, `when`, `page`, `pageSize`; `{items, page, pageSize, totalCount}`; reuses `IX_Appointments_CustomerId_StartUtc`) and add the spec to the Booking spec pointer
  - `docs/specs/README.md`: add a "Booking my appointments" row (FR-BKG-003, links to the three documents) and change the "not yet specced" line to drop `FR-BKG-003`
  - `src/Services/Booking/Booking.Api/Booking.Api.http`: samples for the default list, `status=Booked&when=upcoming`, `when=past&page=2&pageSize=5`, and an invalid one (`status=Completed&page=0`)
  - `src/ApiGateway/SmartAppointments.Gateway/SmartAppointments.Gateway.http`: the same list sample through the gateway with `CustomerToken`
  - _Requirements: 4.4, 4.5_

- [x] 6. Verify against a local PostgreSQL
  - Run Auth, Availability, Booking and the gateway; register two customers and a staff and an admin user; seed one customer (SQL or the create endpoint) with more than one page of mixed appointments: several `Booked` and `Cancelled`, past and future, two with an equal `StartUtc`, one starting exactly at the clock's "now" boundary
  - Filters: no filter lists both statuses; `status=booked` and `status=CANCELLED` work; `when=upcoming` is ascending and `when=past` and none descending; both filters together; ties ordered by `Id` ascending; `totalCount` counts all matches
  - Paging: pages 1, 2, 3 never repeat or skip an item; `page` beyond the last gives `200`, empty `items` and the right `totalCount`; `pageSize=100` works; defaults echo `page=1`, `pageSize=20`; a customer with nothing gets `200` and `totalCount` 0
  - Invalid input: `status=Completed`, `status=NoShow`, `status=` (empty), `when=now`, `page=0`, `page=abc`, `page=` (empty), `pageSize=101`, `pageSize=0` each give `400` with the shared `{status, detail}` body naming the parameter, and `status=x&page=0&pageSize=500` gives one `400` listing all three. This is the check of the empty-value binding (`DisplayFormat(ConvertEmptyStringToNull = false)`); if an empty value is treated as omitted, switch the action to read the four values from `Request.Query` and record it in the design
  - Access: `?customerId=<other customer>` changes nothing; a Staff and an Admin token give `403`; no token gives `401`; `GET /api/appointments/{id}` still works; through the gateway the endpoint is not rate limited
  - `EXPLAIN` for the three query shapes (upcoming, past, unfiltered; with and without `status`): the plan uses `IX_Appointments_CustomerId_StartUtc`, with an incremental sort for the `Id` tie-break rather than a full table sort; no migration is needed unless this fails
  - Record the results in this file's header, as the other specs do
  - _Requirements: 1–4_
