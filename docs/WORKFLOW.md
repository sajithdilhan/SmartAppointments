# Development workflow

This is the authoritative description of how a change goes from idea to merged pull request. It builds on the spec-driven loop in [`specs/README.md`](specs/README.md) and adds a plan, reviews by subagents, ADRs, RFCs and a verification stage.

`docs/requirements.md` (the BRD) still owns the FR-IDs. Nothing here replaces it.

## Roles

| Role | Who | Does |
|---|---|---|
| Orchestrator | The main Claude session | Drafts plans, RFCs, specs and ADRs. Delegates implementation and reviews. Reviews every chunk, commits, raises the PR. Does the manual end-to-end check. |
| User | The project owner | Answers open decisions, approves each gate, accepts plans, RFCs and designs. |
| `plan-reviewer` | Subagent (Sonnet) | Reviews `plan.md` for gaps, BRD conflicts, convention violations and risks. Read-only. |
| `implementer` | Subagent (Sonnet) | Writes code and tests for a chunk of `tasks.md` on the feature branch. Does not commit. |
| `code-reviewer` | Subagent (Sonnet) | Reviews the branch diff for correctness, `CLAUDE.md` conventions, security and test quality. Read-only. |
| `architecture-reviewer` | Subagent (Sonnet) | Reviews an RFC; later checks the diff against `design.md`, the accepted ADRs and the RFC, and the Clean Architecture layering rules. Read-only. |
| `test-verifier` | Subagent (Sonnet) | Builds and tests the solution, maps every acceptance criterion to the tests covering it, reports gaps. |

The agents are defined in `.claude/agents/`. All run on Sonnet.

The orchestrator never assumes an answer to a design question. Open decisions are asked of the user with AskUserQuestion.

## Flow

```
 0 Plan            plan.md  (+ RFC draft if required)
        │
 1 Review          plan-reviewer  (+ architecture-reviewer on the RFC)
        │              orchestrator revises
 2 Acceptance      user accepts plan / RFC ──► ADRs Proposed during design
        │
        │   requirements.md ─approve─► design.md ─approve─► tasks.md ─approve─┐
        │                                                                     │
 3 Implementation  implementer chunks on a feature branch; orchestrator       │
        │          reviews each chunk, then commits code + tasks.md ◄─────────┘
        │
        ├──────────────────────┬──────────────────────────┐
 4 Code review          5 Architecture review      (read-only, parallel)
   code-reviewer          architecture-reviewer
        │                      │
        └──────────┬───────────┘
                   │  findings fixed by an implementer, then re-reviewed
 6 Verification    test-verifier + manual end-to-end check
        │          docs updated, PR raised
      Done  =  tests pass AND a PR is raised
```

## Stages

### 0. Plan

- **Entry:** the user asks for a feature or a cross-cutting change.
- **Work:** the orchestrator reads the BRD, `CLAUDE.md` and the existing specs, then drafts `docs/specs/<feature>/plan.md` from [`specs/_templates/plan.md`](specs/_templates/plan.md). It covers the problem and goal, scope and out of scope, FR-IDs, approach, affected services, risks, open decisions, expected ADRs, whether an RFC is required, the subagent work split and the verification plan.
- **RFC:** if the change is cross-cutting (see below), the orchestrator also drafts `docs/rfc/NNNN-kebab-title.md` with status `Draft`.
- **Exit:** a complete `plan.md` with status `Draft`.
- **Artefacts:** `plan.md`, optionally an RFC.

### 1. Review

- **Entry:** a drafted plan.
- **Work:** the `plan-reviewer` reviews `plan.md`. If there is an RFC, the `architecture-reviewer` reviews it. They report gaps, BRD conflicts, convention violations and risks. The orchestrator revises. Plan status becomes `In review`; the RFC becomes `In review`.
- **Exit:** the orchestrator has addressed every finding, or recorded why not.
- **Gate:** none beyond the orchestrator's own judgement. The user gate is the next stage.

### 2. Acceptance

- **Entry:** a reviewed plan.
- **Gate (user):** the user accepts the plan. Every open decision is asked with AskUserQuestion and the answer recorded in the plan. The plan becomes `Accepted`; an RFC becomes `Accepted`, and the decisions it settles are written as ADRs linking to it.
- **Then the spec trio**, each with its existing gate:
  1. `requirements.md`, then the user approves.
  2. `design.md`, then the user approves. ADRs for each significant decision are written during design as `Proposed`, and become `Accepted` when the design is approved.
  3. `tasks.md`, then the user approves.
- **Exit:** approved `requirements.md`, `design.md` and `tasks.md`; ADRs `Accepted`.
- **Artefacts:** accepted `plan.md`, the spec trio, ADRs, the RFC if any.

### 3. Implementation

- **Entry:** approved `tasks.md`, on a feature branch (never `master`).
- **Work:** the orchestrator gives `implementer` subagents non-overlapping chunks of `tasks.md`, plus the spec paths and the relevant ADRs. Implementers do not commit. The orchestrator reviews each chunk before starting the next, then commits the code with the matching `tasks.md` boxes checked in the same commit.
- **Exit:** every task checked, solution builds.
- **Artefacts:** code, tests, commits.

### 4. Code review

- **Entry:** implementation complete.
- **Work:** the `code-reviewer` reviews the branch diff for correctness, `CLAUDE.md` conventions, security and test quality. An implementer fixes the findings; the reviewer re-reviews.
- **Exit:** no unresolved findings.

### 5. Architecture review

- **Entry:** implementation complete. It can run in parallel with code review, since both are read-only.
- **Work:** the `architecture-reviewer` checks the diff against `design.md`, the accepted ADRs and RFC, and the Clean Architecture layering rules (Api → Application → Domain, Infrastructure → Application). Findings are fixed and re-reviewed as in stage 4.
- **Exit:** no unresolved findings.

### 6. Testing and verification

- **Entry:** reviews are clear.
- **Work:**
  1. The `test-verifier` runs `dotnet build SmartAppointments.slnx` and `dotnet test SmartAppointments.slnx`, maps every acceptance criterion to the test or tests covering it, and reports gaps. Gaps go back to an implementer.
  2. The orchestrator does the manual end-to-end check (Docker Compose and the `.http` files) and records the result.
  3. Docs are updated: the "currently built" section of `CLAUDE.md`, the index in `docs/specs/README.md`, the `plan.md` status (`Implemented`), and an RFC to `Implemented` once all its specs are.
  4. The orchestrator pushes the branch and raises the PR.
- **Done:** tests pass **and** a PR is raised.

## When an ADR is needed

Write an ADR when a decision:

- is hard to reverse;
- affects more than one feature or service;
- picks between real alternatives; or
- deviates from an existing convention in `CLAUDE.md`.

One decision per ADR, in `docs/adr/NNNN-kebab-title.md`. Written as `Proposed` during design, `Accepted` when the design is approved. An accepted ADR changes only its status line; a new ADR supersedes it instead. Decisions made before ADR-0001 are not backfilled. See [`adr/README.md`](adr/README.md).

## When an RFC is needed

Only for cross-cutting changes:

- a new service;
- a new infrastructure dependency (RabbitMQ, Redis, an outbox);
- a change to an inter-service contract (internal APIs, events) or to the shared libraries (`BuildingBlocks`, `BuildingBlocks.Web`);
- a change touching two or more services.

Everything else needs only a plan, a spec and ADRs. An RFC proposes; the decisions it settles become ADRs that link back to it. One RFC can span several feature specs. See [`rfc/README.md`](rfc/README.md).

## Small changes

A bug fix or a one-file tweak that involves no design decision may skip the plan and the spec. It still goes through code review (stage 4) and verification (stage 6), and still ends in a PR. If a design decision turns up, stop and use the full workflow.

## Which command to run

| Stage | Command | Effect |
|---|---|---|
| 0 Plan | `/plan-new <feature>` | Drafts `plan.md` from the template. |
| 0 Plan | `/rfc-new <title>` | Drafts an RFC with status `Draft`. |
| 1 Review | `/plan-review <feature>` | Runs `plan-reviewer` (and `architecture-reviewer` on the RFC). |
| 2 Acceptance | `/spec-new <feature>` | Writes `requirements.md`, stops for approval. |
| 2 Acceptance | `/spec-design <feature>` | Writes `design.md`, stops for approval. |
| 2 Acceptance | `/adr-new <title>` | Writes an ADR as `Proposed`. Use during design. |
| 2 Acceptance | `/spec-tasks <feature>` | Writes `tasks.md`, stops for approval. |
| 3 Implementation | `/spec-execute <feature>` | Runs `implementer` chunks and commits after review. |
| 4 and 5 Reviews | `/review` | Runs `code-reviewer` and `architecture-reviewer` in parallel. |
| 6 Verification | `/verify <feature>` | Runs `test-verifier`, then guides the manual check. |

## Where things live

| Artefact | Path |
|---|---|
| BRD and FR-IDs | `docs/requirements.md` |
| Plan, requirements, design, tasks | `docs/specs/<feature>/` |
| Templates | `docs/specs/_templates/` |
| ADRs | `docs/adr/` |
| RFCs | `docs/rfc/` |
| Subagents and commands | `.claude/agents/`, `.claude/commands/` |
