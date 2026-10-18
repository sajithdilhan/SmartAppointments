# Architecture decision records

An ADR records one architectural decision: what was decided, why, and what else was considered. The specs say what a feature does. ADRs say why a choice was made, so it is not reopened by accident.

The process that produces them is in [`docs/WORKFLOW.md`](../WORKFLOW.md).

## Rules

1. **Location and naming.** `docs/adr/NNNN-kebab-title.md`, with a 4-digit sequential number. Numbers are never reused, even for a rejected ADR.
2. **Statuses.** `Proposed`, `Accepted`, `Rejected`, `Deprecated`, `Superseded by ADR-NNNN`.
3. **One decision per ADR.**
4. **Written during design or at RFC acceptance.** An ADR written during design starts as `Proposed` and becomes `Accepted` when the design is approved. An ADR derived from an RFC is written as `Accepted` when the RFC is accepted.
5. **Immutable once accepted**, except for its status line. To change a decision, write a new ADR that supersedes it and update the old one's status to `Superseded by ADR-NNNN`.
6. **When to write one.** The decision is hard to reverse, affects more than one feature or service, picks between real alternatives, or deviates from an existing convention in `CLAUDE.md`.
7. **No backfill.** Decisions made before ADR-0001 are documented in the specs and `CLAUDE.md`, not as ADRs.
8. **RFCs.** An RFC proposes a change; the decisions it settles are recorded here as ADRs that link back to it.

Start from [`_template.md`](_template.md).

## Index

| Number | Title | Status | Date | Related |
|---|---|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted | 2026-10-17 | [WORKFLOW](../WORKFLOW.md) |
| [0002](0002-staff-branch-jwt-claim.md) | Carry a staff member's branch as a JWT claim | Accepted | 2026-10-18 | [RFC-0001](../rfc/0001-staff-branch-assignment.md) |
| [0003](0003-auth-validates-branch-via-availability.md) | Auth validates staff branch assignments through Availability | Accepted | 2026-10-18 | [RFC-0001](../rfc/0001-staff-branch-assignment.md) |
| [0004](0004-branch-local-day-for-staff-queries.md) | Staff day queries use the branch's local day | Accepted | 2026-10-18 | [RFC-0001](../rfc/0001-staff-branch-assignment.md) |
| [0005](0005-booking-reads-customers-from-auth.md) | Booking reads customer display data from Auth through a batch lookup | Accepted | 2026-10-18 | [RFC-0001](../rfc/0001-staff-branch-assignment.md) |
| [0006](0006-conditional-appointment-transitions.md) | Appointment state transitions use conditional updates | Accepted | 2026-10-18 | [RFC-0001](../rfc/0001-staff-branch-assignment.md) |
| [0007](0007-revoke-sessions-on-staff-reassignment.md) | Revoke all of a staff member's sessions when their branch changes | Accepted | 2026-10-18 | [RFC-0001](../rfc/0001-staff-branch-assignment.md) |
