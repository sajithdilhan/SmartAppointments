# <Feature name> — Plan

<!--
Copy this file to docs/specs/<feature-name>/plan.md.
This is stage 0 of docs/WORKFLOW.md. The orchestrator drafts it, a plan-reviewer
subagent reviews it, and the user accepts it before requirements.md is written.
Keep it short. It decides what to build and how to split the work; the spec trio
that follows says exactly how it behaves.
FR-IDs are owned by docs/requirements.md (the BRD). Cite them; do not invent them.
Never assume an answer to an open decision. Ask the user.
-->

**Status:** Draft <!-- Draft | In review | Accepted | Implemented -->

## Problem and goal

<!-- Two or three sentences: what is wrong or missing today, and what is true
once this feature exists. -->

## Scope

<!-- Bullet list of what this feature delivers. -->

## Out of scope

<!-- What a reader might expect here but will not get, and where it goes instead
(a later feature, another service, never). -->

## FR-IDs

<!-- Requirements from docs/requirements.md this plan covers, one per line. If
part of the work has no FR-ID, say "(no FR-ID)" and note whether the BRD needs
updating. -->

| FR-ID | Summary |
|---|---|

## Approach

<!-- The shape of the solution in a paragraph or two. Name the main moving parts
and anything non-obvious. Check docs/specs/ and CLAUDE.md for what already exists
and reuse it. -->

## Affected services and projects

<!-- List each service and project touched, with a line on what changes. Touching
two or more services, or a shared library, usually means an RFC (see below). -->

| Service / project | Change |
|---|---|

## Risks

<!-- Technical, delivery and compatibility risks, each with a mitigation or a
note that it is accepted. -->

## Open decisions

<!-- Questions only the user can answer. Each one is asked with AskUserQuestion
before the plan is accepted. Record the answer here once given. Delete the
section if there are none. -->

1. <Question> — options: <A>, <B>. Answer: _pending_.

## ADRs expected

<!-- Decisions likely to need an ADR (hard to reverse, cross-feature, a real
choice between alternatives, or a deviation from convention). Titles only; the
ADRs are written during design as Proposed. Write "None expected" if so. -->

## RFC

<!-- Required only for cross-cutting changes: a new service, a new infrastructure
dependency (RabbitMQ, Redis, an outbox), a change to an inter-service contract
(internal APIs, events) or to BuildingBlocks / BuildingBlocks.Web, or a change
touching two or more services. Say whether one is required and why or why not.
If it is, link the draft: docs/rfc/NNNN-kebab-title.md. -->

**Required:** Yes / No — <reason>

## Work split

<!-- Chunks for implementer subagents. Each chunk owns a distinct set of files so
chunks never edit the same file. Refine into tasks.md after design is approved. -->

| Chunk | Owns (projects / folders) | Depends on |
|---|---|---|

## Verification plan

<!-- How we will know it works. -->

**Automated tests:** <!-- Which kinds of test cover which behaviour, in the
xUnit + Moq style of the existing test projects. -->

**Manual end-to-end checks:** <!-- Docker Compose and .http requests to run,
with the expected result of each. -->
