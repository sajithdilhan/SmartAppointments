---
description: Stage 2 - write tasks.md from the approved design, then stop for approval
argument-hint: <feature-name>
---
Workflow stage 2, step 3 (tasks) for the feature: $ARGUMENTS. See `docs/WORKFLOW.md`. You are the orchestrator; spawn no agents.

1. Precondition: `docs/specs/<feature>/design.md` exists and the user has explicitly approved it in this conversation, and its ADRs are Accepted. Otherwise stop. If `tasks.md` exists, ask whether to revise.
2. Read plan.md (especially the subagent work split), requirements.md, design.md, `docs/specs/_templates/tasks.md`, and a finished `tasks.md` from another feature for granularity.
3. Copy the template to `docs/specs/<feature>/tasks.md` and write the checklist. Rules from the template: coding steps only (no research, discuss or deploy items); each task cites requirement numbers (`_Requirements: 1.2, 1.3_`); each is small enough to finish and test in one sitting and leaves `dotnet build SmartAppointments.slnx` green; order is domain, persistence (with migration), application, API, gateway/config, then tests. Each task includes its own unit tests, in the xUnit + Moq style, rather than a trailing "write tests" block.
4. Every acceptance criterion must be cited by at least one task; check this and fix gaps. Add a final task for gateway routes/config/docker-compose and `.http` examples if endpoints are public.
5. Group the tasks into chunks matching the plan's work split, marking which chunks touch disjoint files and so may run in parallel, and which depend on others. Put this as a short "Execution chunks" note at the bottom of tasks.md. Do not commit.

Produces: `docs/specs/<feature>/tasks.md` with all boxes unchecked.

STOP here for approval. Summarise the task count, the chunks and the criteria coverage, and wait for the user's explicit approval before `/spec-execute <feature>`.
