# ADR-0004: Staff day queries use the branch's local day

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** Sajith Dilhan (accepted with RFC-0001)
**Related:** [RFC-0001](../rfc/0001-staff-branch-assignment.md), [staff-booking plan](../specs/staff-booking/plan.md), [ADR-0003](0003-auth-validates-branch-via-availability.md) (the internal branch endpoint)

## Context

Staff list a day's appointments for their branch (FR-BKG-004). Booking stores only UTC times and has no branch time zone; a UTC day would start at the wrong local hour for branches far from UTC.

## Decision

We will interpret `date` (`yyyy-MM-dd`) as the branch's local calendar day: Booking reads the branch's `timeZoneId` from Availability's internal branch endpoint, keeps it in an in-memory cache for a few minutes, and queries `[localMidnight, nextLocalMidnight)` converted to UTC, so DST days are 23 or 25 hours. Availability unreachable with no cached zone gives `503`.

## Alternatives considered

- A UTC day: no call, but wrong for most branches.
- The client sends a UTC `from`/`to`: Booking stays simple, but every client re-implements the branch-day logic.
- Copy the zone onto each appointment at booking time: no read-time call, but stale if a branch changes zone and needs a backfill.

## Consequences

### Positive

- Staff see exactly the appointments of their local day, DST included.

### Negative

- The branch list depends on Availability (softened by the cache).
- A small new pattern in Booking: an in-memory cache.

### Neutral

- The behaviour for a branch without a schedule (no zone) is settled in the design.
