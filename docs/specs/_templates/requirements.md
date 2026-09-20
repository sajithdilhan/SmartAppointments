# <Feature name> — Requirements

<!--
Copy this file to docs/specs/<feature-name>/requirements.md.
Derive requirements from docs/requirements.md (the BRD) — cite the FR-ID in each
heading so the trace back to the BRD is explicit. If a requirement has no FR-ID
because it came up during design, mark it "(no FR-ID)" and consider whether the
BRD should be updated too.
Stop here and get approval before writing design.md.
-->

## Introduction

<!-- Two or three sentences: what this feature is, which service owns it, and
which BRD sections it refines. -->

## Requirements

### Requirement 1: <Short name> (FR-XXX-000)

**User Story:** As a <role>, I want <capability>, so that <benefit>.

#### Acceptance Criteria

<!--
EARS form. Each criterion is independently testable and names a concrete status
code, field or behaviour — never "the system SHALL handle errors gracefully".

  WHEN <trigger> THEN the system SHALL <response>
  IF <precondition> THEN the system SHALL <response>
  WHILE <state> the system SHALL <response>
  WHERE <context> the system SHALL <response>
-->

1. WHEN <trigger>, THEN the system SHALL <observable response>.
2. IF <error precondition>, THEN the system SHALL <response, with status code>.

### Requirement 2: <Short name> (FR-XXX-000)

**User Story:** As a <role>, I want <capability>, so that <benefit>.

#### Acceptance Criteria

1. ...

## Out of scope

<!-- Anything a reader might reasonably expect to be here but isn't, and why.
For a retro-fitted spec this is also where code that exists but is unreachable
gets recorded, so nobody mistakes it for a delivered requirement. -->

## Known gaps

<!-- Defects, shortcuts or risks discovered while writing this spec. Each one
should have a matching unchecked item in tasks.md. Delete the section if empty. -->
