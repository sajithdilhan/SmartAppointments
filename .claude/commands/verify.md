---
description: Stage 6 - test-verifier run, manual end-to-end check, doc updates, push and open the PR
argument-hint: <feature-name>
---
Workflow stage 6 (Testing and verification) for the feature: $ARGUMENTS. See `docs/WORKFLOW.md`. Done means tests pass AND a PR is raised. You are the orchestrator.

1. Confirm you are on the feature branch, the tree is clean and reviews (`/review`) finished clean.
2. Spawn the `test-verifier` agent with `model: "sonnet"` passed explicitly: "Build, run the full tests and produce the acceptance-criterion traceability table for docs/specs/<feature>/requirements.md." If it reports failures, untested or partial criteria, spawn `implementer` (sonnet) to fix or add tests, review and commit, and re-run the verifier. Do not proceed with failures; ask the user if a gap is acceptable.
   - **No spec:** for a small change with no `docs/specs/<feature>/` folder, ask the `test-verifier` to build, run the full suite and list the tests covering the changed code instead of an acceptance-criterion table.
3. Manual end-to-end check yourself, covering what unit tests cannot (gateway routes, auth, migrations, config): `Copy-Item .env.example .env` if missing, `docker compose up --build`, then exercise the new endpoints through the gateway on http://localhost:5290 using the `.http` files (add requests for the new endpoints to the relevant `.http` file) or curl: happy path, auth failures (401/403), validation 400, not-found, and any idempotency/concurrency case. Check `/healthz`. Record what you ran and the results. `docker compose down` afterwards. If Docker is unavailable, say so and run the services with `dotnet run` instead.
4. Update docs and commit them: CLAUDE.md "Project status" (factual, matching code), the `docs/specs/README.md` index row (Status Implemented, FR-IDs, links), and `docs/specs/<feature>/plan.md` status (Implemented); if the feature implements an RFC and it was the last of that RFC's specs, set the RFC to Implemented in its file and in `docs/rfc/README.md`. Check all boxes in tasks.md are ticked. With no spec, skip the `plan.md` status and `docs/specs/README.md` index updates, but still update CLAUDE.md if behaviour it describes changed. Add the manual verification notes to the spec if prior specs do that.
5. Push: `git push -u origin <branch>`. Open the PR with `gh pr create --base master` with a title and a body containing: summary, spec/plan/RFC/ADR links, the traceability result (counts, gaps), test totals, manual verification notes, and review outcome. The last line of the body must be exactly `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. Pass the body via a heredoc or file, not an inline quoted string.
6. Report the PR URL and the test totals to the user. Do not merge.

Produces: updated docs, a pushed branch and an open PR. Stops after the PR is raised; merging is the user's call.
