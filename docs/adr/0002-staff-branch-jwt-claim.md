# ADR-0002: Carry a staff member's branch as a JWT claim

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** Sajith Dilhan (accepted with RFC-0001)
**Related:** [RFC-0001](../rfc/0001-staff-branch-assignment.md), [staff-booking plan](../specs/staff-booking/plan.md)

## Context

Booking must confine staff to their branch (FR-BKG-004, FR-BKG-006, FR-AUTH-003), but the branch assignment belongs to the user, which Auth owns. Booking needs the branch on every staff request.

## Decision

We will store the branch on the user in Auth (`User.BranchId`, required for Staff) and put it in staff access tokens as a `branch_id` claim (a GUID string, `Constants.BranchIdClaimType`). Booking authorises staff requests by comparing the claim with the branch of the route or the appointment. Non-staff tokens carry no `branch_id`; a staff request without it is refused.

## Alternatives considered

- Booking calls an Auth internal endpoint on each staff request: always current, but every staff request depends on Auth being up and pays a round trip.
- Booking keeps its own staff-to-branch table: no runtime coupling, but user data is duplicated in the wrong service and needs its own admin API.

## Consequences

### Positive

- No runtime call to decide access; Booking stays available when Auth is down.
- The claim is signed with the existing key; other services ignore it, so the change is backward compatible.

### Negative

- A reassignment reaches Booking only with the next access token: a moved staff member keeps the old branch for up to the access-token lifetime (60 minutes). Accepted as a residual risk (see ADR-0007).

### Neutral

- Existing staff without a branch cannot use the staff endpoints until an admin assigns one.
