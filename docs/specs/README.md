# Feature specs

This project follows **spec-driven development**. Every feature is described by three documents before it is built, and those documents stay in the repository as the record of what the feature is supposed to do.

`docs/requirements.md` is the BRD for the whole system and owns the FR-IDs (`FR-AUTH-001`, `FR-AVL-003`, …). It is not replaced by anything here — feature specs refine a slice of it into something testable.

## The loop

```
requirements.md  ──►  design.md  ──►  tasks.md  ──►  implement
     approve            approve         approve
```

1. **One feature per folder.** A spec covers a bounded feature, not a whole microservice. `auth-identity` covers registration, login and profile retrieval because they share the `User` aggregate and one design; a separate concern such as password reset would get its own folder.
2. **Requirements are approved before a design is written.** The design is an answer to the acceptance criteria, so the criteria have to be settled first.
3. **The design is approved before tasks are written.** Tasks are coding steps derived from the design, nothing more.
4. **Tasks are checked off in the same commit as the code that satisfies them.** A checked box means the code exists and the tests pass. An unchecked box is the backlog.

Use the slash commands in `.claude/commands/` to walk the loop: `/spec-new`, `/spec-design`, `/spec-tasks`, `/spec-execute`. Each stops at its gate and waits for approval.

Templates live in [`_templates/`](_templates/).

## Index

| Feature | Status | FR-IDs | Spec |
|---|---|---|---|
| Auth identity | Implemented | FR-AUTH-001, FR-AUTH-002, FR-AUTH-003 | [requirements](auth-identity/requirements.md) · [design](auth-identity/design.md) · [tasks](auth-identity/tasks.md) |
| Availability branches | Partially implemented | FR-AVL-001 | [requirements](availability-branches/requirements.md) · [design](availability-branches/design.md) · [tasks](availability-branches/tasks.md) |

Features named in `docs/requirements.md` but not yet specced: service types (FR-AVL-002), slot generation and search (FR-AVL-003/004/005), appointment booking (FR-BKG-*), walk-in queue (FR-QUE-*), notifications (FR-NOT-001), reporting (FR-RPT-*).

## Retro-fitted specs

`auth-identity` and `availability-branches` were written **after** the code they describe. Their acceptance criteria were derived from the implementation and the existing tests rather than the other way round, and each says so at the top. Where the code does something the BRD never asked for, or omits something it did, the spec records the discrepancy instead of quietly matching one to the other.
