# ADR-0003: Auth validates staff branch assignments through Availability

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** Sajith Dilhan (accepted with RFC-0001)
**Related:** [RFC-0001](../rfc/0001-staff-branch-assignment.md), [staff-booking plan](../specs/staff-booking/plan.md)

## Context

Admins assign staff to a branch in Auth, but branches live in Availability. Without a check, a mistyped id silently creates a staff member who sees nothing.

## Decision

We will add `GET /internal/branches/{id}` to Availability (API key, `InternalServicePolicy`, `{id, isActive, timeZoneId}`, `404` unknown) and call it from Auth when creating or reassigning staff. An unknown or inactive branch gives `422`; Availability unreachable gives `503`. Auth's client follows Booking's `AvailabilityClient` pattern (typed `HttpClient`, `AddStandardResilienceHandler`, `X-API-Key`, `CorrelationIdHandler`), and Auth now requires `InternalApi:Key` and `Services:Availability:BaseUrl` at startup.

## Alternatives considered

- No validation: Auth stays independent, but bad ids go unnoticed.
- Validate asynchronously through events: no messaging infrastructure exists yet.

## Consequences

### Positive

- Assignments always point at a real, active branch at the time they are made.
- The same internal endpoint serves Booking's time-zone need (ADR-0004).

### Negative

- Auth gains its first synchronous dependency: admins cannot create or move staff during an Availability outage.
- Auth needs the shared API key and the Availability URL in every environment.

### Neutral

- A branch deactivated later is not re-checked; the assignment stays until an admin changes it.
