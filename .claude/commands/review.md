---
description: Stages 4-5 - code review and architecture review of the branch in parallel, fix agreed findings, re-review until clean
argument-hint: [feature-name]
---
Workflow stages 4 and 5 (Code review, Architecture review). Feature: $ARGUMENTS (if blank, infer it from the branch name or the newest folder under `docs/specs/` whose tasks.md is fully checked; ask if unclear). See `docs/WORKFLOW.md`. You are the orchestrator.

1. Confirm you are on the feature branch (not master) and that `git diff master...HEAD` is non-empty. Commit nothing yet.
2. In ONE message, spawn in parallel, each with `model: "sonnet"` passed explicitly:
   - `code-reviewer`: "Review git diff master...HEAD plus uncommitted changes for feature <feature>."
   - `architecture-reviewer`: "Diff mode: check the branch against docs/specs/<feature>/design.md, accepted ADRs and any RFC for feature <feature>."
3. Consolidate both reports into one list, de-duplicated, with severity. Verify each Blocker and Major yourself by reading the cited code; drop false positives and say why. Present the user a short table: finding, severity, proposed action (fix / defer / reject).
4. Agree the fix list. Blockers are always fixed. For Majors, Minors and anything that is a design change (including an ADR a reviewer says is missing, which you create via the `/adr-new` steps), ask the user with AskUserQuestion, recommended option first. Never silently expand scope.
5. Spawn `implementer` agent(s) with `model: "sonnet"` to apply the agreed fixes, as non-overlapping chunks with the exact findings (path:line and the intended fix). They do not commit. Review their diff, run `dotnet build SmartAppointments.slnx` and the affected tests, then commit the fixes with a descriptive message and the attribution trailer.
6. Re-run step 2 on the new state. Repeat until both reviewers report no Blocker or Major (and the user is content with remaining Minors). Cap at three rounds; if still not clean, stop and report to the user.

Produces: a clean review on the branch, fix commits, and a short review summary for the PR body (what was found, fixed, deferred).

Then proceed to `/verify <feature>` (stage 6).
