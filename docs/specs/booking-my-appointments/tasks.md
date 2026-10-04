# My appointments — Tasks

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

- [ ] 5. Documentation and request samples
  - `CLAUDE.md`: extend the Booking bullet with `GET /api/appointments/my` (customers; `status`, `when`, `page`, `pageSize`; `{items, page, pageSize, totalCount}`; reuses `IX_Appointments_CustomerId_StartUtc`) and add the spec to the Booking spec pointer
  - `docs/specs/README.md`: add a "Booking my appointments" row (FR-BKG-003, links to the three documents) and change the "not yet specced" line to drop `FR-BKG-003`
  - `src/Services/Booking/Booking.Api/Booking.Api.http`: samples for the default list, `status=Booked&when=upcoming`, `when=past&page=2&pageSize=5`, and an invalid one (`status=Completed&page=0`)
  - `src/ApiGateway/SmartAppointments.Gateway/SmartAppointments.Gateway.http`: the same list sample through the gateway with `CustomerToken`
  - _Requirements: 4.4, 4.5_

- [ ] 6. Verify against a local PostgreSQL
  - Run Auth, Availability, Booking and the gateway; register two customers and a staff and an admin user; seed one customer (SQL or the create endpoint) with more than one page of mixed appointments: several `Booked` and `Cancelled`, past and future, two with an equal `StartUtc`, one starting exactly at the clock's "now" boundary
  - Filters: no filter lists both statuses; `status=booked` and `status=CANCELLED` work; `when=upcoming` is ascending and `when=past` and none descending; both filters together; ties ordered by `Id` ascending; `totalCount` counts all matches
  - Paging: pages 1, 2, 3 never repeat or skip an item; `page` beyond the last gives `200`, empty `items` and the right `totalCount`; `pageSize=100` works; defaults echo `page=1`, `pageSize=20`; a customer with nothing gets `200` and `totalCount` 0
  - Invalid input: `status=Completed`, `status=NoShow`, `status=` (empty), `when=now`, `page=0`, `page=abc`, `page=` (empty), `pageSize=101`, `pageSize=0` each give `400` with the shared `{status, detail}` body naming the parameter, and `status=x&page=0&pageSize=500` gives one `400` listing all three. This is the check of the empty-value binding (`DisplayFormat(ConvertEmptyStringToNull = false)`); if an empty value is treated as omitted, switch the action to read the four values from `Request.Query` and record it in the design
  - Access: `?customerId=<other customer>` changes nothing; a Staff and an Admin token give `403`; no token gives `401`; `GET /api/appointments/{id}` still works; through the gateway the endpoint is not rate limited
  - `EXPLAIN` for the three query shapes (upcoming, past, unfiltered; with and without `status`): the plan uses `IX_Appointments_CustomerId_StartUtc`, with an incremental sort for the `Id` tie-break rather than a full table sort; no migration is needed unless this fails
  - Record the results in this file's header, as the other specs do
  - _Requirements: 1–4_
