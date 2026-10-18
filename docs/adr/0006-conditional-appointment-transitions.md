# ADR-0006: Appointment state transitions use conditional updates

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** Sajith Dilhan (accepted with RFC-0001)
**Related:** [RFC-0001](../rfc/0001-staff-branch-assignment.md), [staff-booking plan](../specs/staff-booking/plan.md)

## Context

Cancel, complete and no-show can race (a customer cancels while staff completes). Cancel today loads, mutates and saves the appointment with no concurrency guard, so two requests can both succeed.

## Decision

We will apply each transition as one conditional `UPDATE ... WHERE Id = @id AND Status = 'Booked'`. When no row changes, the handler re-reads the appointment and returns the repeat-mark `200` (same status) or a `409`. The existing cancel moves to the same approach. This matches the slot reservation and refresh-token rotation code.

## Alternatives considered

- An EF Core concurrency token (row version): also safe, but adds a column and exception-driven control flow.
- Leave load-modify-save: a known race.

## Consequences

### Positive

- Exactly one of concurrent transitions wins, and the loser gets a clear `409`.

### Negative

- Transition logic sits partly in the repository (the SQL condition), not only in the domain method.

### Neutral

- The domain methods still validate before the update, so unit tests cover the rules.
