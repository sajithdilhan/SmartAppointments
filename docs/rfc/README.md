# Requests for comments

An RFC proposes a cross-cutting change and gets it reviewed before any feature spec is written. It proposes; it does not record the final decisions. Those are written as [ADRs](../adr/README.md) that link back to the RFC.

The process that produces them is in [`docs/WORKFLOW.md`](../WORKFLOW.md).

## When an RFC is required

Only for cross-cutting changes:

- a new service;
- a new infrastructure dependency (for example RabbitMQ, Redis or an outbox);
- a change to an inter-service contract (internal APIs, events);
- a change to the shared libraries (`BuildingBlocks`, `BuildingBlocks.Web`);
- a change touching two or more services.

Everything else needs only a plan, a spec and ADRs.

## Rules

1. **Location and naming.** `docs/rfc/NNNN-kebab-title.md`, with a 4-digit sequential number that is never reused.
2. **Statuses.** `Draft`, `In review`, `Accepted`, `Rejected`, `Withdrawn`, `Implemented`.
3. **Drafted at stage 0**, alongside `plan.md`, with status `Draft`. It is reviewed by the `architecture-reviewer` subagent, then accepted by the user.
4. **One RFC can span several feature specs.** Each spec links back to it.
5. **Decisions become ADRs.** When the RFC is accepted, the decisions it settles are written as ADRs that link to it.
6. **Mark it `Implemented`** once every spec it spans is implemented.

Start from [`_template.md`](_template.md).

## Index

| Number | Title | Status | Created | Related |
|---|---|---|---|---|
| [0001](0001-staff-branch-assignment.md) | Staff branch assignment and branch appointment operations | Accepted | 2026-10-18 | [staff-booking](../specs/staff-booking/plan.md) |
