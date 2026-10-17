---
description: Stage 0 - draft docs/specs/<feature>/plan.md (and an RFC if cross-cutting), then stop for review
argument-hint: <feature-name in kebab-case>
---
Workflow stage 0 (Plan) for the feature: $ARGUMENTS. See `docs/WORKFLOW.md`. You are the orchestrator; do this yourself, spawn no agents here.

1. Normalise the feature name to kebab-case (the folder name under `docs/specs/`). If `docs/specs/<feature>/plan.md` already exists, stop and ask whether to revise it instead.
2. Read `CLAUDE.md`, `docs/specs/README.md`, `docs/specs/_templates/plan.md`, and the FR-IDs this feature refines in `docs/requirements.md`. If the feature or its FR-IDs are unclear, ask with AskUserQuestion before drafting. Read the existing code and specs the feature touches (verify against code; README.md is aspirational), and list `docs/adr/` and `docs/rfc/` for prior decisions.
3. Copy the template to `docs/specs/<feature>/plan.md` and fill every section: goal, scope and explicit out of scope, FR-IDs, approach, affected services, risks, open decisions, ADRs expected, RFC required (yes/no with the reason), subagent work split (non-overlapping chunks, in order), verification plan. Set `Status: Draft`.
4. Decide the RFC. It is required only for cross-cutting changes: a new service, a new infra dependency (RabbitMQ, Redis, outbox), a change to an inter-service contract or to BuildingBlocks / BuildingBlocks.Web, or a change touching 2 or more services. Otherwise record "No" and the reason.
5. If an RFC is required, run the `/rfc-new` steps to create it from `docs/rfc/_template.md` (next number, Status Draft, index updated) and fill it in, linking it from the plan.
6. List genuine open decisions in the plan, each with options and a recommended option. Do not resolve them by assumption.

Produces: `docs/specs/<feature>/plan.md` (Draft), optionally `docs/rfc/NNNN-*.md` (Draft). Nothing is committed.

STOP here. Summarise the plan in a few lines (scope, RFC yes/no, open decisions) and tell the user to run `/plan-review <feature>` next, or to edit the plan first.
