---
description: Stage 2 - write design.md and Proposed ADRs from approved requirements, stop for approval
argument-hint: <feature-name>
---
Workflow stage 2, step 2 (design) for the feature: $ARGUMENTS. See `docs/WORKFLOW.md`. You are the orchestrator.

1. Precondition: `docs/specs/<feature>/requirements.md` exists and the user has explicitly approved it in this conversation (ask if unsure). Otherwise stop. If `design.md` exists, ask whether to revise.
2. Read `CLAUDE.md`, plan.md, requirements.md, the cited RFC and Accepted ADRs, `docs/specs/_templates/design.md`, and the real code you will extend: Auth first, Availability second, Booking for HTTP clients and idempotency, and `BuildingBlocks.Web` for what already exists. Mirror their layout and DI wiring.
3. Copy the template to `docs/specs/<feature>/design.md` and fill every section. Each decision cites the criteria it satisfies, e.g. "(Req 2.2)"; a decision satisfying nothing is removed, and a criterion no decision satisfies means the requirements are incomplete (tell the user). Cover: layer table respecting Api -> Application -> Domain, Infrastructure -> Application; endpoints with authorization and dispatched command/query; handlers, validators, abstractions; data model and EF Core configuration and migration; error handling via `Result<T>` and `this.ToActionResult`; concurrency/idempotency; gateway routes, rate limits and CORS if endpoints are public; configuration keys; testing strategy in the xUnit + Moq style.
4. For each significant, hard-to-reverse decision (new pattern or library, data-model trade-off, consistency strategy, new inter-service dependency), run the `/adr-new` steps to create an ADR with `Status: Proposed`, one decision each, and link it from the design. Do not write an ADR for routine choices that follow existing conventions.
5. Settle real design forks with AskUserQuestion (recommended option first) rather than choosing silently. Do not commit.

Produces: `docs/specs/<feature>/design.md` and Proposed ADRs under `docs/adr/` (indexed).

STOP here for approval. Summarise the design and list the ADRs. When the user approves the design, set each linked ADR to `Status: Accepted` (and update the `docs/adr/README.md` index) in that same step, then tell them the next step is `/spec-tasks <feature>`. If they request changes, revise and ask again; ADRs stay Proposed until then.
