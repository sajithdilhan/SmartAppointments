# Availability service types — Tasks

> Each task leaves the solution building and `dotnet test SmartAppointments.slnx` green.
>
> All five tasks are done. The migration was applied to a local PostgreSQL and every endpoint was exercised with admin and customer tokens: create `201` with a normalised code and a blank description stored as null; a duplicate in another case `409`; invalid input `400` listing every rule; customer create `403`; anonymous list `401`; update `200` ignoring a `code` in the body; unknown id `404`; deactivate and activate `204` twice; an inactive service type `404` to a customer and `200` to an admin, and listed only for an admin asking for inactive ones.

- [x] 1. `ServiceType` aggregate
  - `Create`, `NormaliseCode`, `UpdateDetails`, `Activate`, `Deactivate`
  - `ServiceTypeTests`
  - _Requirements: 1.2, 1.4, 2.1, 2.6, 3.1, 3.2_

- [x] 2. Persistence
  - `ServiceTypes` configuration and unique index `IX_ServiceTypes_Code` in `ApplicationDbContext`
  - `IServiceTypeRepository`, `DuplicateServiceTypeCodeException`, `ServiceTypeRepository`
  - Both repositories translate a unique violation only when it is on their own code index
  - `CreateServiceTypes` migration
  - _Requirements: 1.5, 4.1, 4.5_

- [x] 3. Commands, queries, validators and handlers
  - Create, update and set-active commands; list and get-by-id queries; both validators, registered in `AddApplication`
  - Handler tests: stage then commit; duplicate pre-check and commit-time duplicate → 409; normalised code looked up; invalid input → 400 without writing; unknown id → 404; repeat activation writes nothing; non-admin never sees an inactive service type
  - Validator tests at every boundary
  - _Requirements: 1.1–1.5, 2.1–2.6, 3.1–3.3, 4.1–4.6_

- [x] 4. `ServicesController`
  - The six actions with their policies; `CreatedAtAction(nameof(GetServiceType), ...)`
  - Controller tests, including each action's policy
  - Requests for every endpoint in `Availability.Api.http`
  - _Requirements: 1.1, 2.1, 3.1, 4.1–4.4, 4.7_

- [x] 5. Verify against a local PostgreSQL
  - Apply the migration; exercise every endpoint with admin and customer tokens
  - _Requirements: 1.1–4.7_
