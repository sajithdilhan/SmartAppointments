# <Feature name> — Design

<!--
Copy this file to docs/specs/<feature-name>/design.md.
Write it only after requirements.md is approved. Every decision below should name
the acceptance criteria it satisfies, e.g. "(Req 2.2)" for requirement 2,
criterion 2. If a decision satisfies nothing, either it is unnecessary or the
requirements are incomplete.
Mirror the Auth service's layering and DI wiring — see CLAUDE.md.
Stop here and get approval before writing tasks.md.
-->

## Overview

<!-- A paragraph on the approach and anything non-obvious about it. -->

## Architecture

<!-- Which of the four projects each piece lands in. Keep the dependency
direction right: Api → Application → Domain, Infrastructure → Application. -->

| Layer | Contents |
|---|---|
| `<Service>.Api` | |
| `<Service>.Application` | |
| `<Service>.Domain` | |
| `<Service>.Infrastructure` | |

## Components and interfaces

<!-- The actual types. Name commands, queries, handlers, validators,
abstractions and the concrete implementations, with their signatures. -->

### Endpoints

| Verb / route | Action | Authorization | Dispatches |
|---|---|---|---|

### Commands, queries and handlers

### Abstractions

<!-- Interfaces declared in Application/Abstractions and implemented in
Infrastructure. -->

### Validation

<!-- One FluentValidation validator per command; list the rules and the
criteria they enforce. -->

## Data model

<!-- Entities and value objects, the EF Core configuration (required, max
length, converters, indexes) and the migration that creates it. -->

## Error handling

<!-- Expected failures use Result<T> with Error(Status, Details); unexpected
ones fall through to ExceptionMiddleware. Map each failure to its ActionResult. -->

| Condition | `Error.Status` | Controller result |
|---|---|---|

## Testing strategy

<!-- Which acceptance criteria are covered by which kind of test. Follow the
tests/Auth.Tests style: xUnit + Moq, construct the controller or handler
directly, assert on the concrete IActionResult type. -->

## Open questions

<!-- Delete if none. -->
