# ADR-0005: Booking reads customer display data from Auth through a batch lookup

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** Sajith Dilhan (accepted with RFC-0001)
**Related:** [RFC-0001](../rfc/0001-staff-branch-assignment.md), [staff-booking plan](../specs/staff-booking/plan.md)

## Context

Staff need customers' names and phone numbers in the branch list (user decision). Booking stores only customer ids; the people belong to Auth.

## Decision

We will add `POST /internal/users/lookup` to Auth (`{ids}`, at most 100, API key; returns `id, firstName, lastName, phoneNumber`, unknown ids omitted) and call it once per branch-list page from Booking through an `IAuthClient` built like its Availability client. Booking never stores the data.

## Alternatives considered

- Ids only: no coupling, but the list is not usable for staff.
- Copy names into Booking at booking time: no read-time call, but personal data duplicated and stale after profile changes.
- One call per appointment: simpler endpoint, N round trips per page.

## Consequences

### Positive

- Staff see who is coming; the data stays owned by Auth and current.

### Negative

- Booking gains a second synchronous dependency on the branch list path.
- Auth gains the API-key scheme and an internal surface that returns personal data, so it must stay unrouted and key-protected.

### Neutral

- The list's behaviour when only Auth is down is settled in the design.
