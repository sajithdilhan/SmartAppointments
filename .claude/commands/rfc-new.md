---
description: Create the next numbered RFC from docs/rfc/_template.md (Status Draft) and index it
argument-hint: <title>
---
Create an RFC titled: $ARGUMENTS. RFCs cover cross-cutting changes only (new service, new infra dependency such as RabbitMQ/Redis/outbox, a change to an inter-service contract or to BuildingBlocks/BuildingBlocks.Web, or a change touching 2+ services). If this change does not qualify, say so and ask the user whether to continue. See `docs/WORKFLOW.md`.

1. List `docs/rfc/` and take the highest existing 4-digit number plus one (start at 0001 if none; ignore `_template.md` and `README.md`). Never reuse a number.
2. Slugify the title to kebab-case and copy `docs/rfc/_template.md` to `docs/rfc/NNNN-kebab-title.md`.
3. Fill the template: title, `Status: Draft`, today's date, author, related plan (`docs/specs/<feature>/plan.md` if any), problem and context, goals and non-goals, the proposal, at least two real alternatives with trade-offs, consequences, rollout and compatibility (docker-compose, gateway routes, config keys, contract changes), open questions, and the ADRs this RFC is expected to produce. Ground it in the actual code and `docs/requirements.md`; do not describe aspirational architecture as if built.
4. Add a row to the index table in `docs/rfc/README.md` (number, title linked to the file, status, created date, related plan/specs), keeping numeric order.
5. Do not commit.

Produces: `docs/rfc/NNNN-kebab-title.md` (Draft) and the updated index. If run standalone, stop and report the path; the RFC is reviewed by `architecture-reviewer` in `/plan-review`. Statuses (Draft, In review, Accepted, Rejected, Withdrawn, Implemented) move to Accepted or Rejected only with the user's explicit approval.
