# ADR-0007: Revoke all of a staff member's sessions when their branch changes

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** Sajith Dilhan (accepted with RFC-0001)
**Related:** [RFC-0001](../rfc/0001-staff-branch-assignment.md), [staff-booking plan](../specs/staff-booking/plan.md), [ADR-0002](0002-staff-branch-jwt-claim.md)

## Context

With the branch carried as a claim (ADR-0002), a reassigned staff member keeps the old branch until their access token is replaced. The refresh path would already issue the new branch.

## Decision

We will revoke every refresh-token family of a staff member when an admin changes their branch (new `IRefreshTokenRepository.RevokeAllForUserAsync`), forcing a fresh login. The issued access token is not revoked; its remaining lifetime (up to 60 minutes) is an accepted residual risk.

## Alternatives considered

- No revocation: the next refresh carries the new branch anyway; simpler, but the session silently continues.
- An access-token denylist checked by every service: closes the window, but adds shared state and a check on every request.

## Consequences

### Positive

- A reassignment is a clean break: the staff member signs in again under the new branch.

### Negative

- The access-token window remains; Auth gains a new repository method and behaviour.

### Neutral

- Deactivating a user is unchanged by this decision.
