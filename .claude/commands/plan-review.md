---
description: Stage 1-2 - review the plan (and RFC) with subagents, revise, then ask the user to accept it
argument-hint: <feature-name>
---
Workflow stages 1 and 2 (Review, Acceptance) for the feature: $ARGUMENTS. See `docs/WORKFLOW.md`. You are the orchestrator.

1. Read `docs/specs/<feature>/plan.md`. It must exist and be Draft or In review (so the command can be re-run); otherwise tell the user to run `/plan-new`. Check whether it links an RFC (`docs/rfc/NNNN-*.md`).
2. In ONE message, spawn in parallel, each with `model: "sonnet"` passed explicitly:
   - `plan-reviewer` agent: "Review docs/specs/<feature>/plan.md (and any other spec docs present) for feature <feature>."
   - `architecture-reviewer` agent, only if an RFC exists: "RFC mode: review docs/rfc/NNNN-<title>.md for feature <feature>."
   Right after spawning, set `Status: In review` in plan.md and, if an RFC exists, in the RFC and its row in `docs/rfc/README.md`.
3. Consolidate the findings. Fix Blockers and Major findings that are clear improvements by editing plan.md (and the RFC), re-verifying claims against the code yourself rather than trusting a reviewer blindly. Anything that is a real design choice becomes an entry under open decisions, not a silent edit. Show the user a short table: finding, severity, applied / deferred / rejected with reason. If Blockers needed a substantial rewrite, run the reviewers once more.
4. Ask the user to accept using AskUserQuestion: "Accept this plan?" with options "Accept" (recommended when no Blocker remains), "Revise (I will say what)", "Reject". Also ask each remaining open decision with AskUserQuestion, one question per decision, marking your recommended option first with "(Recommended)". Never assume an answer. Record the decisions in plan.md.
5. If an RFC exists, ask whether to move it to Accepted too (same AskUserQuestion pattern) and update its status and the `docs/rfc/README.md` index. When it moves to Accepted, run the `/adr-new` steps for each decision the RFC settles, link each ADR to the RFC, and write it with `Status: Accepted` (the user's RFC acceptance is the approval), updating the `docs/adr/README.md` index.
6. On acceptance set `Status: Accepted` in plan.md with today's date. Do not commit unless the user asks; tell them the next step is `/spec-new <feature>`.

Produces: a revised, accepted plan.md (and RFC). Stops at the user's acceptance in step 4; if they choose Revise, apply their changes and ask again.
