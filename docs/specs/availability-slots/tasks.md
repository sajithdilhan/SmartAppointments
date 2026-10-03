# Availability slots — Tasks

> Each task leaves the solution building and `dotnet test SmartAppointments.slnx` green.
>
> All seven tasks are done. Tasks 1-3 and 4-6 were implemented by subagents and reviewed before this commit. Against a local PostgreSQL with the migration applied: generating before a schedule existed returned `409`; an invalid schedule returned `400` listing every rule (Windows zone id, duplicate day, inverted and malformed times); replacing a schedule removed Sunday and changed Monday in place, and hours come back Monday first; a Monday 09:00-12:00 plus Wednesday 09:00-10:45 schedule with a 30-minute service generated 9 slots over a week, and regenerating created 0 and skipped 9; search returned Monday's six slots with 09:00 local as 03:30Z, an empty list for a closed day, `400` without a service id and `401` without a token; an inactive service or branch gave `404` on search and `409` on generate; a customer token got `403` on schedule and generate.

- [x] 1. Branch schedule in the domain
  - `WorkingHours`; `Branch.TimeZoneId`, `Branch.WorkingHours`, `Branch.SetSchedule` updating in place
  - `BranchScheduleTests`
  - _Requirements: 1.1, 1.3, 1.6_

- [x] 2. `Slot` and `SlotPlanner`
  - `Slot.Create`, `AvailableCapacity`; `SlotPlanner.Plan`
  - `SlotPlannerTests`, including the fixed-offset and DST cases
  - _Requirements: 2.2–2.5_

- [x] 3. Persistence
  - `OwnsMany` working hours, `TimeZoneId`, the `Slots` table with both indexes and foreign keys
  - `ISlotRepository`, `DuplicateSlotException`, `SlotRepository`; register `TimeProvider.System`
  - Migration `AddSchedulesAndSlots`
  - _Requirements: 2.3, 2.11, 3.6_

- [x] 4. Set a branch's schedule
  - `SetBranchScheduleRequest`, command, validator, handler; `BranchResponse` gains the schedule
  - `PUT /api/branches/{id}/schedule`; `JsonStringEnumConverter`
  - Validator, handler and controller tests
  - _Requirements: 1.1–1.7_

- [x] 5. Generate slots
  - `GenerateSlotsRequest`, command, validator, handler, `GenerateSlotsResponse`; `POST /api/slots/generate`
  - Validator, handler and controller tests
  - _Requirements: 2.1–2.11_

- [x] 6. Search available slots
  - Query, validator, handler, `SlotResponse`; `GET /api/slots/available`
  - Handler and controller tests
  - _Requirements: 3.1–3.7_

- [x] 7. Verify against a local PostgreSQL and document
  - Apply the migration; set a schedule, generate, regenerate, search, with admin and customer tokens
  - `.http` requests; `CLAUDE.md` and the spec index
  - _Requirements: 1–3_
