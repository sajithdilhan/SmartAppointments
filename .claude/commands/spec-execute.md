---
description: Stage 3 - implement approved tasks.md on a feature branch with implementer subagents, reviewing and committing each chunk
argument-hint: <feature-name>
---
Workflow stage 3 (Implementation) for the feature: $ARGUMENTS. See `docs/WORKFLOW.md`. You are the orchestrator; you spawn the agents, review, and commit. Implementers never commit.

1. Precondition: requirements.md, design.md and tasks.md exist and are approved. Read them, plus plan.md and `CLAUDE.md`. Run `git status`; the tree must be clean (otherwise ask the user).
2. Branch: if on `master`, create and switch to a branch named after the feature (`<feature>`, matching existing branches such as `gateway-cors`); if already on a feature branch for this work, stay; otherwise ask. Note the branch-protection habit: never commit to master.
3. Split the unchecked tasks into non-overlapping chunks (use the "Execution chunks" note in tasks.md). Chunks that touch disjoint files may run in parallel; anything sharing files, a DbContext, a DI file or a migration runs sequentially. Never let two implementers edit the same file.
4. For each chunk, spawn an `implementer` agent with `model: "sonnet"` passed explicitly. Prompt it with: the feature name, the exact task numbers, the files it owns, which earlier chunks are already done, and "do not commit, do not check off tasks.md". For parallel chunks send all Agent calls in one message.
5. Review each returned chunk yourself before accepting it: read the diff (`git diff`), check it against design.md and the cited requirement numbers, CLAUDE.md conventions (Result<T>, `ToActionResult`, layering, shared code in BuildingBlocks.Web), and that tests assert the criteria. Run `dotnet build SmartAppointments.slnx` and the affected test project yourself. If a chunk is wrong, send it back by spawning a fresh implementer with specific fixes (or use SendMessage to continue the same one). If an implementer reports a spec ambiguity, ask the user with AskUserQuestion and record the answer in design.md.
6. When a chunk is verified, tick its tasks in tasks.md and commit code, tests and the tick together, with a message naming the feature and tasks, ending with the attribution trailer given in your session. Stage specific files, not `git add -A`.
7. Repeat until every task is checked and `dotnet test SmartAppointments.slnx` passes.

Produces: committed work on the feature branch with all tasks checked. No push yet.

Then proceed directly to `/review <feature>` (stage 4); no user gate here unless a decision came up.
