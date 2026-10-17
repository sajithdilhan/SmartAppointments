# ADR-0001: Record architecture decisions

**Status:** Accepted
**Date:** 2026-10-17
**Deciders:** Sajith Dilhan
**Related:** [`docs/WORKFLOW.md`](../WORKFLOW.md), [`docs/specs/README.md`](../specs/README.md)

## Context

The project is spec-driven. Requirements, design and tasks record what each feature does and how it is built. They do not record the reasoning behind decisions that reach beyond one feature, such as a new infrastructure dependency or a change to a shared library. That reasoning ends up in commit messages and conversations, and is lost.

Larger changes also touch several services at once. A feature spec is too narrow to propose them, and nothing exists to review them before design starts.

## Decision

We will record significant architectural decisions as ADRs in `docs/adr/`, following the rules in [`docs/adr/README.md`](README.md).

We will propose cross-cutting changes as RFCs in `docs/rfc/` before any feature spec is written. A change is cross-cutting if it adds a service or an infrastructure dependency, changes an inter-service contract or a shared library, or touches two or more services. Each RFC records its settled decisions as ADRs.

We will adopt the development workflow in [`docs/WORKFLOW.md`](../WORKFLOW.md): plan, review, acceptance, implementation, code review, architecture review, then testing and verification. It adds a `plan.md` before the existing requirements, design and tasks documents.

We will not backfill ADRs. Decisions made before this one are documented in the specs and in `CLAUDE.md`, not as ADRs. Numbering starts here.

## Alternatives considered

- **Keep decisions inside design.md only.** Simple, but a design is scoped to one feature, so a decision that affects other features is hard to find and easy to contradict.
- **Backfill ADRs for existing decisions.** Complete, but expensive, and the specs and `CLAUDE.md` already describe them. Rejected by the project owner.
- **ADRs without RFCs.** Lighter, but an ADR records a decision after the fact. Cross-cutting changes benefit from review while still a proposal.

## Consequences

### Positive

- The reasons behind significant decisions are kept next to the code.
- Cross-cutting changes are reviewed before design effort is spent on them.
- Superseding an ADR leaves a visible history instead of a silent change.

### Negative

- More documents to write and keep consistent for each large feature.
- Small features gain a `plan.md` step, unless they qualify as small changes under the workflow.

### Neutral

- Existing specs predate `plan.md` and have none.
- The BRD, `docs/requirements.md`, still owns FR-IDs.
