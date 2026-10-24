# ADR-0008: Soft-delete staff accounts

**Status:** Accepted
**Date:** 2026-10-24
**Deciders:** Sajith Dilhan
**Related:** [staff-management plan](../specs/staff-management/plan.md), [requirements](../specs/staff-management/requirements.md) (Requirement 6), [design](../specs/staff-management/design.md)

## Context

Admins need to remove a staff account for good (`DELETE /api/auth/staff/{id}`). Nothing in Booking stores a staff id today, but complete and no-show are natural places to record who acted later on, and an audit trail is useless if the person behind an id has vanished. The `Users` table enforces a unique email, and refresh tokens cascade-delete with their user.

## Decision

We will delete staff accounts softly. A nullable `Users.DeletedAtUtc` records the deletion instant, and deletion also sets `IsActive = false`. The row, the name and the email stay. A deleted account:
- Can never sign in: login and refresh check `User.CanSignIn`, which means active and not deleted.
- Is absent from the staff list and gives `404` on every staff endpoint and on profile-by-email.
- Keeps its email registered.
- Is still returned by Auth's internal user lookup, so names resolve.

There is no undelete.

Deleted accounts are excluded by explicit checks in the handlers that must not see them, not by an EF Core global query filter. A filter would also hide them from the duplicate-email check, letting the unique index reject a re-registration only through the race path. It would also hide them from the internal lookup.

## Alternatives considered

- **Hard delete, any time**: simplest, and it frees the email. But it loses the name behind any future audit record and cannot be undone by an operator.
- **Hard delete only when deactivated**: the extra step guards against mistakes, but it still loses the audit trail.
- **Soft delete with a global query filter**: fewer explicit checks, but it breaks the email-stays-taken rule and the lookup, as described above.

## Consequences

### Positive

- Future "completed by" or "cancelled by" records can always resolve a name.
- An operator can still inspect or repair a deleted account in the database.

### Negative

- A re-hired person needs a different email address.
- Every handler that must not see deleted accounts needs its own check, and a missed one acts on a deleted user. Each has a test for the deleted case.
- Rows are never purged; a retention policy would be a later decision.

### Neutral

- The pattern applies to staff only. Customers and admins have no delete endpoint.
