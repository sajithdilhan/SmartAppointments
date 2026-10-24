# ADR-0009: End all sessions when a staff account is deactivated or deleted

**Status:** Accepted
**Date:** 2026-10-24
**Deciders:** Sajith Dilhan
**Related:** [staff-management plan](../specs/staff-management/plan.md), [requirements](../specs/staff-management/requirements.md) (Requirements 4, 5, 6), [design](../specs/staff-management/design.md); builds on [ADR-0007](0007-revoke-sessions-on-staff-reassignment.md)

## Context

ADR-0007 revokes a staff member's refresh-token families when their branch changes, and notes that deactivation was unchanged by it. Login and refresh already refuse an inactive user, and refresh revokes a family when it finds one. But a family nobody presents stays unrevoked. If the account is reactivated within the 7-day sliding window, such a token would work again. Deactivation and deletion now become admin actions with the same intent as a move: a clean break.

## Decision

We will revoke every unrevoked refresh token of a staff member, in the same transaction as the state change, when an admin deactivates or deletes the account. A repeat deactivation still runs the revocation, so a session created by a login that raced the first call is ended too. Reactivation restores no session: the staff member signs in again.

Move, deactivate and delete share one repository method, `IUserRepository.SaveAndRevokeSessionsAsync`, which is the existing `SaveBranchMoveAsync` renamed. Issued access tokens are not revoked. Their remaining lifetime (up to 60 minutes) is the same accepted residual risk as ADR-0007.

## Alternatives considered

- **No revocation, relying on login and refresh refusing inactive users**: reactivation within the refresh-token lifetime would resurrect old sessions, possibly on devices the staff member no longer controls.
- **Revoke on reactivation instead**: this leaves live-looking tokens in the database while the account is out of service, and makes reactivation, the less urgent action, do the security work.
- **An access-token denylist**: it closes the 60-minute window too, but adds shared state and a check on every request in every service. It was already rejected in ADR-0007.

## Consequences

### Positive

- Deactivation and deletion are clean breaks, consistent with a move.
- One transactional method covers all three admin actions.

### Negative

- The access-token window remains: a deactivated or deleted staff member can keep calling Booking for up to 60 minutes.
- Reactivated staff must sign in again, even after a deactivation of a few seconds.

### Neutral

- ADR-0007 is unchanged. This ADR extends its rule to two more state changes.
