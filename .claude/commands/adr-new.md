---
description: Create the next numbered ADR from docs/adr/_template.md (Status Proposed) and index it
argument-hint: <title>
---
Create an ADR titled: $ARGUMENTS. One ADR records exactly one decision. See `docs/WORKFLOW.md`.

1. List `docs/adr/` and take the highest existing 4-digit number plus one (start at 0001 if none; ignore `_template.md` and `README.md`). Never reuse a number.
2. If this changes or reverses an existing Accepted ADR, do NOT edit that ADR except to set its status to `Superseded by ADR-NNNN`; the new ADR says `Supersedes ADR-XXXX`. Accepted ADRs are otherwise immutable.
3. Slugify the title to kebab-case and copy `docs/adr/_template.md` to `docs/adr/NNNN-kebab-title.md`.
4. Fill it in: `Status: Proposed` (exception: an ADR derived from an RFC the user has just accepted via `/plan-review` is written as `Accepted`), today's date, context (the forces and the constraints from `docs/requirements.md` and the code), the decision in one clear statement, alternatives considered with why they lost, consequences (good, bad, follow-ups), and links to the plan, RFC or design section and the requirement numbers it serves. Verify claims about existing code before writing them.
5. Add a row to the index table in `docs/adr/README.md` (number, linked title, status, date, related spec/RFC), in numeric order.
6. Do not commit.

Produces: `docs/adr/NNNN-kebab-title.md` (Proposed) and the updated index. ADRs are written as Proposed while a design is drafted and become Accepted only when the user approves that design (see `/spec-design`); never mark one Accepted on your own, except an ADR derived from an RFC the user has just accepted via `/plan-review`, which is written as Accepted.
