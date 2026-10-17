---
description: Stage 2 - write requirements.md from the accepted plan, then stop for approval
argument-hint: <feature-name>
---
Workflow stage 2, step 1 (requirements) for the feature: $ARGUMENTS. See `docs/WORKFLOW.md` and `docs/specs/README.md`. You are the orchestrator; spawn no agents.

1. Precondition: `docs/specs/<feature>/plan.md` exists with `Status: Accepted`. If not, stop and tell the user to run `/plan-new` and `/plan-review`. If `requirements.md` already exists, stop and ask whether to revise.
2. Read `CLAUDE.md`, the accepted plan (scope, out of scope, resolved open decisions), the FR-IDs it cites in `docs/requirements.md`, any cited RFC and Accepted ADRs, `docs/specs/_templates/requirements.md`, and one or two existing `requirements.md` files (e.g. `booking-my-appointments`) for tone and granularity.
3. Copy the template to `docs/specs/<feature>/requirements.md` and fill it: introduction (owning service, BRD sections refined), then numbered requirements headed with their FR-ID (or "(no FR-ID)"), each with a user story and acceptance criteria in EARS form (WHEN/IF/WHILE/WHERE ... THEN the system SHALL ...). Every criterion must be independently testable and name a concrete status code, field or behaviour, including authorization (role, owner, the 404-for-non-owner convention), validation, idempotency and error cases. Error bodies are the shared `{status, detail}` problem+json. Add an "Out of scope" section matching the plan.
4. Do not invent behaviour the plan or BRD did not settle. Where something is undecided, ask the user with AskUserQuestion (one question per decision, recommended option first) before writing the criterion.
5. Do not write design.md, and do not commit.

Produces: `docs/specs/<feature>/requirements.md`.

STOP here for approval. Give the user a short summary (number of requirements and criteria, notable decisions) and wait. They approve explicitly; only then is `/spec-design <feature>` allowed.
